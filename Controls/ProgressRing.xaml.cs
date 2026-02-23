using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SelfHealingPipeline.Controls;

public partial class ProgressRing : UserControl
{
    private int _current;
    private int _total;

    public ProgressRing()
    {
        InitializeComponent();
        SizeChanged += (_, _) => DrawArc();
    }

    public void Update(int current, int total)
    {
        _current = current;
        _total = total;
        ValueText.Text = total == current && total > 0 ? $"{current}" : $"{current}/{total}";
        DrawArc();
    }

    private void DrawArc()
    {
        ArcCanvas.Children.Clear();

        if (_total <= 0) return;

        double fraction = (double)_current / _total;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) size = 64;

        double radius = (size / 2) - 4;
        double centerX = size / 2;
        double centerY = size / 2;
        double angle = fraction * 360;

        if (angle <= 0) return;

        var path = new Path
        {
            Stroke = (FindResource("AccentBrush") as Brush) ?? Brushes.Indigo,
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        var startAngle = -90.0;
        var endAngle = startAngle + angle;

        var startRad = startAngle * Math.PI / 180;
        var endRad = endAngle * Math.PI / 180;

        var startPoint = new Point(
            centerX + radius * Math.Cos(startRad),
            centerY + radius * Math.Sin(startRad));

        var endPoint = new Point(
            centerX + radius * Math.Cos(endRad),
            centerY + radius * Math.Sin(endRad));

        var isLargeArc = angle > 180;

        var figure = new PathFigure { StartPoint = startPoint };
        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        path.Data = geometry;

        ArcCanvas.Children.Add(path);
    }
}
