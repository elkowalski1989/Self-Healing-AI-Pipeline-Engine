using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SelfHealingPipeline.Controls;
using SelfHealingPipeline.Engine;
using SelfHealingPipeline.Helpers;
using SelfHealingPipeline.Models;
using SelfHealingPipeline.Persistence;

namespace SelfHealingPipeline.Views;

public partial class RunView : UserControl
{
    private Pipeline? _pipeline;
    private readonly PipelineEngine _engine = new();
    private readonly Dictionary<string, MarkerBadge> _markerBadges = new();
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _elapsedTimer;

    public RunView()
    {
        InitializeComponent();

        LoadButton.Click += OnLoadClick;
        StartButton.Click += OnStartClick;
        PauseButton.Click += OnPauseClick;
        StopButton.Click += OnStopClick;

        _engine.OnLog += (src, msg) => Dispatcher.Invoke(() => AddLog(src, msg));
        _engine.OnIterationChanged += (cur, max) => Dispatcher.Invoke(() =>
        {
            IterationRing.Update(cur, max == 0 ? cur : max);
            IterationLabel.Text = max == 0 ? $"Iteration {cur}" : $"Iteration {cur}/{max}";
        });
        _engine.OnMarkersUpdated += results => Dispatcher.Invoke(() => UpdateMarkers(results));
        _engine.OnStatusChanged += status => Dispatcher.Invoke(() => OnStatusUpdate(status));
        _engine.OnRegressionWarning += show => Dispatcher.Invoke(() =>
            RegressionBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed);
        _engine.OnCostUpdated += cost => Dispatcher.Invoke(() =>
            CostText.Text = cost.ToString());
        _engine.OnPhaseChanged += phase => Dispatcher.Invoke(() =>
            PhaseLabel.Text = phase);

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => ElapsedText.Text = _elapsed.Elapsed.ToString(@"hh\:mm\:ss");

        // Escape key to stop pipeline
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && StopButton.IsEnabled)
            {
                var result = MessageBox.Show("Stop the running pipeline?", "Confirm Stop",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _engine.Cancel();
                    AddLog("Engine", "Stop requested (Escape)...");
                }
            }
        };
    }

    public async Task CheckClaudeAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("claude", "--version", timeoutMs: 5_000);
            if (result.ExitCode != 0)
            {
                ClaudeWarningBanner.Visibility = Visibility.Visible;
                ClaudeWarningText.Text = $"Claude CLI returned exit code {result.ExitCode} — check Settings.";
            }
            else
            {
                ClaudeWarningBanner.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            ClaudeWarningBanner.Visibility = Visibility.Visible;
        }
    }

    public void SetClaudePath(string path) => _engine.SetClaudePath(path);

    public void LoadPipeline(Pipeline pipeline)
    {
        _pipeline = pipeline;
        PipelineNameText.Text = pipeline.Name;
        PipelineDescText.Text = pipeline.Description;
        StartButton.IsEnabled = true;

        MarkerStrip.Items.Clear();
        _markerBadges.Clear();
        foreach (var marker in pipeline.Markers)
        {
            var badge = new MarkerBadge { Margin = new Thickness(0, 0, 8, 8) };
            badge.SetPending(marker.Name, MarkerToDescription(marker));
            _markerBadges[marker.Id] = badge;
            MarkerStrip.Items.Add(badge);
        }

        IterationRing.Update(0, pipeline.MaxIterations);
        AddLog("Engine", $"Pipeline loaded: {pipeline.Name}");
    }

    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Pipeline JSON|*.json",
            Title = "Load Pipeline"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var pipeline = await PipelineStore.LoadAsync(dlg.FileName);
                LoadPipeline(pipeline);
                AddLog("Engine", $"  Target: {pipeline.TargetProjectPath}");
                AddLog("Engine", $"  Steps: {pipeline.Steps.Count}, Markers: {pipeline.Markers.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load pipeline:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null) return;

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        PauseButton.IsEnabled = true;
        LoadButton.IsEnabled = false;
        RegressionBanner.Visibility = Visibility.Collapsed;

        LogPanel.Children.Clear();
        EmptyLogText.Visibility = Visibility.Collapsed;
        PhaseLabel.Text = "";
        CostText.Text = "";
        StatusText.Text = "Running";
        _elapsed.Restart();
        _elapsedTimer.Start();

        foreach (var marker in _pipeline.Markers)
        {
            if (_markerBadges.TryGetValue(marker.Id, out var badge))
                badge.SetPending(marker.Name, MarkerToDescription(marker));
        }

        try
        {
            var session = await _engine.RunAsync(_pipeline);

            try
            {
                await HistoryStore.SaveSessionAsync(session);
            }
            catch (Exception ex)
            {
                AddLog("Engine", $"Warning: Could not save history: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            AddLog("Engine", $"Unexpected error: {ex.Message}");
        }

        _elapsed.Stop();
        _elapsedTimer.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        LoadButton.IsEnabled = true;
        PauseButtonText.Text = "⏸ Pause";
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsPaused)
        {
            _engine.Resume();
            PauseButtonText.Text = "⏸ Pause";
            StatusText.Text = "Running";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        }
        else
        {
            _engine.Pause();
            PauseButtonText.Text = "▶ Resume";
            StatusText.Text = "Paused";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _engine.Cancel();
        AddLog("Engine", "Stop requested...");
    }

    private void OnStatusUpdate(RunStatus status)
    {
        // Clear phase label when pipeline finishes
        if (status != RunStatus.Running)
            PhaseLabel.Text = "";

        StatusText.Text = status switch
        {
            RunStatus.Running => "Running",
            RunStatus.Succeeded => "Succeeded",
            RunStatus.Failed => "Failed",
            RunStatus.Aborted => "Aborted",
            _ => "Unknown"
        };

        StatusText.Foreground = status switch
        {
            RunStatus.Succeeded => (System.Windows.Media.Brush)FindResource("SuccessBrush"),
            RunStatus.Failed => (System.Windows.Media.Brush)FindResource("ErrorBrush"),
            RunStatus.Aborted => (System.Windows.Media.Brush)FindResource("WarningBrush"),
            _ => (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
        };
    }

    private void UpdateMarkers(List<MarkerResult> results)
    {
        foreach (var result in results)
        {
            if (_markerBadges.TryGetValue(result.MarkerId, out var badge))
                badge.Update(result);
        }
    }

    private static string MarkerToDescription(Marker marker)
    {
        if (marker.Type == MarkerType.ExitCode && marker.TargetValue == "0")
            return "must succeed";
        if (marker.Type == MarkerType.ExitCode)
            return $"exit code must be {marker.TargetValue}";
        if (marker.Type == MarkerType.FileExists)
            return "file must exist";

        return marker.Operator switch
        {
            CompareOperator.Equals when marker.TargetValue == "0" => "must be zero",
            CompareOperator.Equals => $"must equal {marker.TargetValue}",
            CompareOperator.GreaterThanOrEqual => $"at least {marker.TargetValue}",
            CompareOperator.GreaterThan => $"more than {marker.TargetValue}",
            CompareOperator.LessThan => $"less than {marker.TargetValue}",
            CompareOperator.LessThanOrEqual => $"at most {marker.TargetValue}",
            CompareOperator.Contains => $"contains \"{marker.TargetValue}\"",
            _ => $"{marker.Operator} {marker.TargetValue}"
        };
    }

    private void AddLog(string source, string message)
    {
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var entry = new LogEntry();
            entry.Set(source, line.TrimEnd('\r'));
            LogPanel.Children.Add(entry);
        }

        LogScroller.ScrollToEnd();

        while (LogPanel.Children.Count > 1000)
            LogPanel.Children.RemoveAt(0);
    }
}
