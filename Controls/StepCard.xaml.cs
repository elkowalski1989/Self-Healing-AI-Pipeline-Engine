using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Controls;

public partial class StepCard : UserControl
{
    public PipelineStep? Step { get; private set; }
    public int StepNumber { get; private set; }

    public event RoutedEventHandler? Selected;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            CardBorder.BorderBrush = value
                ? (FindResource("AccentBrush") as Brush)!
                : (FindResource("BorderBrush") as Brush)!;
            CardBorder.BorderThickness = value ? new Thickness(2) : new Thickness(1);
        }
    }

    public StepCard()
    {
        InitializeComponent();
    }

    public void SetStep(PipelineStep step, int number)
    {
        Step = step;
        StepNumber = number;
        NumberText.Text = number.ToString();
        NameText.Text = step.Name;
        TypeText.Text = step.Type.ToString();

        var detail = step.Type switch
        {
            StepType.Execute => step.Command,
            StepType.Extract => !string.IsNullOrEmpty(step.FilePath) ? step.FilePath : step.Command,
            StepType.Validate => step.Command,
            _ => ""
        };
        DetailText.Text = detail;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Selected?.Invoke(this, new RoutedEventArgs());
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Pressed && Step != null)
        {
            DragDrop.DoDragDrop(this, new DataObject("StepCard", this), DragDropEffects.Move);
        }
    }
}
