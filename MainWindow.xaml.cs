using System.Windows;
using System.Windows.Controls;
using SelfHealingPipeline.Controls;
using SelfHealingPipeline.Models;
using SelfHealingPipeline.Views;

namespace SelfHealingPipeline;

public partial class MainWindow : Window
{
    private readonly SetupView _setupView = new();
    private readonly PipelineEditorView _editorView = new();
    private readonly RunView _runView = new();
    private readonly HistoryView _historyView = new();
    private readonly SettingsView _settingsView = new();
    private SidebarButton _activeNav;
    private UIElement _activeView;

    public MainWindow()
    {
        InitializeComponent();
        _activeNav = NavRun;
        _activeView = _runView;

        // Nav wiring
        NavSetup.Clicked += (_, _) => SwitchView(NavSetup, _setupView);
        NavEdit.Clicked += (_, _) => SwitchView(NavEdit, _editorView);
        NavRun.Clicked += (_, _) => SwitchView(NavRun, _runView);
        NavHistory.Clicked += (_, _) => SwitchView(NavHistory, _historyView);
        NavSettings.Clicked += (_, _) => SwitchView(NavSettings, _settingsView);
        ThemeButton.Clicked += (_, _) => ToggleTheme();

        // When Setup generates a pipeline, load it into editor and run view
        _setupView.PipelineGenerated += OnPipelineGenerated;
        _editorView.PipelineChanged += OnPipelineChanged;

        // When settings change, propagate Claude path
        _settingsView.SettingsChanged += settings =>
            _runView.SetClaudePath(settings.ClaudePath);

        // Apply saved settings on startup
        var initialSettings = _settingsView.GetSettings();
        _runView.SetClaudePath(initialSettings.ClaudePath);

        // Start on Run view
        ContentHost.Children.Add(_runView);

        // Check Claude CLI on startup (non-blocking)
        Loaded += async (_, _) => await _runView.CheckClaudeAsync();
    }

    private void SwitchView(SidebarButton nav, UIElement view)
    {
        // Check for unsaved changes when leaving the editor
        if (_activeView == _editorView && view != _editorView && _editorView.IsDirty)
        {
            var result = MessageBox.Show(
                "You have unsaved changes in the Pipeline Editor. Switch views anyway?",
                "Unsaved Changes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
                return;
        }

        _activeNav.IsActive = false;
        _activeNav = nav;
        nav.IsActive = true;
        _activeView = view;

        ContentHost.Children.Clear();
        ContentHost.Children.Add(view);
    }

    private void OnPipelineGenerated(Pipeline pipeline)
    {
        _editorView.LoadPipeline(pipeline);
        _runView.LoadPipeline(pipeline);

        // Switch to editor so user can review/tweak
        SwitchView(NavEdit, _editorView);
    }

    private void OnPipelineChanged(Pipeline pipeline)
    {
        _runView.LoadPipeline(pipeline);
    }

    private void ToggleTheme()
    {
        if (Application.Current is App app)
        {
            app.ToggleTheme();
            ThemeButton.Icon = app.IsDarkTheme ? "☀" : "🌙";
        }
    }
}
