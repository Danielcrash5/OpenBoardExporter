using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace OpenBoardExporter;

public sealed class MainWindow : Window
{
    private readonly string[] _args;
    private readonly WebView2[] _webViews;
    private readonly WpfTextBox _inputText;
    private readonly WpfListBox _filesList;
    private readonly WpfTextBox _outputText;
    private readonly WpfTextBox _logText;
    private readonly WpfProgressBar _progressBar;
    private readonly TextBlock _progressText;
    private readonly WpfButton _startButton;
    private CoreWebView2Environment? _webViewEnvironment;
    private bool _started;

    public MainWindow(string[] args)
    {
        _args = args;

        Width = 900;
        Height = 650;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "OpenBoard Exporter";

        var root = new Grid
        {
            Margin = new Thickness(12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "OpenBoard .ubx in PDF exportieren",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetColumnSpan(title, 3);
        root.Children.Add(title);

        var inputLabel = new TextBlock { Text = "Dateien:", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(inputLabel, 1);
        Grid.SetColumn(inputLabel, 0);
        root.Children.Add(inputLabel);

        _inputText = new WpfTextBox
        {
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(6),
            Background = System.Windows.Media.Brushes.White
        };
        Grid.SetRow(_inputText, 2);
        Grid.SetColumn(_inputText, 0);
        root.Children.Add(_inputText);

        var browseFilesButton = new WpfButton
        {
            Content = "Dateien wählen",
            Width = 140,
            Margin = new Thickness(0, 0, 0, 8)
        };
        browseFilesButton.Click += (_, _) => SelectInputFiles();
        Grid.SetRow(browseFilesButton, 2);
        Grid.SetColumn(browseFilesButton, 1);
        root.Children.Add(browseFilesButton);

        var clearButton = new WpfButton
        {
            Content = "Liste leeren",
            Width = 120,
            Margin = new Thickness(0, 0, 0, 8)
        };
        clearButton.Click += (_, _) => ClearFiles();
        Grid.SetRow(clearButton, 2);
        Grid.SetColumn(clearButton, 2);
        root.Children.Add(clearButton);

        var outputLabel = new TextBlock { Text = "Ausgabeordner:", Margin = new Thickness(0, 8, 0, 5) };
        Grid.SetRow(outputLabel, 3);
        Grid.SetColumn(outputLabel, 0);
        root.Children.Add(outputLabel);

        _outputText = new WpfTextBox
        {
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(6),
            Background = System.Windows.Media.Brushes.White,
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenBoard-PDF")
        };
        Grid.SetRow(_outputText, 4);
        Grid.SetColumn(_outputText, 0);
        root.Children.Add(_outputText);

        var browseOutputButton = new WpfButton
        {
            Content = "Ordner wählen",
            Width = 140,
            Margin = new Thickness(0, 0, 0, 8)
        };
        browseOutputButton.Click += (_, _) => SelectOutputFolder();
        Grid.SetRow(browseOutputButton, 4);
        Grid.SetColumn(browseOutputButton, 1);
        root.Children.Add(browseOutputButton);

        _startButton = new WpfButton
        {
            Content = "Export starten",
            Width = 120,
            Margin = new Thickness(0, 0, 0, 8),
            IsEnabled = false
        };
        _startButton.Click += async (_, _) => await StartExportAsync();
        Grid.SetRow(_startButton, 4);
        Grid.SetColumn(_startButton, 2);
        root.Children.Add(_startButton);

        var progressLabel = new TextBlock { Text = "Fortschritt:", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(progressLabel, 5);
        Grid.SetColumn(progressLabel, 0);
        root.Children.Add(progressLabel);

        _progressBar = new WpfProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(_progressBar, 5);
        Grid.SetColumn(_progressBar, 1);
        Grid.SetColumnSpan(_progressBar, 1);
        root.Children.Add(_progressBar);

        _progressText = new TextBlock
        {
            Text = "0%",
            Width = 44,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetRow(_progressText, 5);
        Grid.SetColumn(_progressText, 2);
        root.Children.Add(_progressText);

        _filesList = new WpfListBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 200
        };
        Grid.SetRow(_filesList, 6);
        Grid.SetColumnSpan(_filesList, 3);
        root.Children.Add(_filesList);

        _logText = new WpfTextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            MinHeight = 160,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(246, 248, 250))
        };
        Grid.SetRow(_logText, 7);
        Grid.SetColumnSpan(_logText, 3);
        root.Children.Add(_logText);

        _webViews = Enumerable.Range(0, 2)
            .Select(_ => new WebView2
            {
                Width = 1,
                Height = 1,
                Visibility = Visibility.Hidden
            })
            .ToArray();
        foreach (var webView in _webViews)
            root.Children.Add(webView);

        Content = root;
        Loaded += async (_, _) => await StartAsync();
    }

    private void SelectInputFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "OpenBoard-Dateien (*.ubx)|*.ubx|Alle Dateien (*.*)|*.*",
            Multiselect = true,
            Title = "OpenBoard-Dateien auswählen"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var files = dialog.FileNames
            .Where(x => string.Equals(Path.GetExtension(x), ".ubx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Log("Keine .ubx-Dateien ausgewählt.");
            return;
        }

        _filesList.ItemsSource = files;
        _inputText.Text = string.Join("; ", files);
        _startButton.IsEnabled = true;
        Log($"{files.Length} Datei(en) ausgewählt.");
    }

    private void ClearFiles()
    {
        _filesList.ItemsSource = null;
        _inputText.Text = string.Empty;
        _startButton.IsEnabled = false;
        Log("Dateiliste geleert.");
    }

    private void SelectOutputFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Ausgabeordner wählen",
            UseDescriptionForTitle = true,
            SelectedPath = _outputText.Text
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _outputText.Text = dialog.SelectedPath;
    }

    private async Task StartAsync()
    {
        if (_started)
            return;

        _started = true;

        try
        {
            if (_args.Length > 0)
            {
                var options = CommandLineOptions.Parse(_args);
                if (options.ShowHelp)
                {
                    Log(CommandLineOptions.Help);
                    Close();
                    return;
                }

                if (options.InputFiles.Count == 0)
                {
                    Log(CommandLineOptions.Help);
                    Close();
                    return;
                }

                _outputText.Text = options.OutputDirectory;
                _filesList.ItemsSource = options.InputFiles;
                _inputText.Text = string.Join("; ", options.InputFiles);
                _startButton.IsEnabled = true;
                await StartExportAsync();
                return;
            }

            Log("Bereit. Wähle .ubx-Dateien und starte den Export.");
        }
        catch (Exception ex)
        {
            Log($"FEHLER: {ex}");
            Close();
        }
    }

    private async Task StartExportAsync()
    {
        var files = _filesList.ItemsSource as IEnumerable<string> ??
            _inputText.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var inputFiles = files
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (inputFiles.Count == 0)
        {
            Log("Bitte zuerst eine oder mehrere .ubx-Dateien auswählen.");
            return;
        }

        var outputDirectory = string.IsNullOrWhiteSpace(_outputText.Text)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenBoard-PDF")
            : _outputText.Text;

        Directory.CreateDirectory(outputDirectory);

        _startButton.IsEnabled = false;
        _progressBar.Value = 0;
        _progressText.Text = "0%";
        Log($"Starte Export für {inputFiles.Count} Datei(en) nach '{outputDirectory}'.");

        try
        {
            await EnsureWebViewInitializedAsync();

            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenBoardExporter", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var renderers = _webViews
                    .Select(webView => new PdfPageRenderer(webView.CoreWebView2, tempRoot))
                    .ToArray();
                var exporter = new UbxExporter(renderers, tempRoot);

                for (int i = 0; i < inputFiles.Count; i++)
                {
                    var fileIndex = i;
                    var input = inputFiles[i];
                    Log($"Verarbeite: {Path.GetFileName(input)} ({i + 1}/{inputFiles.Count})");
                    var progress = new Progress<int>(percent =>
                    {
                        var overallPercent = (fileIndex * 100 + percent) / inputFiles.Count;
                        _progressBar.Value = overallPercent;
                        _progressText.Text = $"{overallPercent}%";
                    });

                    await exporter.ExportAsync(input, outputDirectory, progress, Log);
                }

                _progressBar.Value = 100;
                _progressText.Text = "100%";
                Log("Fertig. Die Dateien wurden exportiert.");
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"FEHLER: {ex}");
        }
        finally
        {
            _startButton.IsEnabled = true;
        }
    }

    private async Task EnsureWebViewInitializedAsync()
    {
        if (_webViewEnvironment is null)
        {
            var webViewDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenBoardExporter",
                "WebView2");
            Directory.CreateDirectory(webViewDataDirectory);

            _webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: webViewDataDirectory);
        }

        foreach (var webView in _webViews)
        {
            if (webView.CoreWebView2 is null)
                await webView.EnsureCoreWebView2Async(_webViewEnvironment);
        }
    }

    private void Log(string message)
    {
        _logText.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _logText.ScrollToEnd();
    }
}
