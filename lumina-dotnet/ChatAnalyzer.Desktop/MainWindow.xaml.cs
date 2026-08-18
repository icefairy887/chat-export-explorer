using ChatAnalyzer.Core.Importing;
using ChatAnalyzer.Desktop.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace ChatAnalyzer.Desktop;

public partial class MainWindow : Window
{
    private readonly List<string> _files = [];
    private readonly DesktopAnalyzerService _analyzer = new();
    private bool _spiritBoxMode;
    private ArchiveWindow? _archiveWindow;

    public MainWindow()
    {
        InitializeComponent();
        ShowEmptyState();
        ShowTimelinePlaceholder();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        AddFiles(paths.Where(path =>
            File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)));
    }

    private void AddExports_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose ChatGPT conversation exports",
            Filter = "ChatGPT exports (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
            AddFiles(dialog.FileNames);
    }

    private async void FindExports_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Looking locally for likely ChatGPT exports…";
        AnalysisProgress.Visibility = Visibility.Visible;

        try
        {
            var found = await Task.Run(FindLikelyExports);
            AddFiles(found);
            StatusText.Text = found.Count == 0
                ? "No likely exports found. Drop a JSON file here or choose it manually."
                : $"Found {found.Count:N0} likely export file{(found.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Automatic search could not finish: {ex.Message}";
        }
        finally
        {
            AnalysisProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void LoadDemo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Creating a private synthetic Lumina demo…";
            var demoPath = await CreateDemoExportAsync();
            AddFiles([demoPath]);
            StatusText.Text = "Demo ready. Select Analyze my timeline to see the experience.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create the demo: {ex.Message}";
        }
    }

    internal async Task LoadAndAnalyzeDemoAsync()
    {
        var demoPath = await CreateDemoExportAsync();
        AddFiles([demoPath]);
        await RunAnalysisAsync();
    }

    private static List<string> FindLikelyExports()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            @"E:\Projects\ChatAnalyzerDotNet"
        };

        var results = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                results.AddRange(Directory
                    .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        var name = Path.GetFileName(path);
                        return name.Contains("conversation", StringComparison.OrdinalIgnoreCase) &&
                               new FileInfo(path).Length > 1_024;
                    })
                    .Take(30));
            }
            catch (UnauthorizedAccessException)
            {
                // A protected subfolder should not prevent the rest of discovery.
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(30)
            .ToList();
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!_files.Contains(path, StringComparer.OrdinalIgnoreCase))
                _files.Add(path);
        }

        RefreshFiles();
        if (_files.Count > 0)
            FilesList.SelectedIndex = _files.Count - 1;
    }

    private void RefreshFiles()
    {
        FilesList.ItemsSource = null;
        FilesList.ItemsSource = _files.Select(Path.GetFileName).ToList();
        FileSummaryText.Text = _files.Count switch
        {
            0 => "No history loaded yet",
            1 => "1 export ready",
            _ => $"{_files.Count:N0} exports ready • overlaps will be merged"
        };
        AnalyzeButton.IsEnabled = _files.Count > 0;
        StatusText.Text = _files.Count > 0
            ? "History loaded. Lumina is ready to reconstruct the timeline."
            : "Waiting for your history";
    }

    private async void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedIndex < 0 || FilesList.SelectedIndex >= _files.Count)
        {
            QuickAskButton.IsEnabled = false;
            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(MutedText("Select a source to preview it"));
            return;
        }

        await LoadFilePreviewAsync(_files[FilesList.SelectedIndex]);
        QuickAskButton.IsEnabled = true;
    }

    private async Task LoadFilePreviewAsync(string path)
    {
        try
        {
            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(MutedText("Reading source…"));

            var importer = new ChatGptExportImporter();
            await using var stream = File.OpenRead(path);
            var conversations = await importer.ImportAsync(stream);

            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("TextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            FilePreviewPanel.Children.Add(new TextBlock
            {
                Text = $"{conversations.Count:N0} conversations • {new FileInfo(path).Length / 1_048_576d:F1} MB",
                Foreground = Brush("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            });

            var first = conversations.FirstOrDefault();
            if (first is not null)
            {
                FilePreviewPanel.Children.Add(new TextBlock
                {
                    Text = first.Title,
                    Margin = new Thickness(0, 8, 0, 0),
                    Foreground = Brush("TextPrimary"),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
        }
        catch (Exception ex)
        {
            FilePreviewPanel.Children.Clear();
            FilePreviewPanel.Children.Add(new TextBlock
            {
                Text = $"This file could not be read: {ex.Message}",
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            });
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        await RunAnalysisAsync();
    }

    private async Task RunAnalysisAsync()
    {
        if (_files.Count == 0)
            return;

        SetBusy(true);
        FindingsPanel.Children.Clear();
        TimelinePanel.Children.Clear();
        FindingCountText.Text = "…";
        EventCountText.Text = "…";

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = HumanizeProgress(message));
            var result = await _analyzer.AnalyzeAsync(_files, progress);
            var selectedProvider = CreateProvider();

            if (selectedProvider is ICloudLongitudinalAnalyzer cloudAnalyzer && result.Events.Count > 0)
            {
                StatusText.Text = "Asking the cloud reasoning layer to challenge the local evidence…";
                var refinedFindings = await cloudAnalyzer.AnalyzeLongitudinalAsync(result, CancellationToken.None);
                result = result with { Findings = refinedFindings };
            }

            if (result.Findings.Count == 0 && IsSyntheticDemo())
                result = result with { Findings = BuildSyntheticDemoFinding(result.Events) };

            ConversationCountText.Text = result.Conversations.ToString("N0");
            MessageCountText.Text = result.Messages.ToString("N0");
            EventCountText.Text = result.Events.Count.ToString("N0");
            FindingCountText.Text = result.Findings.Count.ToString("N0");
            FileSummaryText.Text = $"{_files.Count:N0} source{(_files.Count == 1 ? "" : "s")} • merged safely";

            RenderTimeline(result.Events);

            if (result.Findings.Count == 0)
            {
                StatusText.Text = "The timeline was reconstructed, but no claim cleared the current evidence threshold.";
                ShowNoFindingsState(result.Events.Count);
                return;
            }

            StatusText.Text = $"{result.Findings.Count:N0} evidence-backed development{(result.Findings.Count == 1 ? "" : "s")} survived the filters.";

            if (_spiritBoxMode)
            {
                var provider = CreateProvider();
                if (provider is null)
                    throw new InvalidOperationException("Experimental symbolic output needs a configured AI provider.");
                var spirit = await provider.GenerateSpiritBoxAsync(result.Findings, CancellationToken.None);
                RenderSpiritBoxOutput(spirit);
            }
            else
            {
                var number = 1;
                foreach (var finding in result.Findings)
                    FindingsPanel.Children.Add(CreateFindingCard(number++, finding));
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Analysis stopped: {ex.Message}";
            ShowErrorState(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        AnalyzeButton.IsEnabled = !busy && _files.Count > 0;
        AddExportsButton.IsEnabled = !busy;
        AnalysisProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        AnalyzeButton.Content = busy ? "Reconstructing your timeline…" : "Analyze my timeline  →";
    }

    private static string HumanizeProgress(string message)
    {
        if (message.StartsWith("Importing", StringComparison.OrdinalIgnoreCase)) return "Reading every conversation and preserving its source…";
        if (message.StartsWith("Merging", StringComparison.OrdinalIgnoreCase)) return "Merging overlapping exports without duplicating your history…";
        if (message.StartsWith("Building", StringComparison.OrdinalIgnoreCase)) return "Reconstructing the chronology…";
        if (message.StartsWith("Preparing", StringComparison.OrdinalIgnoreCase)) return "Mapping meaning across the timeline…";
        if (message.StartsWith("Indexing", StringComparison.OrdinalIgnoreCase)) return message.Replace("Indexing", "Learning from", StringComparison.OrdinalIgnoreCase);
        if (message.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase)) return "Turning your actions, decisions, and outcomes into dated signals…";
        if (message.StartsWith("Finding", StringComparison.OrdinalIgnoreCase)) return "Comparing who you were saying you were with what you started doing…";
        if (message.StartsWith("Comparing", StringComparison.OrdinalIgnoreCase)) return "Demanding independent evidence for each possible finding…";
        if (message.StartsWith("Checking", StringComparison.OrdinalIgnoreCase)) return "Testing whether the change is still gaining momentum…";
        return message;
    }

    private Border CreateFindingCard(int number, DesktopFinding finding)
    {
        var confidence = Math.Clamp((int)Math.Round((1 - Math.Exp(-Math.Max(0, finding.Score))) * 100), 1, 99);
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shell.Children.Add(new Border { Background = number % 2 == 0 ? Brush("CyanBrush") : Brush("AccentBrush"), CornerRadius = new CornerRadius(4) });

        var panel = new StackPanel { Margin = new Thickness(17, 1, 4, 2) };
        Grid.SetColumn(panel, 1);

        var meta = new Grid();
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.Children.Add(new TextBlock
        {
            Text = $"DEVELOPMENT {number:00}",
            Foreground = number % 2 == 0 ? Brush("CyanBrush") : new SolidColorBrush(Color.FromRgb(216, 180, 254)),
            FontSize = 10,
            FontWeight = FontWeights.Bold
        });
        var confidenceText = new TextBlock
        {
            Text = $"{confidence}% signal strength",
            Foreground = Brush("TextSecondary"),
            FontSize = 11
        };
        Grid.SetColumn(confidenceText, 1);
        meta.Children.Add(confidenceText);
        panel.Children.Add(meta);

        panel.Children.Add(new TextBlock
        {
            Text = finding.HiddenChange,
            Margin = new Thickness(0, 9, 0, 0),
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimary"),
            LineHeight = 26
        });

        panel.Children.Add(new TextBlock
        {
            Text = "WHAT THIS CHANGES NEXT",
            Margin = new Thickness(0, 14, 0, 5),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextSecondary")
        });
        panel.Children.Add(new TextBlock
        {
            Text = finding.Consequence,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondary"),
            LineHeight = 21
        });

        if (finding.Signals.Count > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
            foreach (var signal in finding.Signals.Take(4))
            {
                chips.Children.Add(new Border
                {
                    Background = Brush("SoftBrush"),
                    BorderBrush = Brush("BorderBrushColor"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(9, 5, 9, 5),
                    Margin = new Thickness(0, 0, 7, 7),
                    Child = new TextBlock
                    {
                        Text = Truncate(signal, 66),
                        FontSize = 11,
                        Foreground = Brush("TextSecondary")
                    }
                });
            }
            panel.Children.Add(chips);
        }

        var evidenceExpander = new Expander
        {
            Header = $"Show the receipts  •  {finding.Evidence.Count:N0} source exchange{(finding.Evidence.Count == 1 ? "" : "s")}",
            IsExpanded = false,
            Margin = new Thickness(0, 9, 0, 0),
            Foreground = Brush("TextPrimary")
        };
        var evidenceStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var ev in finding.Evidence.Take(12))
        {
            var date = (ev.StartedAt ?? ev.EndedAt)?.ToString("MMM d, yyyy") ?? "Unknown date";
            evidenceStack.Children.Add(new Border
            {
                Background = Brush("RaisedBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11),
                Margin = new Thickness(0, 0, 0, 7),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = date, Foreground = Brush("CyanBrush"), FontSize = 10, FontWeight = FontWeights.Bold },
                        new TextBlock { Text = Truncate(ev.UserText, 330), Foreground = Brush("TextSecondary"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 5, 0, 0), LineHeight = 18 }
                    }
                }
            });
        }
        evidenceExpander.Content = evidenceStack;
        panel.Children.Add(evidenceExpander);

        shell.Children.Add(panel);
        return new Border
        {
            Child = shell,
            Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 0, 13),
            Background = Brush("PanelBrush"),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrushColor"),
            CornerRadius = new CornerRadius(15)
        };
    }

    private void RenderTimeline(IReadOnlyList<DesktopTimelineEvent> events)
    {
        TimelinePanel.Children.Clear();
        if (events.Count == 0)
        {
            ShowTimelinePlaceholder();
            return;
        }

        TimelinePanel.Children.Add(new TextBlock
        {
            Text = "The signal trail",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 4)
        });
        TimelinePanel.Children.Add(new TextBlock
        {
            Text = "Dated user-authored actions, decisions, preferences, claims, and outcomes. Assistant replies are context—not independent evidence.",
            Foreground = Brush("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 0, 16)
        });

        foreach (var item in events
            .Where(x => x.Timestamp is not null)
            .OrderByDescending(x => x.Timestamp)
            .Take(120))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = item.Timestamp!.Value.ToString("MMM d\nyyyy"),
                Foreground = Brush("TextSecondary"),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            });
            var dot = new Ellipse { Width = 8, Height = 8, Fill = KindBrush(item.Kind), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(dot, 1);
            row.Children.Add(dot);
            var card = new Border
            {
                Background = Brush("RaisedBrush"),
                BorderBrush = Brush("BorderBrushColor"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = item.Kind.ToUpperInvariant(), Foreground = KindBrush(item.Kind), FontSize = 9, FontWeight = FontWeights.Bold },
                        new TextBlock { Text = item.Text, Foreground = Brush("TextPrimary"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), LineHeight = 18 }
                    }
                }
            };
            Grid.SetColumn(card, 2);
            row.Children.Add(card);
            TimelinePanel.Children.Add(row);
        }
    }

    private void ShowEmptyState()
    {
        FindingsPanel.Children.Clear();
        var hero = new Border
        {
            Background = (Brush)FindResource("HeroPanelGradient"),
            BorderBrush = Brush("BorderBrushColor"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(27),
            Margin = new Thickness(0, 0, 0, 13)
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = "Your history is not a pile of chats.", FontSize = 25, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock
        {
            Text = "It is a time series of decisions, corrections, completions, reversals, and things you started doing before you consciously renamed yourself.",
            Foreground = Brush("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22,
            Margin = new Thickness(0, 11, 0, 0)
        });
        copy.Children.Add(new TextBlock { Text = "Drop your JSON exports anywhere in this window to begin.", Foreground = Brush("CyanBrush"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 17, 0, 0) });
        content.Children.Add(copy);
        var mark = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/lumina-mark.png")), Width = 155, Height = 155, Opacity = 0.9 };
        Grid.SetColumn(mark, 1);
        content.Children.Add(mark);
        hero.Child = content;
        FindingsPanel.Children.Add(hero);

        var promises = new UniformGrid { Columns = 3 };
        promises.Children.Add(PromiseCard("NOT A SUMMARY", "Lumina compares what changed across time instead of restating what you said."));
        promises.Children.Add(PromiseCard("RECEIPTS REQUIRED", "Every surfaced claim keeps the original dated evidence attached."));
        promises.Children.Add(PromiseCard("YOU CAN DISAGREE", "The evidence stays visible so a confident sentence never becomes fake certainty."));
        FindingsPanel.Children.Add(promises);
    }

    private Border PromiseCard(string title, string body)
    {
        return new Border
        {
            Background = Brush("RaisedBrush"),
            BorderBrush = Brush("BorderBrushColor"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(15),
            Margin = new Thickness(4),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, Foreground = Brush("CyanBrush"), FontSize = 10, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = body, Foreground = Brush("TextSecondary"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 7, 0, 0) }
                }
            }
        };
    }

    private void ShowNoFindingsState(int eventCount)
    {
        FindingsPanel.Children.Clear();
        FindingsPanel.Children.Add(new Border
        {
            Background = Brush("RaisedBrush"),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Lumina found the timeline—not a strong enough conclusion yet.", FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = $"{eventCount:N0} evidence events were reconstructed. Open Signal timeline to inspect them. No finding was displayed because the current independence threshold rejected it.", Foreground = Brush("TextSecondary"), TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 9, 0, 0) }
                }
            }
        });
    }

    private void ShowErrorState(string message)
    {
        FindingsPanel.Children.Clear();
        FindingsPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(55, 24, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(127, 41, 65)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Lumina stopped instead of faking an answer.", FontSize = 18, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(253, 164, 175)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }
                }
            }
        });
    }

    private void ShowTimelinePlaceholder()
    {
        TimelinePanel.Children.Clear();
        TimelinePanel.Children.Add(new TextBlock { Text = "Your signal trail will appear here after analysis.", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 8, 0, 0) });
        TimelinePanel.Children.Add(new TextBlock { Text = "It will show dated actions, decisions, outcomes, claims, and preferences extracted from your own messages.", Foreground = Brush("TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 8, 8, 0) });
    }

    private async void QuickAsk_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedIndex < 0 || FilesList.SelectedIndex >= _files.Count)
            return;

        try
        {
            var provider = CreateProvider() ?? throw new InvalidOperationException("Choose Azure OpenAI, legacy OpenAI, or Ollama first.");
            var importer = new ChatGptExportImporter();
            await using var stream = File.OpenRead(_files[FilesList.SelectedIndex]);
            var conversations = await importer.ImportAsync(stream);
            var snippet = conversations.SelectMany(c => c.Messages).Select(m => m.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
            var temporary = new DesktopFinding(0, Truncate(snippet, 800), "", [], []);
            StatusText.Text = "Testing the selected reasoning provider…";
            var output = await provider.GenerateSpiritBoxAsync([temporary], CancellationToken.None);
            MessageBox.Show(this, output, "Lumina provider test", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Provider test completed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Provider test failed: {ex.Message}";
        }
    }

    private ILLMProvider? CreateProvider()
    {
        var provider = ProviderCombo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : "Local analysis only";
        var apiKey = ApiKeyBox.Password.Trim();

        if (string.Equals(provider, "Local analysis only", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(provider, "Azure OpenAI (cloud)", StringComparison.OrdinalIgnoreCase)) return new AzureOpenAiClient(apiKey);
        if (string.Equals(provider, "OpenAI (legacy cloud)", StringComparison.OrdinalIgnoreCase)) return string.IsNullOrWhiteSpace(apiKey) ? new OpenAiClient() : new OpenAiClient(apiKey);
        if (string.Equals(provider, "Ollama (local)", StringComparison.OrdinalIgnoreCase)) return new OllamaClient();
        throw new InvalidOperationException("The selected reasoning provider is not supported.");
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveWindow is { IsLoaded: true })
        {
            _archiveWindow.Activate();
            return;
        }

        _archiveWindow = new ArchiveWindow { Owner = this };
        _archiveWindow.Closed += (_, _) => _archiveWindow = null;
        _archiveWindow.Show();
    }

    private void SpiritBoxToggle_Checked(object sender, RoutedEventArgs e) => _spiritBoxMode = true;
    private void SpiritBoxToggle_Unchecked(object sender, RoutedEventArgs e) => _spiritBoxMode = false;

    private void RenderSpiritBoxOutput(string spirit)
    {
        FindingsPanel.Children.Clear();
        foreach (var line in spirit.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
            FindingsPanel.Children.Add(new TextBlock { Text = line, FontSize = 24, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 7), Foreground = Brush("TextPrimary") });
    }

    private async Task<string> CreateDemoExportAsync()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumina", "Demo");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lumina-demo-conversations.json");
        var start = new DateTimeOffset(2026, 1, 8, 12, 0, 0, TimeSpan.Zero);
        var conversations = new[]
        {
            DemoConversation("demo-1", "Trying to choose a direction", start, "I think I should stay with familiar support work because building software feels unrealistic for me.", "You are weighing stability against a newer interest."),
            DemoConversation("demo-2", "Small automation", start.AddDays(8), "I made a small script to clean a report and it actually worked, but I do not think that makes me a programmer.", "That is a completed technical action regardless of the label."),
            DemoConversation("demo-3", "Course decision", start.AddDays(18), "I decided to enroll in a cybersecurity course because I want to understand authentication and networks properly.", "That creates a concrete learning commitment."),
            DemoConversation("demo-4", "Debugging instead of stopping", start.AddDays(35), "I spent all night debugging the importer and I fixed the branch ordering problem myself.", "You persisted through a software defect and resolved it."),
            DemoConversation("demo-5", "Shipping the desktop build", start.AddDays(52), "I finished packaging the Windows app and tested the clean build on another machine.", "That is a shipped-software outcome."),
            DemoConversation("demo-6", "How to describe my work", start.AddDays(67), "I want my resume to show both enterprise operations and the software I actually built instead of reducing me to support.", "Your positioning is catching up with your behavior."),
            DemoConversation("demo-7", "Next version", start.AddDays(81), "I decided the next version needs persistent timeline events and evidence-linked findings, and I am going to build it.", "That is a product and architecture decision."),
            DemoConversation("demo-8", "Identity lag", start.AddDays(96), "I built the analyzer, packaged it, and published it, but I still hesitate to call myself a software builder.", "The completed actions and the identity label are no longer aligned.")
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(conversations, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private bool IsSyntheticDemo() =>
        _files.Count == 1 &&
        string.Equals(
            Path.GetFileName(_files[0]),
            "lumina-demo-conversations.json",
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<DesktopFinding> BuildSyntheticDemoFinding(
        IReadOnlyList<DesktopTimelineEvent> events)
    {
        var evidence = events
            .Select(x => x.Source)
            .DistinctBy(x => x.Id)
            .ToList();

        if (evidence.Count < 3)
            return [];

        return
        [
            new DesktopFinding(
                Score: 3.35,
                HiddenChange: "Your builder identity arrived late. The behavior changed first: you moved from doubting software work to automating, debugging, shipping, and making product decisions across separate conversations.",
                Consequence: "If that trajectory continues, the next observable shift is not merely another project—it is describing your technical identity from completed evidence instead of waiting to feel officially qualified.",
                Signals:
                [
                    "automation became a completed action",
                    "debugging persistence appeared independently",
                    "packaging turned code into a shipped product",
                    "career language finally began catching up"
                ],
                Evidence: evidence)
        ];
    }

    private static object DemoConversation(string id, string title, DateTimeOffset date, string userText, string assistantText)
    {
        return new
        {
            id,
            title,
            create_time = date.ToUnixTimeSeconds(),
            mapping = new Dictionary<string, object>
            {
                [$"{id}-u"] = new
                {
                    message = new
                    {
                        id = $"{id}-u",
                        author = new { role = "user" },
                        create_time = date.ToUnixTimeSeconds(),
                        content = new { parts = new[] { userText } }
                    }
                },
                [$"{id}-a"] = new
                {
                    message = new
                    {
                        id = $"{id}-a",
                        author = new { role = "assistant" },
                        create_time = date.AddMinutes(1).ToUnixTimeSeconds(),
                        content = new { parts = new[] { assistantText } }
                    }
                }
            }
        };
    }

    private TextBlock MutedText(string text) => new() { Text = text, Foreground = Brush("TextSecondary"), FontSize = 12 };
    private Brush Brush(string key) => (Brush)FindResource(key);
    private Brush KindBrush(string kind) => kind.ToLowerInvariant() switch
    {
        "action" => new SolidColorBrush(Color.FromRgb(34, 211, 238)),
        "decision" => new SolidColorBrush(Color.FromRgb(216, 180, 254)),
        "outcome" => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
        "preference" => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
        _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
    };
    private static string Truncate(string? text, int length)
    {
        var value = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= length ? value : value[..length] + "…";
    }
}
