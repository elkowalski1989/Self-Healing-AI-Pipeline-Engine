using System;
using System.Windows.Controls;
using System.Windows.Media;

namespace SelfHealingPipeline.Controls;

public partial class LogEntry : UserControl
{
    public LogEntry()
    {
        InitializeComponent();
    }

    public void Set(string source, string message, DateTime? timestamp = null)
    {
        TimestampText.Text = (timestamp ?? DateTime.Now).ToString("HH:mm:ss");
        SourceText.Text = source;
        MessageText.Text = message;

        var badgeBrushKey = source switch
        {
            "Claude" => "BadgeClaudeBrush",
            "Step" => "BadgeStepBrush",
            _ => "BadgeEngineBrush"
        };
        SourceBadge.Background = (FindResource(badgeBrushKey) as Brush)!;

        // Severity-based text colors
        var lower = message.ToLowerInvariant();
        if (lower.Contains("error") || lower.Contains("fail"))
            MessageText.Foreground = (FindResource("ErrorBrush") as Brush) ?? MessageText.Foreground;
        else if (lower.Contains("warning"))
            MessageText.Foreground = (FindResource("WarningBrush") as Brush) ?? MessageText.Foreground;
        else if (lower.Contains("pass") || lower.Contains("succeed"))
            MessageText.Foreground = (FindResource("SuccessBrush") as Brush) ?? MessageText.Foreground;
    }
}
