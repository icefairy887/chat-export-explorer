using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChatAnalyzer.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;

        var renderArgument = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--render-preview=", StringComparison.OrdinalIgnoreCase));
        var demoArgument = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--demo-preview=", StringComparison.OrdinalIgnoreCase));

        if (renderArgument is null && demoArgument is null)
        {
            window.Show();
            return;
        }

        var outputPath = renderArgument is not null
            ? renderArgument["--render-preview=".Length..].Trim('"')
            : demoArgument!["--demo-preview=".Length..].Trim('"');
        window.Width = 1320;
        window.Height = 850;
        window.Show();

        if (demoArgument is not null)
            await window.LoadAndAnalyzeDemoAsync();

        window.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);

        window.Close();
        Shutdown();
    }
}
