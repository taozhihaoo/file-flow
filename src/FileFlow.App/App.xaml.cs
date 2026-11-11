using System.IO;
using System.Windows;
using FileFlow.Core.Interfaces;
using FileFlow.Core.Models;
using FileFlow.Core.Planning;
using FileFlow.Core.Scanning;
using FileFlow.Infrastructure;
using FileFlow.Infrastructure.Database;
using FileFlow.Infrastructure.FileSystem;
using FileFlow.Infrastructure.Logging;
using FileFlow.Infrastructure.Repositories;
using FileFlow.Infrastructure.Services;
using FileFlow.App.Localization;
using FileFlow.App.Services;
using FileFlow.App.ViewModels;
using FileFlow.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileFlow.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static new App Current => (App)System.Windows.Application.Current;

    public IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("Services are not initialized yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var dataDirectory = ParseDataDirectory(e.Args);
        var paths = new AppPaths(dataDirectory);
        paths.EnsureDirectories();

        SettingsService? settingsServiceRef = null;
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new FileLoggerProvider(paths, () => settingsServiceRef?.Current.LogLevel ?? "Information"));
        });

        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<ISqliteConnectionFactory>(new SQLiteDatabase(paths.DatabasePath));
        services.AddSingleton<DbMigrator>();
        services.AddSingleton<IFileRepository, FileRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<KeyedLockRegistry>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<FileScanner>();
        services.AddSingleton<RenamePlanner>();
        services.AddSingleton<TransferPlanner>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<IImportExportService, ImportExportService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ITaskQueueService, TaskQueueService>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<FilesViewModel>();
        services.AddSingleton<TasksViewModel>();
        services.AddSingleton<TagsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();

        _serviceProvider = services.BuildServiceProvider();
        settingsServiceRef = (SettingsService)_serviceProvider.GetRequiredService<ISettingsService>();

        try
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            logger.LogInformation("FileFlow {Version} starting. Data directory: {DataDirectory}", version, paths.DataDirectory);

            await Task.Run(() => _serviceProvider.GetRequiredService<DbMigrator>().Migrate());
            await settingsServiceRef.LoadAsync();

            // The interface language must be known before any window or view model is created.
            var languageOverride = ParseOption(e.Args, "--lang");
            Loc.Initialize(languageOverride ?? settingsServiceRef.Current.UiLanguage);

            // Optional: index a folder first so the captured screenshots are not empty.
            var seedFolder = ParseOption(e.Args, "--capture-scan");
            if (!string.IsNullOrEmpty(seedFolder))
            {
                if (Directory.Exists(seedFolder))
                {
                    await _serviceProvider.GetRequiredService<ILibraryService>()
                        .ScanDirectoryAsync(seedFolder, new ScanOptions(true, false), null, CancellationToken.None);
                }
                else
                {
                    logger.LogWarning("Capture seed folder does not exist: {Folder}", seedFolder);
                }
            }

            await _serviceProvider.GetRequiredService<ITaskQueueService>().StartAsync();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();

            var captureDirectory = ParseOption(e.Args, "--capture");
            if (!string.IsNullOrEmpty(captureDirectory))
            {
                var captured = await ScreenshotService.CaptureAllAsync(
                    mainWindow,
                    _serviceProvider.GetRequiredService<MainViewModel>(),
                    captureDirectory);
                logger.LogInformation("Captured {Count} screenshot(s) into {Directory}", captured, captureDirectory);
                Shutdown(0);
                return;
            }
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "Startup failed");
            try
            {
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(paths.LogsDirectory, "startup-crash.txt"),
                    ex.ToString());
            }
            catch
            {
                // best effort - never mask the original failure
            }

            MessageBox.Show(
                Loc.Instance["App_StartupFailed"] + "\n\n" + ex.Message
                + "\n\n" + Loc.Instance["App_StartupHint"],
                Loc.Instance["AppName"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        logger?.LogCritical(e.Exception, "Unhandled UI exception");

        MessageBox.Show(
            "An unexpected error occurred:\n\n" + e.Exception.Message + "\n\nThe details were written to the log file.",
            "FileFlow",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>Supports "--datadir &lt;path&gt;" for portable/test runs; the default is %LOCALAPPDATA%\FileFlow.</summary>
    private static string? ParseDataDirectory(string[] args) => ParseOption(args, "--datadir");

    /// <summary>Reads "--name value" from the command line; returns null when absent.</summary>
    private static string? ParseOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
