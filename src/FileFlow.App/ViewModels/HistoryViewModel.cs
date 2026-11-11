using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Localization;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>History page: recent operations with type filter and paging.</summary>
public partial class HistoryViewModel : ObservableObject, IRefreshable
{
    private const int PageSize = 200;

    private static readonly string[] OperationTypes =
    [
        TaskTypeNames.Scan,
        TaskTypeNames.Rename,
        TaskTypeNames.Move,
        TaskTypeNames.Copy,
        TaskTypeNames.Delete,
        TaskTypeNames.ChangeExtension,
        TaskTypeNames.AddTag,
        TaskTypeNames.RemoveTag,
        TaskTypeNames.Import,
        TaskTypeNames.Export,
    ];

    private readonly IHistoryRepository _history;
    private readonly ILogger<HistoryViewModel> _logger;
    private int _offset;
    private bool _suppressReload;

    public HistoryViewModel(IHistoryRepository history, ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _logger = logger;
        BuildTypeFilters();
    }

    public ObservableCollection<OperationRowViewModel> Operations { get; } = new();

    public ObservableCollection<FilterOption> TypeFilters { get; } = new();

    [ObservableProperty]
    private FilterOption? selectedTypeFilter;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private bool canLoadMore;

    /// <summary>Rebuilds the filter labels (they are localized) keeping the current selection.</summary>
    private void BuildTypeFilters()
    {
        var previous = SelectedTypeFilter?.Value ?? string.Empty;
        _suppressReload = true;

        TypeFilters.Clear();
        TypeFilters.Add(new FilterOption(string.Empty, Loc.Instance["History_FilterAll"]));
        foreach (var operationType in OperationTypes)
        {
            TypeFilters.Add(new FilterOption(operationType, DisplayNames.Operation(operationType)));
        }

        SelectedTypeFilter = TypeFilters.FirstOrDefault(o => o.Value == previous) ?? TypeFilters[0];
        _suppressReload = false;
    }

    partial void OnSelectedTypeFilterChanged(FilterOption? value)
    {
        if (_suppressReload)
        {
            return;
        }

        _offset = 0;
        _ = LoadAsync(reset: true);
    }

    public async Task RefreshAsync()
    {
        BuildTypeFilters();
        _offset = 0;
        await LoadAsync(reset: true);
    }

    private async Task LoadAsync(bool reset)
    {
        try
        {
            if (reset)
            {
                Operations.Clear();
            }

            var typeFilter = string.IsNullOrEmpty(SelectedTypeFilter?.Value) ? null : SelectedTypeFilter.Value;
            IReadOnlyList<OperationRecord> records;
            if (typeFilter is not null)
            {
                records = await _history.GetByTypeAsync(typeFilter, PageSize);
                TotalCount = records.Count;
            }
            else
            {
                var (items, total) = await _history.GetRecentAsync(PageSize, _offset);
                records = items;
                TotalCount = total;
            }

            foreach (var record in records)
            {
                Operations.Add(new OperationRowViewModel(record));
            }

            _offset = Operations.Count;
            CanLoadMore = typeFilter is null && Operations.Count < TotalCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the operation history");
        }
    }

    [RelayCommand]
    private Task LoadMoreAsync() => LoadAsync(reset: false);
}
