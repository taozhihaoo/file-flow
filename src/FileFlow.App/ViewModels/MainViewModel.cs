using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.Core.Interfaces;

namespace FileFlow.App.ViewModels;

public sealed record NavItem(string Title, string Glyph, ObservableObject Page);

/// <summary>Shell view model: owns the pages and the navigation state.</summary>
public partial class MainViewModel : ObservableObject
{
    public DashboardViewModel Dashboard { get; }

    public FilesViewModel Files { get; }

    public TasksViewModel Tasks { get; }

    public TagsViewModel Tags { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> Navigation { get; }

    [ObservableProperty]
    private ObservableObject? currentPage;

    [ObservableProperty]
    private string currentPageTitle = string.Empty;

    [ObservableProperty]
    private NavItem? selectedNav;

    public string AppVersion { get; }

    public MainViewModel(
        DashboardViewModel dashboard,
        FilesViewModel files,
        TasksViewModel tasks,
        TagsViewModel tags,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        Dashboard = dashboard;
        Files = files;
        Tasks = tasks;
        Tags = tags;
        History = history;
        Settings = settings;

        Navigation =
        [
            new NavItem("Dashboard", "\uE80F", dashboard),
            new NavItem("Files", "\uE8B7", files),
            new NavItem("Tasks", "\uE9F5", tasks),
            new NavItem("Tags", "\uE8EC", tags),
            new NavItem("History", "\uE81C", history),
            new NavItem("Settings", "\uE713", settings),
        ];

        SelectedNav = Navigation[0];
        AppVersion = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Page;
        CurrentPageTitle = value.Title;
        if (value.Page is IRefreshable refreshable)
        {
            _ = refreshable.RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshCurrentAsync()
    {
        if (CurrentPage is IRefreshable refreshable)
        {
            await refreshable.RefreshAsync();
        }
    }
}
