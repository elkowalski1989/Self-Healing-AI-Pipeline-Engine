using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Controls;

public partial class MarkerBadge : UserControl
{
    public MarkerBadge()
    {
        InitializeComponent();
    }

    public void Update(MarkerResult result)
    {
        NameText.Text = result.MarkerName;
        ValueText.Text = result.Passed ? "passed" : FormatActualValue(result);
        TargetText.Text = result.Passed ? "" : FormatExpectation(result);

        var targetBrush = result.Passed
            ? (FindResource("SuccessBrush") as SolidColorBrush)!
            : (FindResource("ErrorBrush") as SolidColorBrush)!;

        // Animate the status dot color change
        var animation = new ColorAnimation
        {
            To = targetBrush.Color,
            Duration = System.TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var currentBrush = StatusDot.Fill as SolidColorBrush;
        if (currentBrush == null || currentBrush.IsFrozen)
        {
            currentBrush = new SolidColorBrush(currentBrush?.Color ?? Colors.Gray);
            StatusDot.Fill = currentBrush;
        }
        currentBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);

        // Animate border color
        var borderTarget = result.Passed
            ? (FindResource("SuccessBrush") as SolidColorBrush)!.Color
            : (FindResource("ErrorBrush") as SolidColorBrush)!.Color;

        var borderBrush = BadgeBorder.BorderBrush as SolidColorBrush;
        if (borderBrush == null || borderBrush.IsFrozen)
        {
            borderBrush = new SolidColorBrush(borderBrush?.Color ?? Colors.Gray);
            BadgeBorder.BorderBrush = borderBrush;
        }

        var borderAnim = new ColorAnimation
        {
            To = Color.FromArgb(80, borderTarget.R, borderTarget.G, borderTarget.B),
            Duration = System.TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
    }

    public void SetPending(string name, string description)
    {
        NameText.Text = name;
        ValueText.Text = "waiting...";
        TargetText.Text = description;
        StatusDot.Fill = (FindResource("TextMutedBrush") as Brush)!;
    }

    private static string FormatActualValue(MarkerResult result)
    {
        if (string.IsNullOrEmpty(result.ActualValue) || result.ActualValue == "(null)")
            return "no result";
        return $"got: {result.ActualValue}";
    }

    private static string FormatExpectation(MarkerResult result)
    {
        var op = result.Operator switch
        {
            CompareOperator.Equals when result.ExpectedValue == "0" => "expected to succeed",
            CompareOperator.Equals => $"expected {result.ExpectedValue}",
            CompareOperator.GreaterThanOrEqual => $"needs at least {result.ExpectedValue}",
            CompareOperator.GreaterThan => $"needs more than {result.ExpectedValue}",
            CompareOperator.LessThan => $"needs less than {result.ExpectedValue}",
            CompareOperator.LessThanOrEqual => $"needs at most {result.ExpectedValue}",
            CompareOperator.Contains => $"should contain \"{result.ExpectedValue}\"",
            _ => $"expected {result.ExpectedValue}"
        };
        return op;
    }
}
