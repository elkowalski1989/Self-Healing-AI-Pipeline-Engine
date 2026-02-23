using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SelfHealingPipeline.Helpers;

namespace SelfHealingPipeline.Views;

public class AppSettings
{
    public string ClaudePath { get; set; } = "claude";
    public int DefaultMaxIterations { get; set; } = 5;
    public int DefaultStepTimeout { get; set; } = 120;
    public int ClaudeMaxTurns { get; set; } = 20;
}

public partial class SettingsView : UserControl
{
    private AppSettings _settings = new();

    public event Action<AppSettings>? SettingsChanged;

    private static string SettingsFilePath =>
        Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "settings.json");

    public SettingsView()
    {
        InitializeComponent();

        BrowseClaudeButton.Click += OnBrowseClaude;
        TestClaudeButton.Click += OnTestClaude;
        SaveSettingsButton.Click += OnSave;

        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        ClaudePathBox.Text = _settings.ClaudePath;
        DefaultMaxIterBox.Text = _settings.DefaultMaxIterations.ToString();
        DefaultTimeoutBox.Text = _settings.DefaultStepTimeout.ToString();
        MaxTurnsBox.Text = _settings.ClaudeMaxTurns.ToString();
    }

    private void OnBrowseClaude(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executable|*.exe;*.cmd;*.bat|All files|*.*",
            Title = "Select Claude CLI"
        };
        if (dlg.ShowDialog() == true)
            ClaudePathBox.Text = dlg.FileName;
    }

    private async void OnTestClaude(object sender, RoutedEventArgs e)
    {
        TestResultText.Text = "Testing...";
        TestResultText.Foreground = (FindResource("TextSecondaryBrush") as Brush)!;

        try
        {
            var result = await ProcessRunner.RunAsync(
                ClaudePathBox.Text, "--version",
                timeoutMs: 10_000);

            if (result.ExitCode == 0)
            {
                TestResultText.Text = $"OK: {result.Output.Trim()}";
                TestResultText.Foreground = (FindResource("SuccessBrush") as Brush)!;
            }
            else
            {
                TestResultText.Text = $"Failed (exit code {result.ExitCode}): {result.Error.Trim()}";
                TestResultText.Foreground = (FindResource("ErrorBrush") as Brush)!;
            }
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"Error: {ex.Message}";
            TestResultText.Foreground = (FindResource("ErrorBrush") as Brush)!;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.ClaudePath = ClaudePathBox.Text;

        if (int.TryParse(DefaultMaxIterBox.Text, out var maxIter))
            _settings.DefaultMaxIterations = maxIter;
        if (int.TryParse(DefaultTimeoutBox.Text, out var timeout))
            _settings.DefaultStepTimeout = timeout;
        if (int.TryParse(MaxTurnsBox.Text, out var maxTurns))
            _settings.ClaudeMaxTurns = maxTurns;

        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
            SettingsChanged?.Invoke(_settings);

            TestResultText.Text = "Settings saved.";
            TestResultText.Foreground = (FindResource("SuccessBrush") as Brush)!;
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"Save failed: {ex.Message}";
            TestResultText.Foreground = (FindResource("ErrorBrush") as Brush)!;
        }
    }

    public AppSettings GetSettings() => _settings;
}
