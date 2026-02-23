using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SelfHealingPipeline.Agents;
using SelfHealingPipeline.Helpers;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Engine;

public class HealingLoop
{
    private readonly ClaudeCodeAgent _agent = new();
    public event Action<string, string>? OnLog;
    public event Action<ClaudeStreamEvent>? OnClaudeEvent;

    public void SetClaudePath(string path) => _agent.SetClaudePath(path);

    public async Task<(string analysis, List<string> changes)> HealAsync(
        Pipeline pipeline,
        Iteration currentIteration,
        List<Iteration> previousIterations,
        List<ClaudeTranscriptEntry> transcript,
        CancellationToken ct = default)
    {
        OnLog?.Invoke("Engine", $"Invoking Claude for healing (iteration {currentIteration.Number})...");

        var prompt = PromptBuilder.BuildHealingPrompt(
            pipeline, currentIteration, previousIterations, transcript);

        // Wire up streaming events — store handler as local so we can unsubscribe
        Action<ClaudeStreamEvent> handler = evt =>
        {
            switch (evt.Type)
            {
                case "assistant" when evt.Content != null:
                    OnLog?.Invoke("Claude", evt.Content);
                    break;
                case "tool_use":
                    var toolDesc = evt.ToolName ?? "unknown";
                    if (evt.FilePath != null)
                        toolDesc += $": {evt.FilePath}";
                    OnLog?.Invoke("Claude", $"  [{toolDesc}]");
                    break;
            }
            OnClaudeEvent?.Invoke(evt);
        };

        _agent.OnStreamEvent += handler;

        try
        {
            var result = await _agent.RunAgenticAsync(
                prompt,
                pipeline.TargetProjectPath,
                maxTurns: 20,
                timeoutMs: 300_000,
                ct: ct);

            // Store transcript
            transcript.Add(new ClaudeTranscriptEntry
            {
                Prompt = prompt,
                Response = result.FullResponse,
                Timestamp = DateTime.Now
            });

            var changesSummary = result.ChangesMade.Count > 0
                ? string.Join(", ", result.ChangesMade)
                : "no file changes detected";
            OnLog?.Invoke("Engine", $"Claude finished: {result.ChangesMade.Count} changes ({changesSummary})");

            if (result.TimedOut)
                OnLog?.Invoke("Engine", "WARNING: Claude timed out");

            return (result.FullResponse, result.ChangesMade);
        }
        finally
        {
            _agent.OnStreamEvent -= handler;
        }
    }

    /// <summary>
    /// Checks if markers are regressing (getting worse over consecutive iterations).
    /// Returns true if 3 consecutive declining iterations AND the newest is worse than the first.
    /// </summary>
    public static bool IsRegressing(List<Iteration> iterations)
    {
        if (iterations.Count < 4) return false;

        var recent = iterations.Skip(iterations.Count - 4).ToList();

        int PassCount(Iteration it) => it.MarkerResults.Count(m => m.Passed);

        var count0 = PassCount(recent[0]);
        var count1 = PassCount(recent[1]);
        var count2 = PassCount(recent[2]);
        var count3 = PassCount(recent[3]);

        // Three consecutive worsening results AND newest worse than first
        return count3 < count2 && count2 < count1 && count1 < count0
            && count3 < PassCount(iterations[0]);
    }
}
