using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SelfHealingPipeline.Controls;

public partial class SidebarButton : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SidebarButton),
            new PropertyMetadata("◉", OnIconChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SidebarButton),
            new PropertyMetadata("Label", OnLabelChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(SidebarButton),
            new PropertyMetadata(false, OnIsActiveChanged));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public event RoutedEventHandler? Clicked;

    public SidebarButton()
    {
        InitializeComponent();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Clicked?.Invoke(this, new RoutedEventArgs());
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SidebarButton sb)
            sb.IconText.Text = (string)e.NewValue;
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SidebarButton sb)
            sb.LabelText.Text = (string)e.NewValue;
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SidebarButton sb)
        {
            var active = (bool)e.NewValue;
            var brush = active
                ? sb.FindResource("AccentBrush") as Brush
                : sb.FindResource("TextMutedBrush") as Brush;

            sb.IconText.Foreground = brush!;
            sb.LabelText.Foreground = brush!;
            sb.ButtonBorder.Background = active
                ? (sb.FindResource("CardBrush") as Brush)!
                : Brushes.Transparent;
        }
    }
}
