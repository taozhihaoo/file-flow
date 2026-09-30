using System.Collections;
using System.Windows;
using System.Windows.Controls;
using FileFlow.App.ViewModels;

namespace FileFlow.App.Behaviors;

/// <summary>
/// Syncs a DataGrid's multi-selection into a bindable IList on the view model.
/// DataGrid.SelectedItems has no binding setter, so this attached property bridges it
/// while keeping the view model free of view references.
/// Usage: app:DataGridSelectionBehavior.BindSelection="{Binding SelectedRows}"
/// </summary>
public static class DataGridSelectionBehavior
{
    // OneWay (view -> view model): the target collection is read-only on the view model.
    public static readonly DependencyProperty BindSelectionProperty =
        DependencyProperty.RegisterAttached(
            "BindSelection",
            typeof(IList),
            typeof(DataGridSelectionBehavior),
            new FrameworkPropertyMetadata(null, OnBindSelectionChanged));

    public static IList? GetBindSelection(DependencyObject obj) => (IList?)obj.GetValue(BindSelectionProperty);

    public static void SetBindSelection(DependencyObject obj, IList? value) => obj.SetValue(BindSelectionProperty, value);

    private static void OnBindSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.SelectionChanged -= OnSelectionChanged;
        if (e.NewValue is not null)
        {
            grid.SelectionChanged += OnSelectionChanged;
        }

        SyncSelection(grid);
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelection((DataGrid)sender);

    private static void SyncSelection(DataGrid grid)
    {
        var target = GetBindSelection(grid);
        if (target is null || _syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            target.Clear();
            foreach (var item in grid.SelectedItems)
            {
                target.Add(item);
            }
        }
        finally
        {
            _syncing = false;
        }

        // Let the view model recompute selection-dependent state (counts, details panel, commands).
        if (grid.DataContext is ISelectionConsumer consumer)
        {
            consumer.OnSelectionChanged();
        }

        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private static bool _syncing;
}
