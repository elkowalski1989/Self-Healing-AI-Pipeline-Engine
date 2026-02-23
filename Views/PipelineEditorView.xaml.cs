using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SelfHealingPipeline.Controls;
using SelfHealingPipeline.Models;
using SelfHealingPipeline.Persistence;

namespace SelfHealingPipeline.Views;

public partial class PipelineEditorView : UserControl
{
    private Pipeline _pipeline = new();
    private StepCard? _selectedStepCard;
    private PipelineStep? _selectedStep;
    private bool _suppressEvents;
    private bool _isDirty;

    public event Action<Pipeline>? PipelineChanged;
    public bool IsDirty => _isDirty;

    public PipelineEditorView()
    {
        InitializeComponent();

        LoadButton.Click += OnLoadClick;
        SaveButton.Click += OnSaveClick;
        AddStepButton.Click += OnAddStep;
        DeleteStepButton.Click += OnDeleteStep;
        AddMarkerButton.Click += OnAddMarker;
        AddRuleButton.Click += OnAddRule;

        // Step config change handlers
        StepNameBox.TextChanged += (_, _) => SaveSelectedStep();
        StepCommandBox.TextChanged += (_, _) => SaveSelectedStep();
        StepWorkDirBox.TextChanged += (_, _) => SaveSelectedStep();
        StepOutputKeyBox.TextChanged += (_, _) => SaveSelectedStep();
        StepTimeoutBox.TextChanged += (_, _) => SaveSelectedStep();
        StepFilePathBox.TextChanged += (_, _) => SaveSelectedStep();
        StepPatternBox.TextChanged += (_, _) => SaveSelectedStep();
        StepTypeCombo.SelectionChanged += OnStepTypeChanged;
        StepFailCombo.SelectionChanged += (_, _) => SaveSelectedStep();

        // Pipeline metadata change handlers
        NameBox.TextChanged += (_, _) => { if (!_suppressEvents) _pipeline.Name = NameBox.Text; };
        DescBox.TextChanged += (_, _) => { if (!_suppressEvents) _pipeline.Description = DescBox.Text; };
        TargetPathBox.TextChanged += (_, _) => { if (!_suppressEvents) _pipeline.TargetProjectPath = TargetPathBox.Text; };
        MaxIterBox.TextChanged += (_, _) =>
        {
            if (!_suppressEvents && int.TryParse(MaxIterBox.Text, out var v))
                _pipeline.MaxIterations = v;
        };

        // Drag-drop for step reordering
        StepList.Drop += OnStepListDrop;

        // Ctrl+S to save
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                OnSaveClick(this, new RoutedEventArgs());
            }
        };

        // Auto-focus name box on load
        Loaded += (_, _) => NameBox.Focus();

        // Track dirty state from pipeline metadata changes
        NameBox.TextChanged += (_, _) => { if (!_suppressEvents) _isDirty = true; };
        DescBox.TextChanged += (_, _) => { if (!_suppressEvents) _isDirty = true; };
        TargetPathBox.TextChanged += (_, _) => { if (!_suppressEvents) _isDirty = true; };
        MaxIterBox.TextChanged += (_, _) => { if (!_suppressEvents) _isDirty = true; };
    }

    public void LoadPipeline(Pipeline pipeline)
    {
        _pipeline = pipeline;
        _isDirty = false;
        RefreshUI();
    }

    private void RefreshUI()
    {
        _suppressEvents = true;

        PipelineNameLabel.Text = _pipeline.Name;
        NameBox.Text = _pipeline.Name;
        DescBox.Text = _pipeline.Description;
        TargetPathBox.Text = _pipeline.TargetProjectPath;
        MaxIterBox.Text = _pipeline.MaxIterations.ToString();

        RebuildStepCards();
        RebuildMarkerRows();
        RebuildRuleRows();
        DeselectStep();

        _suppressEvents = false;
    }

    // ─── Step management ──────────────────────────────────────

    private void RebuildStepCards()
    {
        StepList.Children.Clear();
        for (int i = 0; i < _pipeline.Steps.Count; i++)
        {
            var card = new StepCard();
            card.SetStep(_pipeline.Steps[i], i + 1);
            card.Selected += OnStepCardSelected;
            StepList.Children.Add(card);
        }
    }

    private void OnStepCardSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is not StepCard card) return;

        if (_selectedStepCard != null)
            _selectedStepCard.IsSelected = false;

        _selectedStepCard = card;
        card.IsSelected = true;
        _selectedStep = card.Step;

        ShowStepConfig(card.Step!);
    }

    private void ShowStepConfig(PipelineStep step)
    {
        _suppressEvents = true;

        StepConfigPanel.Visibility = Visibility.Visible;
        EmptyConfigText.Visibility = Visibility.Collapsed;

        StepNameBox.Text = step.Name;
        StepTypeCombo.SelectedIndex = (int)step.Type;
        StepCommandBox.Text = step.Command;
        StepWorkDirBox.Text = step.WorkingDir;
        StepOutputKeyBox.Text = step.OutputKey;
        StepTimeoutBox.Text = step.Timeout.ToString();
        StepFailCombo.SelectedIndex = (int)step.FailBehavior;
        StepFilePathBox.Text = step.FilePath;
        StepPatternBox.Text = step.ExtractionPattern;

        ExtractFields.Visibility = step.Type == StepType.Extract
            ? Visibility.Visible
            : Visibility.Collapsed;

        _suppressEvents = false;
    }

    private void DeselectStep()
    {
        if (_selectedStepCard != null)
            _selectedStepCard.IsSelected = false;
        _selectedStepCard = null;
        _selectedStep = null;
        StepConfigPanel.Visibility = Visibility.Collapsed;
        EmptyConfigText.Visibility = Visibility.Visible;
    }

    private void SaveSelectedStep()
    {
        if (_suppressEvents || _selectedStep == null) return;
        _isDirty = true;

        _selectedStep.Name = StepNameBox.Text;
        _selectedStep.Command = StepCommandBox.Text;
        _selectedStep.WorkingDir = StepWorkDirBox.Text;
        _selectedStep.OutputKey = StepOutputKeyBox.Text;
        _selectedStep.FilePath = StepFilePathBox.Text;
        _selectedStep.ExtractionPattern = StepPatternBox.Text;

        if (int.TryParse(StepTimeoutBox.Text, out var t))
            _selectedStep.Timeout = t;

        if (StepTypeCombo.SelectedIndex >= 0)
            _selectedStep.Type = (StepType)StepTypeCombo.SelectedIndex;

        if (StepFailCombo.SelectedIndex >= 0)
            _selectedStep.FailBehavior = (FailBehavior)StepFailCombo.SelectedIndex;

        // Update the card display
        if (_selectedStepCard != null)
        {
            var idx = _pipeline.Steps.IndexOf(_selectedStep);
            _selectedStepCard.SetStep(_selectedStep, idx + 1);
        }
    }

    private void OnStepTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveSelectedStep();

        ExtractFields.Visibility = StepTypeCombo.SelectedIndex == (int)StepType.Extract
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnAddStep(object sender, RoutedEventArgs e)
    {
        var step = new PipelineStep
        {
            Id = $"step_{_pipeline.Steps.Count + 1}",
            Name = $"New Step {_pipeline.Steps.Count + 1}",
            Type = StepType.Execute,
            OutputKey = $"output_{_pipeline.Steps.Count + 1}",
            Timeout = 120
        };
        _pipeline.Steps.Add(step);

        var card = new StepCard();
        card.SetStep(step, _pipeline.Steps.Count);
        card.Selected += OnStepCardSelected;
        StepList.Children.Add(card);

        // Auto-select the new step
        OnStepCardSelected(card, new RoutedEventArgs());
    }

    private void OnDeleteStep(object sender, RoutedEventArgs e)
    {
        if (_selectedStep == null) return;

        _pipeline.Steps.Remove(_selectedStep);
        DeselectStep();
        RebuildStepCards();
    }

    private void OnStepListDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("StepCard")) return;

        var draggedCard = (StepCard)e.Data.GetData("StepCard");
        var draggedStep = draggedCard.Step;
        if (draggedStep == null) return;

        // Determine drop position
        var pos = e.GetPosition(StepList);
        int insertIndex = _pipeline.Steps.Count;

        double yOffset = 0;
        for (int i = 0; i < StepList.Children.Count; i++)
        {
            var child = (FrameworkElement)StepList.Children[i];
            var midY = yOffset + child.ActualHeight / 2;
            if (pos.Y < midY)
            {
                insertIndex = i;
                break;
            }
            yOffset += child.ActualHeight;
        }

        var oldIndex = _pipeline.Steps.IndexOf(draggedStep);
        if (oldIndex == insertIndex) return;

        _pipeline.Steps.Remove(draggedStep);
        if (insertIndex > oldIndex) insertIndex--;
        _pipeline.Steps.Insert(insertIndex, draggedStep);

        RebuildStepCards();
    }

    // ─── Marker management ────────────────────────────────────

    private void RebuildMarkerRows()
    {
        MarkerList.Children.Clear();
        foreach (var marker in _pipeline.Markers)
            MarkerList.Children.Add(CreateMarkerRow(marker));
    }

    private Border CreateMarkerRow(Marker marker)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Background = (FindResource("CardBrush") as Brush)!,
            BorderBrush = (FindResource("BorderBrush") as Brush)!,
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        var nameBox = new TextBox
        {
            Text = marker.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (FindResource("TextPrimaryBrush") as Brush)!,
            Padding = new Thickness(0)
        };
        nameBox.TextChanged += (_, _) => marker.Name = nameBox.Text;

        var detailPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        var typeCombo = new ComboBox { FontSize = 10, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 6, 0) };
        foreach (var mt in Enum.GetValues<MarkerType>())
            typeCombo.Items.Add(mt.ToString());
        typeCombo.SelectedIndex = (int)marker.Type;
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (typeCombo.SelectedIndex >= 0)
                marker.Type = (MarkerType)typeCombo.SelectedIndex;
        };

        var sourceBox = new TextBox
        {
            Text = marker.Source,
            FontSize = 11,
            Width = 140,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Background = (FindResource("InputBrush") as Brush)!,
            Foreground = (FindResource("TextPrimaryBrush") as Brush)!,
            BorderBrush = (FindResource("BorderBrush") as Brush)!,
            BorderThickness = new Thickness(1)
        };
        sourceBox.TextChanged += (_, _) => marker.Source = sourceBox.Text;

        var opCombo = new ComboBox { FontSize = 10, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 6, 0) };
        foreach (var op in Enum.GetValues<CompareOperator>())
            opCombo.Items.Add(op.ToString());
        opCombo.SelectedIndex = (int)marker.Operator;
        opCombo.SelectionChanged += (_, _) =>
        {
            if (opCombo.SelectedIndex >= 0)
                marker.Operator = (CompareOperator)opCombo.SelectedIndex;
        };

        var valueBox = new TextBox
        {
            Text = marker.TargetValue,
            FontSize = 11,
            Width = 80,
            Padding = new Thickness(4, 2, 4, 2),
            Background = (FindResource("InputBrush") as Brush)!,
            Foreground = (FindResource("TextPrimaryBrush") as Brush)!,
            BorderBrush = (FindResource("BorderBrush") as Brush)!,
            BorderThickness = new Thickness(1)
        };
        valueBox.TextChanged += (_, _) => marker.TargetValue = valueBox.Text;

        detailPanel.Children.Add(typeCombo);
        detailPanel.Children.Add(sourceBox);
        detailPanel.Children.Add(opCombo);
        detailPanel.Children.Add(valueBox);

        info.Children.Add(nameBox);
        info.Children.Add(detailPanel);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Delete button
        var delBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent
        };
        delBorder.Child = new TextBlock { Text = "✕", FontSize = 11, Foreground = (FindResource("TextMutedBrush") as Brush)! };
        delBorder.MouseEnter += (_, _) => delBorder.Background = (FindResource("ErrorBrush") as Brush)!;
        delBorder.MouseLeave += (_, _) => delBorder.Background = Brushes.Transparent;
        delBorder.MouseLeftButtonDown += (_, _) =>
        {
            _pipeline.Markers.Remove(marker);
            RebuildMarkerRows();
        };
        Grid.SetColumn(delBorder, 1);
        grid.Children.Add(delBorder);

        row.Child = grid;
        return row;
    }

    private void OnAddMarker(object sender, RoutedEventArgs e)
    {
        var marker = new Marker
        {
            Id = $"marker_{_pipeline.Markers.Count + 1}",
            Name = $"New Marker {_pipeline.Markers.Count + 1}",
            Type = MarkerType.ExitCode,
            Operator = CompareOperator.Equals,
            TargetValue = "0"
        };
        _pipeline.Markers.Add(marker);
        MarkerList.Children.Add(CreateMarkerRow(marker));
    }

    // ─── File access rules ────────────────────────────────────

    private void RebuildRuleRows()
    {
        RuleList.Children.Clear();
        foreach (var rule in _pipeline.FileAccessRules)
            RuleList.Children.Add(CreateRuleRow(rule));
    }

    private Border CreateRuleRow(FileAccessRule rule)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Background = (FindResource("CardBrush") as Brush)!,
            BorderBrush = (FindResource("BorderBrush") as Brush)!,
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var patternBox = new TextBox
        {
            Text = rule.PathPattern,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code,Consolas"),
            Padding = new Thickness(4, 2, 4, 2),
            Background = (FindResource("InputBrush") as Brush)!,
            Foreground = (FindResource("TextPrimaryBrush") as Brush)!,
            BorderBrush = (FindResource("BorderBrush") as Brush)!,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        patternBox.TextChanged += (_, _) => rule.PathPattern = patternBox.Text;
        Grid.SetColumn(patternBox, 0);
        grid.Children.Add(patternBox);

        var levelCombo = new ComboBox
        {
            FontSize = 10,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var lv in Enum.GetValues<AccessLevel>())
            levelCombo.Items.Add(lv.ToString());
        levelCombo.SelectedIndex = (int)rule.AccessLevel;
        levelCombo.SelectionChanged += (_, _) =>
        {
            if (levelCombo.SelectedIndex >= 0)
                rule.AccessLevel = (AccessLevel)levelCombo.SelectedIndex;
        };
        Grid.SetColumn(levelCombo, 1);
        grid.Children.Add(levelCombo);

        var delBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent
        };
        delBorder.Child = new TextBlock { Text = "✕", FontSize = 11, Foreground = (FindResource("TextMutedBrush") as Brush)! };
        delBorder.MouseEnter += (_, _) => delBorder.Background = (FindResource("ErrorBrush") as Brush)!;
        delBorder.MouseLeave += (_, _) => delBorder.Background = Brushes.Transparent;
        delBorder.MouseLeftButtonDown += (_, _) =>
        {
            _pipeline.FileAccessRules.Remove(rule);
            RebuildRuleRows();
        };
        Grid.SetColumn(delBorder, 2);
        grid.Children.Add(delBorder);

        row.Child = grid;
        return row;
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var rule = new FileAccessRule
        {
            PathPattern = "**/*",
            AccessLevel = AccessLevel.Editable
        };
        _pipeline.FileAccessRules.Add(rule);
        RuleList.Children.Add(CreateRuleRow(rule));
    }

    // ─── Load / Save ──────────────────────────────────────────

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
                _pipeline = await PipelineStore.LoadAsync(dlg.FileName);
                RefreshUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Validate pipeline before saving
        var errors = ValidatePipeline();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Validation Errors",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Pipeline JSON|*.json",
            Title = "Save Pipeline",
            FileName = $"{_pipeline.Name}.json"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                await PipelineStore.SaveAsync(_pipeline, dlg.FileName);
                PipelineNameLabel.Text = _pipeline.Name;
                _isDirty = false;
                PipelineChanged?.Invoke(_pipeline);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private List<string> ValidatePipeline()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_pipeline.Name))
            errors.Add("Pipeline name is required.");

        if (_pipeline.Steps.Count == 0)
            errors.Add("At least one step is required.");

        for (int i = 0; i < _pipeline.Steps.Count; i++)
        {
            var step = _pipeline.Steps[i];
            if (string.IsNullOrWhiteSpace(step.Command) && step.Type != StepType.Extract)
                errors.Add($"Step {i + 1} ({step.Name}): command is required.");
            if (step.Timeout <= 0)
                errors.Add($"Step {i + 1} ({step.Name}): timeout must be greater than 0.");
        }

        return errors;
    }

    public Pipeline GetPipeline() => _pipeline;
}
