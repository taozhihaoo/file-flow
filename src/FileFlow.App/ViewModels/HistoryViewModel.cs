using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using Microsoft.Extensions.Logging;

namespace FileFlow.App.ViewModels;

/// <summary>History page: recent operations with type filter and paging.</summary>
public partial class HistoryViewModel : ObservableObject, IRefreshable
{
    private const int PageSize = 200;

    private readonly IHistoryRepository _history;
    private readonly ILogger<HistoryViewModel> _logger;
    private int _offset;

    public HistoryViewModel(IHistoryRepository history, ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _logger = logger;
    }

    public ObservableCollection<OperationRowViewModel> Operations { get; } = new();

    public ObservableCollection<string> TypeFilters { get; } = new()
    {
        "(all)",
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
    };

    [ObservableProperty]
    private string selectedTypeFilter = "(all)";

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private bool canLoadMore;

    partial void OnSelectedTypeFilterChanged(string value)
    {
        _offset = 0;
        _ = LoadAsync(reset: true);
    }

    public async Task RefreshAsync()
    {
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

            var typeFilter = SelectedTypeFilter == "(all)" ? null : SelectedTypeFilter;
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
