using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SelfHealingPipeline.Agents;
using SelfHealingPipeline.Controls;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Views;

public partial class SetupView : UserControl
{
    private readonly ClaudeCodeAgent _agent = new();
    private readonly List<(string role, string content)> _conversationHistory = new();
    private readonly DispatcherTimer _typingAnimTimer;
    private int _typingDotIndex;
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private ChatBubble? _streamingBubble;

    public event Action<Pipeline>? PipelineGenerated;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SetupView()
    {
        InitializeComponent();

        SendButton.Click += OnSendClick;
        CopyAllButton.Click += OnCopyAllClick;
        InputBox.KeyDown += OnInputKeyDown;
        InputBox.TextChanged += (_, _) =>
            PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

        _typingAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _typingAnimTimer.Tick += OnTypingAnimTick;

        AddBubble("Claude", "Hi! Give me a project path and tell me what you want to fix or improve.\n\n" +
            "I'll explore the project, figure out how it builds and runs, " +
            "then create a plan with clear success checks. The engine will keep running and fixing code until everything passes.", false);

        // Auto-focus input on load
        Loaded += (_, _) => InputBox.Focus();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isBusy)
        {
            e.Handled = true;
            SendMessage();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && !_isBusy)
        {
            e.Handled = true;
            SendMessage();
        }
        else if (e.Key == Key.Escape && _isBusy)
        {
            e.Handled = true;
            _cts?.Cancel();
            StatusLine.Text = "Cancelling...";
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
            SendMessage();
    }

