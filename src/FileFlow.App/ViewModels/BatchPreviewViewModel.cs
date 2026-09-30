using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;

namespace FileFlow.App.ViewModels;

/// <summary>One row in a batch preview: what happens to this path.</summary>
public sealed class PreviewItemViewModel
{
    public required string From { get; init; }

    public required string To { get; init; }

    public string? Error { get; init; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string StatusText => HasError ? Error! : "OK";
}

/// <summary>
/// Shared view model for all batch previews (rename, move, copy, delete).
/// The user sees every affected path with its outcome and must press Confirm to proceed.
/// </summary>
public partial class BatchPreviewViewModel : ObservableObject
{
    public string Title { get; init; } = "Preview";

    public string Description { get; init; } = string.Empty;

    /// <summary>Extra warning shown in orange (e.g. protected directory).</summary>
    public string? Warning { get; init; }

    public bool ShowRuleEditor { get; init; }

    public ObservableCollection<PreviewItemViewModel> Items { get; } = new();

    public int TotalCount => Items.Count;

    public int ErrorCount => Items.Count(i => i.HasError);

    public int ValidCount => TotalCount - ErrorCount;

    public string SummaryText =>
        ErrorCount == 0
            ? $"{ValidCount} file(s) will be processed."
            : $"{ValidCount} of {TotalCount} file(s) can be processed. {ErrorCount} have problems and will be skipped.";

    public string ConfirmButtonText { get; init; } = "Execute";

    public event Action<bool>? RequestClose;

    /// <summary>The validated plan to execute, kept current by rule editing in derived classes.</summary>
    public RenamePlan? CurrentPlan { get; set; }

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }

    public BatchPreviewViewModel()
    {
        ConfirmCommand = new RelayCommand(
            () => RequestClose?.Invoke(true),
            () => ValidCount > 0);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public void ReplaceItems(IEnumerable<PreviewItemViewModel> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(ValidCount));
        OnPropertyChanged(nameof(SummaryText));
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Batch preview with editable rename rules; the preview refreshes as rules change.</summary>
public partial class RenamePreviewViewModel : BatchPreviewViewModel
{
    private readonly Func<RenameRules, RenamePlan> _replan;
    private bool _initialized;

    public RenamePreviewViewModel(RenameRules initialRules, Func<RenameRules, RenamePlan> replan)
    {
        ShowRuleEditor = true;
        ConfirmButtonText = "Rename";
        _replan = replan;
        Prefix = initialRules.Prefix;
        Suffix = initialRules.Suffix;
        FindText = initialRules.FindText;
        ReplaceText = initialRules.ReplaceText;
        UseSequentialNumbers = initialRules.UseSequentialNumbers;
        SequentialStart = initialRules.SequentialStart;
        SequentialPadding = initialRules.SequentialPadding;
        _initialized = true;
        Replan();
    }

    [ObservableProperty]
    private string prefix;

    [ObservableProperty]
    private string suffix;

    [ObservableProperty]
    private string findText;

    [ObservableProperty]
    private string replaceText;

    [ObservableProperty]
    private bool useSequentialNumbers;

    [ObservableProperty]
    private int sequentialStart;

    [ObservableProperty]
    private int sequentialPadding;

    public RenameRules BuildRules() => new()
    {
        Prefix = Prefix,
        Suffix = Suffix,
        FindText = FindText,
        ReplaceText = ReplaceText,
        UseSequentialNumbers = UseSequentialNumbers,
        SequentialStart = SequentialStart,
        SequentialPadding = SequentialPadding,
    };

    private void Replan()
    {
        if (!_initialized)
        {
            // Property setters fire during construction, before all rules are assigned.
            return;
        }

        var plan = _replan(BuildRules());
        CurrentPlan = plan;
        ReplaceItems(plan.Entries.Select(e => new PreviewItemViewModel
        {
            From = e.CurrentName,
            To = e.IsValid ? e.NewName : string.Empty,
            Error = e.Error,
        }));
    }

    partial void OnPrefixChanged(string value) => Replan();

    partial void OnSuffixChanged(string value) => Replan();

    partial void OnFindTextChanged(string value) => Replan();

    partial void OnReplaceTextChanged(string value) => Replan();

    partial void OnUseSequentialNumbersChanged(bool value) => Replan();

    partial void OnSequentialStartChanged(int value) => Replan();

    partial void OnSequentialPaddingChanged(int value) => Replan();
}
