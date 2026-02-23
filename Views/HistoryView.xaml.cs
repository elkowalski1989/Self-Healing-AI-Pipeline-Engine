using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SelfHealingPipeline.Models;
using SelfHealingPipeline.Persistence;

namespace SelfHealingPipeline.Views;

public partial class HistoryView : UserControl
{
    private List<RunSession> _sessions = new();

    public HistoryView()
    {
        InitializeComponent();
        RefreshButton.Click += async (_, _) => await LoadHistoryAsync();
        Loaded += async (_, _) => await LoadHistoryAsync();
    }

    private async System.Threading.Tasks.Task LoadHistoryAsync()
    {
        try
        {
            _sessions = await HistoryStore.LoadAllAsync();
            CountLabel.Text = _sessions.Count == 0
                ? "No runs yet"
                : $"{_sessions.Count} run{(_sessions.Count != 1 ? "s" : "")}";
            RebuildRunCards();
        }
        catch (Exception ex)
        {
            CountLabel.Text = $"Error loading history: {ex.Message}";
        }
    }

    private void RebuildRunCards()
    {
        RunList.Children.Clear();
        EmptyHistoryText.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var session in _sessions)
        {
            RunList.Children.Add(CreateRunCard(session));
        }
    }

    private UIElement CreateRunCard(RunSession session)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = (FindResource("CardBrush") as Brush)!,
            BorderBrush = (FindResource("BorderBrush") as Brush)!,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var outerStack = new StackPanel();

        // Header row: name, status, date
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftStack = new StackPanel();
        leftStack.Children.Add(new TextBlock
        {
            Text = session.PipelineName,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (FindResource("TextPrimaryBrush") as Brush)!
        });

        var metaPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        // Status badge
        var statusBrush = session.Status switch
        {
            RunStatus.Succeeded => (FindResource("SuccessBrush") as Brush)!,
            RunStatus.Failed => (FindResource("ErrorBrush") as Brush)!,
            RunStatus.Aborted => (FindResource("WarningBrush") as Brush)!,
            _ => (FindResource("TextMutedBrush") as Brush)!
        };

        var statusBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 10, 0),
            Background = statusBrush,
            Child = new TextBlock
            {
                Text = session.Status.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            }
        };
        metaPanel.Children.Add(statusBadge);

        // Iteration count
        metaPanel.Children.Add(new TextBlock
        {
            Text = $"{session.Iterations.Count} iteration{(session.Iterations.Count != 1 ? "s" : "")}",
            FontSize = 11,
            Foreground = (FindResource("TextSecondaryBrush") as Brush)!,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        // Duration
        if (session.EndTime.HasValue)
        {
            var duration = session.EndTime.Value - session.StartTime;
            metaPanel.Children.Add(new TextBlock
            {
                Text = duration.TotalMinutes >= 1
                    ? $"{duration.TotalMinutes:F1} min"
                    : $"{duration.TotalSeconds:F0}s",
                FontSize = 11,
                Foreground = (FindResource("TextSecondaryBrush") as Brush)!,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        leftStack.Children.Add(metaPanel);
        Grid.SetColumn(leftStack, 0);
        headerGrid.Children.Add(leftStack);

        // Date on the right
        var dateText = new TextBlock
        {
            Text = session.StartTime.ToString("MMM dd, yyyy  HH:mm"),
            FontSize = 11,
            Foreground = (FindResource("TextMutedBrush") as Brush)!,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(dateText, 1);
        headerGrid.Children.Add(dateText);

        outerStack.Children.Add(headerGrid);

        // Expandable detail section
        var detailPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };

        // Marker progression chart
        if (session.Iterations.Count > 0 && session.Iterations[0].MarkerResults.Count > 0)
        {
            detailPanel.Children.Add(new TextBlock
            {
                Text = "MARKER PROGRESSION",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (FindResource("TextMutedBrush") as Brush)!,
                Margin = new Thickness(0, 0, 0, 6)
            });

            detailPanel.Children.Add(CreateMarkerChart(session));
        }

        // Changes made across all iterations
        var allChanges = session.Iterations
            .SelectMany(it => it.ChangesMade.Select(c => $"Iteration {it.Number}: {c}"))
            .ToList();

        if (allChanges.Count > 0)
        {
            detailPanel.Children.Add(new TextBlock
            {
                Text = "CHANGES MADE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (FindResource("TextMutedBrush") as Brush)!,
                Margin = new Thickness(0, 12, 0, 6)
            });

            foreach (var change in allChanges)
            {
                detailPanel.Children.Add(new TextBlock
                {
                    Text = $"  {change}",
                    FontSize = 11,
                    FontFamily = new FontFamily("Cascadia Code,Consolas"),
                    Foreground = (FindResource("TextSecondaryBrush") as Brush)!,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1)
                });
            }
        }

        // Final marker results
        var lastIteration = session.Iterations.LastOrDefault();
        if (lastIteration != null)
        {
            detailPanel.Children.Add(new TextBlock
            {
                Text = "FINAL MARKER RESULTS",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (FindResource("TextMutedBrush") as Brush)!,
                Margin = new Thickness(0, 12, 0, 6)
            });

            foreach (var mr in lastIteration.MarkerResults)
            {
                var icon = mr.Passed ? "PASS" : "FAIL";
                var fg = mr.Passed
                    ? (FindResource("SuccessBrush") as Brush)!
                    : (FindResource("ErrorBrush") as Brush)!;

                detailPanel.Children.Add(new TextBlock
                {
                    Text = $"  [{icon}] {mr.MarkerName}: {mr.ActualValue} (target: {mr.Operator} {mr.ExpectedValue})",
                    FontSize = 11,
                    FontFamily = new FontFamily("Cascadia Code,Consolas"),
                    Foreground = fg,
                    Margin = new Thickness(0, 1, 0, 1)
                });
            }
        }

        outerStack.Children.Add(detailPanel);
        card.Child = outerStack;

        // Toggle expand on click
        card.MouseLeftButtonDown += (_, _) =>
        {
            detailPanel.Visibility = detailPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        };

        card.MouseEnter += (_, _) => card.Background = (FindResource("CardHoverBrush") as Brush)!;
        card.MouseLeave += (_, _) => card.Background = (FindResource("CardBrush") as Brush)!;

        return card;
    }

    private Canvas CreateMarkerChart(RunSession session)
    {
        var canvas = new Canvas
        {
            Height = 100,
            Margin = new Thickness(0, 0, 0, 8),
            ClipToBounds = true
        };

        // Draw a simple line chart of pass count per iteration
        canvas.Loaded += (_, _) =>
        {
            canvas.Children.Clear();
            var w = canvas.ActualWidth;
            var h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var iterations = session.Iterations;
            if (iterations.Count == 0) return;

            var maxMarkers = iterations.Max(it => it.MarkerResults.Count);
            if (maxMarkers == 0) return;

            int count = iterations.Count;
            double xStep = count > 1 ? (w - 20) / (count - 1) : 0;

            // Background grid lines
            for (int i = 0; i <= maxMarkers; i++)
            {
                double y = h - 10 - (i * (h - 20) / maxMarkers);
                var gridLine = new Line
                {
                    X1 = 10, Y1 = y, X2 = w - 10, Y2 = y,
                    Stroke = (FindResource("BorderBrush") as Brush)!,
                    StrokeThickness = 0.5
                };
                canvas.Children.Add(gridLine);
            }

            // Data points and line
            var points = new List<Point>();
            for (int i = 0; i < count; i++)
            {
                var passed = iterations[i].MarkerResults.Count(m => m.Passed);
                double x = 10 + (i * xStep);
                double y = h - 10 - ((double)passed / maxMarkers * (h - 20));
                points.Add(new Point(x, y));
            }

            // Draw line
            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var line = new Line
                    {
                        X1 = points[i].X, Y1 = points[i].Y,
                        X2 = points[i + 1].X, Y2 = points[i + 1].Y,
                        Stroke = (FindResource("AccentBrush") as Brush)!,
                        StrokeThickness = 2,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    canvas.Children.Add(line);
                }
            }

            // Draw dots
            foreach (var pt in points)
            {
                var dot = new Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = (FindResource("AccentBrush") as Brush)!
                };
                Canvas.SetLeft(dot, pt.X - 3);
                Canvas.SetTop(dot, pt.Y - 3);
                canvas.Children.Add(dot);
            }

            // Axis labels
            for (int i = 0; i < count; i++)
            {
                var label = new TextBlock
                {
                    Text = $"{i + 1}",
                    FontSize = 9,
                    Foreground = (FindResource("TextMutedBrush") as Brush)!
                };
                Canvas.SetLeft(label, 10 + (i * xStep) - 3);
                Canvas.SetTop(label, h - 8);
                canvas.Children.Add(label);
            }
        };

        return canvas;
    }
}
