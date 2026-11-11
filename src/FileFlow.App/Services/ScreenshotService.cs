using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileFlow.App.ViewModels;

namespace FileFlow.App.Services;

/// <summary>
/// Renders every page of the running application to a PNG file.
/// Used by <c>--capture &lt;folder&gt;</c> to regenerate the screenshots in the README
/// in both languages without any external screenshot tooling.
/// </summary>
public static class ScreenshotService
{
    private static readonly string[] FileNames =
    [
        "dashboard",
        "files",
        "tasks",
        "tags",
        "history",
        "settings",
    ];

    public static async Task<int> CaptureAllAsync(Window window, MainViewModel viewModel, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var captured = 0;
        for (var index = 0; index < viewModel.Navigation.Count && index < FileNames.Length; index++)
        {
            var item = viewModel.Navigation[index];
            viewModel.SelectedNav = item;

            if (item.Page is IRefreshable refreshable)
            {
                await refreshable.RefreshAsync();
            }

            await WaitForRenderAsync(window);

            var path = Path.Combine(outputDirectory, FileNames[index] + ".png");
            if (Save(window, path))
            {
                captured++;
            }
        }

        return captured;
    }

    private static async Task WaitForRenderAsync(Window window)
    {
        // Let the layout pass, the async page loads and the animations settle.
        await Task.Delay(150);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static bool Save(Window window, string path)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var target = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            window.UpdateLayout();
            target.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}
