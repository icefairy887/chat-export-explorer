using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using ChatAnalyzer.Core.Models;
using ChatAnalyzer.Desktop.Services;
using ChatAnalyzer.Core.Importing;
using System.Threading;
using System.Diagnostics;
using System;
using System.Collections.Generic;

namespace ChatAnalyzer.Desktop;

public partial class MainWindow : Window
{
    private readonly List<string> _files = new List<string>();
    private readonly DesktopAnalyzerService _analyzer = new DesktopAnalyzerService();
    private bool _spiritBoxMode = false;

    public MainWindow()
    {
        InitializeComponent();
        ShowEmptyState();
    }

    private async void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedIndex < 0 || FilesList.SelectedIndex >= _files.Count)
        {
            QuickAskButton.IsEnabled = false;
            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(new TextBlock { Text = "No file selected", Foreground = (Brush)FindResource("TextSecondary") });
            return;
        }

        var path = _files[FilesList.SelectedIndex];
        await LoadFilePreviewAsync(path);
        QuickAskButton.IsEnabled = true;
    }

    private async Task LoadFilePreviewAsync(string path)
    {
        try
        {
            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(new TextBlock { Text = $"Loading preview...", Foreground = (Brush)FindResource("TextSecondary") });

            var importer = new ChatGptExportImporter();
            await using var stream = File.OpenRead(path);
            var conversations = await importer.ImportAsync(stream);

            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextPrimary") });
            FilePreviewPanel.Children.Add(new TextBlock { Text = $"Conversations: {conversations.Count:N0}", Foreground = (Brush)FindResource("TextSecondary"), Margin = new Thickness(0,4,0,0) });

            if (conversations.Count > 0)
            {
                var c = conversations[0];
                FilePreviewPanel.Children.Add(new TextBlock { Text = $"First conversation: {c.Title}", Margin = new Thickness(0,8,0,0), Foreground = (Brush)FindResource("TextPrimary") });
                var sample = c.Messages.FirstOrDefault()?.Text ?? "(no messages)";
                if (sample.Length > 400) sample = sample.Substring(0, 400) + "…";
                FilePreviewPanel.Children.Add(new TextBlock { Text = sample.Replace("\r\n", " ").Replace("\n", " "), Margin = new Thickness(0,6,0,0), Foreground = (Brush)FindResource("TextSecondary"), TextWrapping = TextWrapping.Wrap });
            }
        }
        catch (Exception ex)
        {
            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(new TextBlock { Text = $"Failed to preview file: {ex.Message}", Foreground = Brushes.OrangeRed });
        }
    }

    private async void QuickAsk_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedIndex < 0 || FilesList.SelectedIndex >= _files.Count)
        {
            MessageBox.Show(this, "Please select a file first.", "No file selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = _files[FilesList.SelectedIndex];
        try
        {
            var importer = new ChatGptExportImporter();
            await using var stream = File.OpenRead(path);
            var conversations = await importer.ImportAsync(stream);
            var snippet = conversations.SelectMany(c => c.Messages).Select(m => m.Text).Where(t => !string.IsNullOrWhiteSpace(t)).FirstOrDefault() ?? "";
            if (snippet.Length > 800) snippet = snippet.Substring(0, 800) + "…";

            var tempFinding = new DesktopFinding(0.0, snippet, string.Empty, new List<string>(), new List<ChatAnalyzer.Core.Models.Exchange>());
            var provider = CreateProvider();
            if (provider is null)
                throw new InvalidOperationException("Quick Ask requires Azure OpenAI, legacy OpenAI, or Ollama. Choose a provider first.");
            StatusText.Text = "Querying LLM for quick insight...";
            var spirit = await provider.GenerateSpiritBoxAsync(new[] { tempFinding }, CancellationToken.None);
            StatusText.Text = "Quick insight received.";

            // Display in a simple dialog
            var win = new Window { Title = "Quick LLM Output", Width = 560, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this };
            var sv = new ScrollViewer { Content = new TextBlock { Text = spirit.Trim(), TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(12) } };
            win.Content = sv;
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"LLM quick ask failed: {ex.Message}", "LLM Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddExports_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select ChatGPT conversation exports", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames)
        {
            if (!_files.Contains(file, StringComparer.OrdinalIgnoreCase)) _files.Add(file);
        }
        RefreshFiles();
    }

    private void RefreshFiles()
    {
        FilesList.ItemsSource = null;
        FilesList.ItemsSource = _files.Select(Path.GetFileName).ToList();
        FileSummaryText.Text = _files.Count == 1 ? "1 export loaded" : $"{_files.Count:N0} exports loaded";
        AnalyzeButton.IsEnabled = _files.Count > 0;
        StatusText.Text = _files.Count > 0 ? "Ready to analyze." : "Load one or more ChatGPT export files to begin.";
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;
        AnalyzeButton.IsEnabled = false; AddExportsButton.IsEnabled = false; FindingsPanel.Children.Clear();
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var result = await _analyzer.AnalyzeAsync(_files, progress);
            var selectedProvider = CreateProvider();
            if (selectedProvider is ICloudLongitudinalAnalyzer cloudAnalyzer && result.Findings.Count > 0)
            {
                StatusText.Text = "Sending bounded evidence packet to Azure for longitudinal review...";
                var refinedFindings = await cloudAnalyzer.AnalyzeLongitudinalAsync(result, CancellationToken.None);
                result = result with { Findings = refinedFindings };
            }
            FileSummaryText.Text = $"{_files.Count:N0} exports • {result.Conversations:N0} conversations • {result.Messages:N0} messages • {result.Events.Count:N0} timeline events";
            FindingsPanel.Children.Clear();
            if (result.Findings.Count == 0)
            {
                StatusText.Text = "Analysis finished, but no finding passed the current evidence threshold."; return;
            }
            StatusText.Text = $"Found {result.Findings.Count:N0} strong longitudinal developments.";
            var number = 1;
            if (_spiritBoxMode)
            {
                try
                {
                    StatusText.Text = "Refining findings with LLM...";
                    ILLMProvider? provider = CreateProvider();
                    if (provider is null)
                        throw new InvalidOperationException("Spirit Box mode requires a configured cloud or Ollama provider.");
                    var spirit = await provider.GenerateSpiritBoxAsync(result.Findings, CancellationToken.None);
                    RenderSpiritBoxOutput(spirit);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"LLM refinement failed: {ex.Message}";
                    foreach (var finding in result.Findings) FindingsPanel.Children.Add(CreateFindingCard(number++, finding));
                }
            }
            else
            {
                foreach (var finding in result.Findings) FindingsPanel.Children.Add(CreateFindingCard(number++, finding));
            }
            if (FindingsPanel.Children.Count == 0) ShowEmptyState();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            AnalyzeButton.IsEnabled = _files.Count > 0; AddExportsButton.IsEnabled = true;
        }
    }

    private Border CreateFindingCard(int number, DesktopFinding finding)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"{number}. Hidden development", FontSize = 13, FontWeight = FontWeights.SemiBold, Opacity = 0.8 });
        panel.Children.Add(new TextBlock { Text = finding.HiddenChange, Margin = new Thickness(0, 8, 0, 0), FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextPrimary") });
        panel.Children.Add(new TextBlock { Text = finding.Consequence, Margin = new Thickness(0, 10, 0, 0), FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondary") });
        if (finding.Signals is not null && finding.Signals.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Key signals", Margin = new Thickness(0, 12, 0, 6), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextPrimary") });
            var shortStack = new StackPanel();
            foreach (var signal in finding.Signals.Take(3)) shortStack.Children.Add(new TextBlock { Text = "• " + signal, Margin = new Thickness(8, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Foreground = (Brush)FindResource("TextSecondary") });
            panel.Children.Add(shortStack);
            var evidenceExpander = new Expander { Header = "Evidence & receipts", IsExpanded = false, Margin = new Thickness(0, 10, 0, 0), Foreground = (Brush)FindResource("TextPrimary") };
            var evidenceStack = new StackPanel();
            foreach (var signal in finding.Signals) evidenceStack.Children.Add(new TextBlock { Text = "— " + signal, Margin = new Thickness(8, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondary") });
            if (finding.Evidence is not null && finding.Evidence.Count > 0)
            {
                evidenceStack.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Opacity = 0.6 });
                foreach (var ev in finding.Evidence.Take(12))
                {
                    var when = ev.StartedAt ?? ev.EndedAt ?? null as DateTimeOffset?;
                    var whenText = when.HasValue ? when.Value.ToString("yyyy-MM-dd") : "Unknown date";
                    var header = new TextBlock { Text = $"{whenText} — conversation {ev.ConversationId}", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 6, 0, 0), Foreground = (Brush)FindResource("TextPrimary") };
                    var snippetSource = !string.IsNullOrWhiteSpace(ev.UserText) ? ev.UserText : ev.AssistantText;
                    var snippet = snippetSource.Length > 240 ? snippetSource.Substring(0, 240) + "…" : snippetSource;
                    var snippetBlock = new TextBlock { Text = snippet.Replace("\r\n", " ").Replace("\n", " "), Margin = new Thickness(12, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondary") };
                    evidenceStack.Children.Add(header); evidenceStack.Children.Add(snippetBlock);
                }
                if (finding.Evidence.Count > 12) evidenceStack.Children.Add(new TextBlock { Text = $"... and {finding.Evidence.Count - 12} more receipts", Margin = new Thickness(8, 6, 0, 0), Foreground = (Brush)FindResource("TextSecondary") });
            }
            evidenceExpander.Content = evidenceStack; panel.Children.Add(evidenceExpander);
        }
        var detailsExpander = new Expander { Header = "Details (advanced)", IsExpanded = false, Margin = new Thickness(0, 10, 0, 0), Foreground = (Brush)FindResource("TextPrimary") };
        var detailsStack = new StackPanel(); detailsStack.Children.Add(new TextBlock { Text = $"Internal score: {finding.Score:F3}", FontSize = 12, Foreground = (Brush)FindResource("TextSecondary") });
        if (finding.Evidence is not null && finding.Evidence.Count > 0) detailsStack.Children.Add(new TextBlock { Text = $"Supporting receipts: {finding.Evidence.Count:N0}", FontSize = 12, Foreground = (Brush)FindResource("TextSecondary") });
        detailsExpander.Content = detailsStack; panel.Children.Add(detailsExpander);
        Brush panelBrush = Brushes.Black; Brush borderBrush = Brushes.Gray; try { panelBrush = (Brush)FindResource("PanelBrush"); borderBrush = (Brush)FindResource("BorderBrushColor"); } catch { }
        return new Border { Child = panel, Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 16), Background = panelBrush, BorderThickness = new Thickness(1), BorderBrush = borderBrush, CornerRadius = new CornerRadius(10) };
    }

    private void RenderSpiritBoxOutput(string spirit)
    {
        FindingsPanel.Children.Clear();
        var lines = spirit.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        foreach (var line in lines) FindingsPanel.Children.Add(new TextBlock { Text = line, FontSize = 24, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 6), Foreground = (Brush)FindResource("TextPrimary") });
    }

    private void ShowEmptyState()
    {
        FindingsPanel.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "No findings yet", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextPrimary") });
        panel.Children.Add(new TextBlock { Text = "Load one or more ChatGPT exports and click Analyze. The app will search for subtle longitudinal developments across your conversations.", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondary") });
        panel.Children.Add(new TextBlock { Text = "Tip: findings that rely on multiple independent conversations are stronger and more likely to be surfaced.", Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = (Brush)FindResource("TextSecondary") });
        var card = new Border { Child = panel, Padding = new Thickness(16), Background = (Brush)FindResource("PanelBrush"), BorderBrush = (Brush)FindResource("BorderBrushColor"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 8, 0, 0) };
        FindingsPanel.Children.Add(card);
    }

    private void SpiritBoxToggle_Checked(object sender, RoutedEventArgs e)
    {
        _spiritBoxMode = true; ReRenderFindings();
    }

    private void SpiritBoxToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _spiritBoxMode = false; ReRenderFindings();
    }

    private ILLMProvider? CreateProvider()
    {
        var provider = ProviderCombo?.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : "Local analysis only";
        var apiKey = ApiKeyBox?.Text?.Trim() ?? string.Empty;

        if (string.Equals(provider, "Local analysis only", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        else if (string.Equals(provider, "Azure OpenAI (cloud)", StringComparison.OrdinalIgnoreCase))
        {
            return new AzureOpenAiClient(apiKey);
        }
        else if (string.Equals(provider, "OpenAI (legacy cloud)", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return new OpenAiClient(apiKey);
            return new OpenAiClient();
        }
        else if (string.Equals(provider, "Ollama (local)", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaClient();
        }

        throw new InvalidOperationException("The selected LLM provider is not supported.");
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configuredPath = Environment.GetEnvironmentVariable("LUMINA_ARCHIVE_EXPLORER");
            var bundledPath = Path.Combine(
                AppContext.BaseDirectory,
                "Archive Explorer",
                "Chat Export Explorer.exe");
            var developmentPath = Path.Combine(
                "E:\\Projects\\chat-export-explorer\\dist\\Chat Export Explorer",
                "Chat Export Explorer.exe");

            var exe = new[] { configuredPath, bundledPath, developmentPath }
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

            if (string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show(
                    this,
                    "Could not locate Chat Export Explorer. Install the Lumina Suite package or set LUMINA_ARCHIVE_EXPLORER to its executable path.",
                    "Archive Explorer Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Arguments = string.Empty
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to launch explorer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var model = Path.Combine(current.FullName, "Models", "all-MiniLM-L6-v2", "model.onnx");
            if (File.Exists(model)) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private void ReRenderFindings()
    {
        if (_files.Count == 0) { ShowEmptyState(); return; }
        FindingsPanel.Children.Clear();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Spirit Box mode changed.", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextPrimary") });
        panel.Children.Add(new TextBlock { Text = "Please click ANALYZE to re-run analysis and view findings in the selected mode.", Margin = new Thickness(0,8,0,0), Foreground = (Brush)FindResource("TextSecondary"), TextWrapping = TextWrapping.Wrap });
        FindingsPanel.Children.Add(new Border { Child = panel, Padding = new Thickness(12), Background = (Brush)FindResource("PanelBrush"), BorderBrush = (Brush)FindResource("BorderBrushColor"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0,8,0,0) });
    }
}
