using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Localization;

namespace FileFlow.App.ViewModels;

/// <summary>One sidebar entry. Titles resolve through the localization service so they follow the language.</summary>
public sealed class NavItem : ObservableObject
{
    public NavItem(string titleKey, string subtitleKey, string glyph, ObservableObject page)
    {
        TitleKey = titleKey;
        SubtitleKey = subtitleKey;
        Glyph = glyph;
        Page = page;
    }

    public string TitleKey { get; }

    public string SubtitleKey { get; }

    public string Glyph { get; }

    public ObservableObject Page { get; }

    public string Title => Loc.Instance[TitleKey];

    public string Subtitle => Loc.Instance[SubtitleKey];

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }
}

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
    private string currentPageSubtitle = string.Empty;

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
            new NavItem("Nav_Dashboard", "Sub_Dashboard", "\uE80F", dashboard),
            new NavItem("Nav_Files", "Sub_Files", "\uE8B7", files),
            new NavItem("Nav_Tasks", "Sub_Tasks", "\uE9F5", tasks),
            new NavItem("Nav_Tags", "Sub_Tags", "\uE8EC", tags),
            new NavItem("Nav_History", "Sub_History", "\uE81C", history),
            new NavItem("Nav_Settings", "Sub_Settings", "\uE713", settings),
        ];

        SelectedNav = Navigation[0];
        AppVersion = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Page;
        CurrentPageTitle = value.Title;
        CurrentPageSubtitle = value.Subtitle;
        if (value.Page is IRefreshable refreshable)
        {
            _ = refreshable.RefreshAsync();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Navigation)
        {
            item.RefreshLocalization();
        }

        if (SelectedNav is not null)
        {
            CurrentPageTitle = SelectedNav.Title;
            CurrentPageSubtitle = SelectedNav.Subtitle;
        }

        // Rebuild the generated text on every page (task rows, status lines, filter lists).
        _ = Dashboard.RefreshAsync();
        _ = Files.RefreshLocalizationAsync();
        _ = Tasks.RefreshAsync();
        _ = Tags.RefreshAsync();
        _ = History.RefreshAsync();
        Settings.RefreshLocalization();
    }

    [RelayCommand]
    private async Task RefreshCurrentAsync()
    {
        if (CurrentPage is IRefreshable refreshable)
        {
            await refreshable.RefreshAsync();
        }
    }

    [RelayCommand]
    private void NavigateToFiles() => SelectedNav = Navigation[1];
}
