using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;

namespace ChatAnalyzer.Desktop;

public partial class ArchiveWindow : Window
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _server;
    private Uri? _archiveUri;

    public ArchiveWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await StartArchiveAsync();
    }

    private async Task StartArchiveAsync()
    {
        try
        {
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingDetail.Text = "Starting the private search service.";
            ArchiveStatus.Text = "Local only • starting archive service";

            var serverPath = FindServerExecutable();
            if (serverPath is null)
                throw new FileNotFoundException("The Archive Explorer service is not installed beside Lumina.");

            var port = ReservePort();
            _archiveUri = new Uri($"http://127.0.0.1:{port}/");
            _server = Process.Start(new ProcessStartInfo(serverPath)
            {
                Arguments = $"--port {port}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(serverPath) ?? AppContext.BaseDirectory
            }) ?? throw new InvalidOperationException("Windows did not start the Archive Explorer service.");

            LoadingDetail.Text = "Connecting the archive interface.";
            await WaitForHealthAsync(new Uri(_archiveUri, "health"));

            var webViewData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Lumina",
                "ArchiveWebView");
            Directory.CreateDirectory(webViewData);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewData);
            await ArchiveView.EnsureCoreWebView2Async(environment);
            ArchiveView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            ArchiveView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            ArchiveView.Source = _archiveUri;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ArchiveStatus.Text = "Local only • archive connected";
            _ready.TrySetResult(true);
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingDetail.Text = $"Archive Explorer could not open. {ex.Message}";
            ArchiveStatus.Text = "Archive unavailable • Lumina is still running";
            _ready.TrySetResult(false);
        }
    }

    internal async Task<bool> WaitUntilReadyAsync(TimeSpan timeout) =>
        await _ready.Task.WaitAsync(timeout);

    private static string? FindServerExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("LUMINA_ARCHIVE_SERVER");
        var bundled = Path.Combine(AppContext.BaseDirectory, "Archive Explorer", "Chat Export Explorer Server.exe");
        var development = @"E:\Projects\chat-export-explorer\dist\Chat Export Explorer Server\Chat Export Explorer Server.exe";
        return new[] { configured, bundled, development }
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task WaitForHealthAsync(Uri healthUri)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_server?.HasExited == true)
                throw new InvalidOperationException("The local archive service stopped during startup.");

            try
            {
                using var response = await _http.GetAsync(healthUri);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Startup races are expected until the local listener is ready.
            }
            catch (TaskCanceledException)
            {
                // Retry within the bounded startup window.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("The local archive service did not become ready within 20 seconds.");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveView.CoreWebView2 is not null)
            ArchiveView.Reload();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _http.Dispose();
        if (_server is { HasExited: false })
        {
            try
            {
                _server.Kill(entireProcessTree: true);
                _server.WaitForExit(3000);
            }
            catch
            {
                // Windows will reclaim the loopback-only child when Lumina exits.
            }
        }
        _server?.Dispose();
    }
}
