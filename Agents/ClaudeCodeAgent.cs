using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SelfHealingPipeline.Agents;

public class ClaudeStreamEvent
{
    public string Type { get; set; } = "";
    public string? Content { get; set; }
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public string? FilePath { get; set; }
    public string? RawJson { get; set; }
}

public class ClaudeAgentResult
{
    public string FullResponse { get; set; } = "";
    public List<string> ChangesMade { get; set; } = new();
    public bool TimedOut { get; set; }
    public int ExitCode { get; set; }
}

public class ClaudeCodeAgent
{
    private string _claudePath = "claude";
    public event Action<ClaudeStreamEvent>? OnStreamEvent;

    public void SetClaudePath(string path) => _claudePath = path;

    /// <summary>
    /// Runs Claude Code in agentic mode with full tool access, streaming events to the UI.
    /// </summary>
    public async Task<ClaudeAgentResult> RunAgenticAsync(
        string prompt,
        string workingDirectory,
        int maxTurns = 20,
        int timeoutMs = 300_000,
        string allowedTools = "Read,Edit,Write,Bash,Glob,Grep",
        CancellationToken ct = default)
    {
        var result = new ClaudeAgentResult();
        var responseBuilder = new StringBuilder();
        var changesList = new List<string>();

        var args = $"-p --verbose --output-format stream-json --allowedTools \"{allowedTools}\" --max-turns {maxTurns}";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _claudePath,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        process.Start();

        // Send prompt via stdin
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        // Stream stdout line by line (NDJSON)
        var readTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var evt = ParseStreamEvent(line);
                if (evt != null)
                {
                    if (evt.Type == "assistant" && evt.Content != null)
                        responseBuilder.Append(evt.Content);

                    if (evt.Type == "result" && evt.Content != null && responseBuilder.Length == 0)
                        responseBuilder.Append(evt.Content);

                    if (evt.Type == "tool_use" && evt.ToolName is "Edit" or "Write")
                    {
                        var desc = evt.FilePath != null
                            ? $"{evt.ToolName}: {evt.FilePath}"
                            : $"{evt.ToolName}";
                        changesList.Add(desc);
                    }

                    OnStreamEvent?.Invoke(evt);
                }
            }
        }, ct);

        // Read stderr concurrently to prevent buffer deadlock
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } _) { }
        }, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await Task.WhenAll(readTask, stderrTask);
            await process.WaitForExitAsync(timeoutCts.Token);
            result.ExitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            result.TimedOut = true;
        }

        result.FullResponse = responseBuilder.ToString();
        result.ChangesMade = changesList;
        return result;
    }

    /// <summary>
    /// Runs Claude Code in single-turn mode for setup/conversational use.
    /// </summary>
    public async Task<string> RunSingleTurnAsync(
        string prompt,
        string? workingDirectory = null,
        int maxTurns = 3,
        CancellationToken ct = default)
    {
        var args = $"-p --verbose --output-format stream-json --max-turns {maxTurns}";
        var responseBuilder = new StringBuilder();
        string? resultText = null;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _claudePath,
            Arguments = args,
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        process.Start();

        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        // Read stdout and stderr concurrently to prevent deadlock
        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var evt = ParseStreamEvent(line);
                if (evt == null) continue;

                if (evt.Type == "assistant" && evt.Content != null)
                    responseBuilder.Append(evt.Content);

                // Fallback: grab the final result text
                if (evt.Type == "result" && evt.Content != null)
                    resultText = evt.Content;
            }
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } _) { }
        }, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(60_000);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        // Use assistant text if available, otherwise fall back to result text
        var response = responseBuilder.ToString();
        if (string.IsNullOrWhiteSpace(response) && resultText != null)
            response = resultText;

        return response;
    }

    private static ClaudeStreamEvent? ParseStreamEvent(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            var type = typeProp.GetString() ?? "";

            var evt = new ClaudeStreamEvent { Type = type, RawJson = jsonLine };

            switch (type)
            {
                case "assistant":
                    if (root.TryGetProperty("message", out var msg))
                    {
                        if (msg.ValueKind == JsonValueKind.String)
                        {
                            evt.Content = msg.GetString();
                        }
                        else if (msg.ValueKind == JsonValueKind.Object)
                        {
                            if (msg.TryGetProperty("content", out var contentArr) &&
                                contentArr.ValueKind == JsonValueKind.Array)
                            {
                                var sb = new StringBuilder();
                                foreach (var block in contentArr.EnumerateArray())
                                {
                                    if (block.TryGetProperty("type", out var bt) &&
                                        bt.GetString() == "text" &&
                                        block.TryGetProperty("text", out var txt))
                                        sb.Append(txt.GetString());
                                }
                                evt.Content = sb.ToString();
                            }
                        }
                    }
                    break;

                case "tool_use":
                    if (root.TryGetProperty("tool", out var tool))
                        evt.ToolName = tool.GetString();
                    if (root.TryGetProperty("input", out var input))
                    {
                        evt.ToolInput = input.GetRawText();
                        if (input.ValueKind == JsonValueKind.Object &&
                            input.TryGetProperty("file_path", out var fp))
                            evt.FilePath = fp.GetString();
                    }
                    break;

                case "tool_result":
                    if (root.TryGetProperty("content", out var resultContent))
                        evt.Content = resultContent.GetRawText();
                    break;

                case "result":
                    if (root.TryGetProperty("result", out var resultVal))
                        evt.Content = resultVal.GetString();
                    break;
            }

            return evt;
        }
        catch
        {
            return new ClaudeStreamEvent { Type = "raw", Content = jsonLine, RawJson = jsonLine };
        }
    }
}