    private void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        if (_conversationHistory.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var (role, content) in _conversationHistory)
        {
            var label = role == "user" ? "You" : "Claude";
            sb.AppendLine($"{label}:");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        Clipboard.SetText(sb.ToString().TrimEnd());

        // Brief visual feedback — swap button text temporarily
        if (CopyAllButton.Template.FindName("BtnLabel", CopyAllButton) is TextBlock btnLabel)
        {
            var original = btnLabel.Text;
            btnLabel.Text = "Copied!";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                btnLabel.Text = original;
                timer.Stop();
            };
            timer.Start();
        }
    }

    private async void SendMessage()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputBox.Text = "";
        AddBubble("You", text, true);
        _conversationHistory.Add(("user", text));

        _isBusy = true;
        SendButton.IsEnabled = false;
        ShowTypingIndicator(true);

        _cts = new CancellationTokenSource();

        try
        {
            var prompt = BuildAgenticPrompt();

            // Detect target project path from conversation
            var workingDir = DetectProjectPath();

            // Create a streaming bubble for Claude's response
            _streamingBubble = AddBubble("Claude", "", false);
            ShowTypingIndicator(false);

            var responseBuilder = new StringBuilder();

            _agent.OnStreamEvent += OnSetupStreamEvent;

            var result = await _agent.RunAgenticAsync(
                prompt,
                workingDir,
                maxTurns: 15,
                timeoutMs: 180_000,
                allowedTools: "Read,Bash,Glob,Grep",
                ct: _cts.Token);

            _agent.OnStreamEvent -= OnSetupStreamEvent;

            var fullResponse = result.FullResponse;
            if (string.IsNullOrWhiteSpace(fullResponse))
                fullResponse = "(Claude finished exploring but produced no text response)";

            _conversationHistory.Add(("assistant", fullResponse));

            // Check if the response contains a pipeline JSON
            var pipeline = ExtractPipelineJson(fullResponse);
            if (pipeline != null)
            {
                Dispatcher.Invoke(() => AddPipelinePreviewCard(pipeline));
            }
        }
        catch (OperationCanceledException)
        {
            _agent.OnStreamEvent -= OnSetupStreamEvent;
            ShowTypingIndicator(false);
            AddBubble("Claude", "(Cancelled)", false);
        }
        catch (Exception ex)
        {
            _agent.OnStreamEvent -= OnSetupStreamEvent;
            ShowTypingIndicator(false);
            AddBubble("Claude", $"Error: {ex.Message}", false);
        }

        _streamingBubble = null;
        _isBusy = false;
        SendButton.IsEnabled = true;
        StatusLine.Text = "";
    }

    private void OnSetupStreamEvent(ClaudeStreamEvent evt)
    {
        Dispatcher.Invoke(() =>
        {
            switch (evt.Type)
            {
                case "assistant" when !string.IsNullOrEmpty(evt.Content):
                    // Append text to the streaming bubble
                    if (_streamingBubble != null)
                        _streamingBubble.AppendText(evt.Content);
                    ChatScroller.ScrollToEnd();

                    // Update status line based on content hints
                    if (evt.Content.Contains("```json"))
                        StatusLine.Text = "Generating pipeline...";
                    break;

                case "tool_use":
                    // Show tool activity as a small inline indicator
                    var toolDesc = evt.ToolName ?? "tool";
                    if (evt.FilePath != null)
                        toolDesc += $" {evt.FilePath}";
                    else if (evt.ToolName == "Bash" && evt.ToolInput != null)
                    {
                        // Try to show the command
                        try
                        {
                            using var doc = JsonDocument.Parse(evt.ToolInput);
                            if (doc.RootElement.TryGetProperty("command", out var cmd))
                                toolDesc = $"$ {cmd.GetString()}";
                        }
                        catch { }
                    }
                    AddToolIndicator(toolDesc);

                    // Update status line based on tool activity
                    StatusLine.Text = evt.ToolName switch
                    {
                        "Glob" => "Exploring project structure...",
                        "Read" => $"Reading {(evt.FilePath != null ? System.IO.Path.GetFileName(evt.FilePath) : "file")}...",
                        "Grep" => "Searching code...",
                        "Bash" => "Running build test...",
                        _ => $"Using {evt.ToolName}..."
                    };
                    break;
            }
        });
    }

    private void AddToolIndicator(string text)
    {
        var indicator = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (FindResource("InputBrush") as Brush)!,
            BorderBrush = (FindResource("BorderSubtleBrush") as Brush)!,
            BorderThickness = new Thickness(1)
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = "⚡ ",
            FontSize = 10,
            Foreground = (FindResource("AccentBrush") as Brush)!,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = text.Length > 80 ? text[..80] + "..." : text,
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code,Consolas"),
            Foreground = (FindResource("TextMutedBrush") as Brush)!,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 550
        });

        indicator.Child = panel;

        var insertIndex = ChatPanel.Children.Count - 1;
        if (insertIndex < 0) insertIndex = 0;
        ChatPanel.Children.Insert(insertIndex, indicator);
        ChatScroller.ScrollToEnd();
    }

    private string DetectProjectPath()
    {
        // Look through conversation for a project path
        foreach (var (_, content) in _conversationHistory)
        {
            // Look for common path patterns
            var text = content;
            int idx;

            // Absolute Windows path
            idx = text.IndexOf(":\\", StringComparison.Ordinal);
            if (idx > 0)
            {
                var start = idx - 1;
                var end = text.Length;
                // Walk forward to find end of path
                for (int i = idx + 2; i < text.Length; i++)
                {
                    var c = text[i];
                    if (c == ' ' || c == '\n' || c == '\r' || c == '"' || c == '\'' || c == ',' || c == ';')
                    {
                        end = i;
                        break;
                    }
                }
                var path = text[start..end].Trim();
                // If it points to a file, use its directory
                if (path.Contains('.') && !path.EndsWith('\\'))
                {
                    var lastSlash = path.LastIndexOf('\\');
                    if (lastSlash > 0)
                        path = path[..lastSlash];
                }
                if (System.IO.Directory.Exists(path))
                    return path;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private string BuildAgenticPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("""
            You are a PIPELINE SETUP AGENT. You explore a project and output a pipeline JSON.
            You do NOT create files or implement anything — the self-healing engine does that when it runs.

            ## RULES:
            - Do NOT write, edit, or create any files
            - Do NOT implement the user's goal — just plan the pipeline
            - Explore the project (Glob, Read, optionally Bash to test a build command)
            - Output a pipeline JSON — that's your only deliverable

            ## How to work:
            1. Glob/Read to understand the project structure, tech stack, entry points
            2. Figure out what CLI commands can build, run, and verify the project
            3. If the project is a GUI app (WPF, WinForms, Electron, etc.) and the goal requires
               running internal logic: plan for a TestRunner in the pipeline — the healing agent
               will create it on the first iteration when steps fail
            4. Output the pipeline JSON

            ## Key concept — the healing agent creates scaffolding:
            The pipeline runs steps → checks markers → if anything fails, Claude (the healing agent)
            gets full access to read/write/edit files. On the FIRST iteration, if a TestRunner or CLI
            harness doesn't exist yet, the healing agent will create it. You just need to:
            - Include steps that ASSUME the scaffolding exists (e.g. "dotnet run --project TestRunner")
            - Write a detailed HealingPromptTemplate that tells the healing agent WHAT to create
            - The template should describe: what the TestRunner needs to do, what classes/services
              to call, what CLI args it should accept, what output format to print, and what reference
              files to compare against (if any)

            ## HealingPromptTemplate — THIS IS CRITICAL:
            This field is passed to the healing agent every iteration. It must contain:
            - Project tech stack, architecture, namespaces, coding conventions
            - EXACTLY what scaffolding to create if it doesn't exist (TestRunner project, comparison
              scripts, CLI harnesses) — describe the files, what they should reference, what they do
            - What classes/services to call from the TestRunner (specific class names you found)
            - If comparing against reference files: what format, how to compare, where the reference is
            - What files are safe to edit and what to never touch
            - Any domain-specific knowledge the healer needs (e.g., "DCFX files use line-by-line
              constraint format", "the AI service needs API key from env var", etc.)
            Think of it as a detailed briefing document for the engineer who will do the actual work.

            ## GUI apps need a TestRunner:
            If target is a GUI app, the pipeline CANNOT launch a window. Plan steps that use a
            TestRunner console project. In HealingPromptTemplate, describe:
            - For .NET: create TestRunner/TestRunner.csproj (console app, same TFM), add ProjectReference
              to main .csproj, write Program.cs that calls the relevant services directly
            - For Node: create test-runner.js that imports core modules
            - TestRunner should accept CLI args (input paths, reference paths, options)
            - TestRunner should print machine-readable results to stdout and exit 0 on success
            - Include the specific class names and methods you found during exploration

            ## Pipeline JSON schema:
            ```json
            {
              "Name": "short name (3-5 words)",
              "Description": "one sentence",
              "TargetProjectPath": "absolute path to project ROOT",
              "MaxIterations": 5,
              "Steps": [
                {
                  "Id": "unique_id",
                  "Name": "short name",
                  "Type": "Execute or Extract",
                  "Command": "shell command",
                  "WorkingDir": "",
                  "FilePath": "",
                  "ExtractionPattern": "regex for Extract type",
                  "OutputKey": "key_name",
                  "Timeout": 120,
                  "FailBehavior": "Continue or Abort"
                }
              ],
              "Markers": [
                {
                  "Id": "unique_id",
                  "Name": "plain English — 'Build succeeds', 'Accuracy above 90%'",
                  "Type": "ExitCode or Regex or FileExists",
                  "Source": "step_output_key",
                  "Operator": "Equals or GreaterThanOrEqual or Contains",
                  "TargetValue": "0"
                }
              ],
              "FileAccessRules": [
                { "PathPattern": "**/*.cs", "AccessLevel": "Editable" }
              ],
              "HealingPromptTemplate": "DETAILED briefing for the healing agent (see above)"
            }
            ```

            ## Naming:
            - Step/marker names: short, plain English, no technical jargon
            - Pipeline name: 3-5 words. Description: 1 sentence.

            ## Output:
            SHORT summary (2-3 sentences), then ```json pipeline block. No long explanations.
            """);

        sb.AppendLine();
        sb.AppendLine("## CONVERSATION SO FAR:");
        foreach (var (role, content) in _conversationHistory)
            sb.AppendLine($"{role}: {content}");

        return sb.ToString();
    }

    private Pipeline? ExtractPipelineJson(string response)
    {
        var jsonStart = response.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart < 0)
            jsonStart = response.IndexOf("```\n{", StringComparison.Ordinal);
        if (jsonStart < 0) return null;

        var braceStart = response.IndexOf('{', jsonStart);
        if (braceStart < 0) return null;

        // Find matching closing brace
        int depth = 0;
        int jsonEnd = -1;
        for (int i = braceStart; i < response.Length; i++)
        {
            if (response[i] == '{') depth++;
            else if (response[i] == '}') { depth--; if (depth == 0) { jsonEnd = i + 1; break; } }
        }
        if (jsonEnd < 0) return null;

        var json = response[braceStart..jsonEnd];

        try
        {
            return JsonSerializer.Deserialize<Pipeline>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static string MarkerToPlainEnglish(Marker marker)
    {
        // Turn technical marker definitions into readable descriptions
        var name = marker.Name;

        if (marker.Type == MarkerType.ExitCode)
        {
            if (marker.TargetValue == "0")
                return $"{name} succeeds (no errors)";
            return $"{name} exits with code {marker.TargetValue}";
        }

        if (marker.Type == MarkerType.Regex)
        {
            var op = marker.Operator switch
            {
                CompareOperator.GreaterThanOrEqual => "at least",
                CompareOperator.GreaterThan => "more than",
                CompareOperator.LessThan => "less than",
                CompareOperator.LessThanOrEqual => "at most",
                CompareOperator.Equals => "exactly",
                _ => "equals"
            };
            return $"{name} is {op} {marker.TargetValue}";
        }

        if (marker.Type == MarkerType.FileExists)
            return $"{name} — file exists";

        // Fallback
        return $"{name} {marker.Operator} {marker.TargetValue}";
    }

    private ChatBubble AddBubble(string sender, string message, bool isUser)
    {
        var bubble = new ChatBubble();
        bubble.SetMessage(sender, message, isUser);

        var insertIndex = ChatPanel.Children.Count - 1;
        if (insertIndex < 0) insertIndex = 0;
        ChatPanel.Children.Insert(insertIndex, bubble);

        bubble.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        bubble.BeginAnimation(OpacityProperty, fadeIn);

        ChatScroller.ScrollToEnd();
        return bubble;
    }

    private void AddPipelinePreviewCard(Pipeline pipeline)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(8, 4, 8, 4),
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (FindResource("SurfaceBrush") as Brush)!,
            BorderBrush = (FindResource("AccentBrush") as Brush)!,
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = $"Pipeline: {pipeline.Name}",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (FindResource("TextPrimaryBrush") as Brush)!
        });

        stack.Children.Add(new TextBlock
        {
            Text = pipeline.Description,
            FontSize = 12,
            Foreground = (FindResource("TextSecondaryBrush") as Brush)!,
            Margin = new Thickness(0, 4, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        var iterText = pipeline.MaxIterations <= 0 ? "unlimited retries" : $"up to {pipeline.MaxIterations} retries";
        stack.Children.Add(new TextBlock
        {
            Text = iterText,
            FontSize = 11,
            Foreground = (FindResource("TextMutedBrush") as Brush)!,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Steps in plain language
        stack.Children.Add(new TextBlock
        {
            Text = "What it will do:",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (FindResource("TextSecondaryBrush") as Brush)!,
            Margin = new Thickness(0, 0, 0, 2)
        });
        foreach (var step in pipeline.Steps)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"  {pipeline.Steps.IndexOf(step) + 1}. {step.Name}",
                FontSize = 12,
                Foreground = (FindResource("TextSecondaryBrush") as Brush)!
            });
        }

        // Markers in plain English
        if (pipeline.Markers.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "How it knows it worked:",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (FindResource("TextSecondaryBrush") as Brush)!,
                Margin = new Thickness(0, 8, 0, 2)
            });
            foreach (var marker in pipeline.Markers)
            {
                var desc = MarkerToPlainEnglish(marker);
                stack.Children.Add(new TextBlock
                {
                    Text = $"  \u2713 {desc}",
                    FontSize = 12,
                    Foreground = (FindResource("SuccessBrush") as Brush) ?? (FindResource("TextSecondaryBrush") as Brush)!
                });
            }
        }

        var useButtonBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = (FindResource("AccentBrush") as Brush)!,
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand
        };
        useButtonBorder.Child = new TextBlock
        {
            Text = "Review & Edit Pipeline →",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        };

        useButtonBorder.MouseLeftButtonDown += (_, _) => PipelineGenerated?.Invoke(pipeline);
        useButtonBorder.MouseEnter += (_, _) =>
            useButtonBorder.Background = (FindResource("AccentHoverBrush") as Brush)!;
        useButtonBorder.MouseLeave += (_, _) =>
            useButtonBorder.Background = (FindResource("AccentBrush") as Brush)!;

        stack.Children.Add(useButtonBorder);
        card.Child = stack;

        var insertIndex = ChatPanel.Children.Count - 1;
        if (insertIndex < 0) insertIndex = 0;
        ChatPanel.Children.Insert(insertIndex, card);

        card.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        card.BeginAnimation(OpacityProperty, fadeIn);

        ChatScroller.ScrollToEnd();
    }

    private void ShowTypingIndicator(bool show)
    {
        TypingIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _typingDotIndex = 0;
            _typingAnimTimer.Start();
        }
        else
        {
            _typingAnimTimer.Stop();
        }
        ChatScroller.ScrollToEnd();
    }

    private void OnTypingAnimTick(object? sender, EventArgs e)
    {
        var dots = new[] { Dot1, Dot2, Dot3 };
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Opacity = i == _typingDotIndex ? 1.0 : 0.3;
        }
        _typingDotIndex = (_typingDotIndex + 1) % 3;
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }
}
