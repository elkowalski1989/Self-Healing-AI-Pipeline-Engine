using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Helpers;

public static class PromptBuilder
{
    /// <summary>Rough char budget (~80K chars ≈ ~20K tokens).</summary>
    private const int CharBudget = 80_000;

    public static string BuildHealingPrompt(
        Pipeline pipeline,
        Iteration currentIteration,
        List<Iteration> previousIterations,
        List<ClaudeTranscriptEntry> transcript)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a self-healing meta-engineer. Your job is to make all pipeline markers pass.");
        sb.AppendLine("You have full access to read, write, edit, and run commands in the target project.");
        sb.AppendLine();
        sb.AppendLine("If a step failed because required files/projects don't exist yet (e.g. a TestRunner,");
        sb.AppendLine("CLI harness, comparison script), CREATE THEM. Read the project context below for details");
        sb.AppendLine("on what to create. If code has bugs, fix them. If tests fail, fix the code being tested.");
        sb.AppendLine();
        sb.AppendLine($"Pipeline: {pipeline.Name} — {pipeline.Description}");
        sb.AppendLine($"Target Project: {pipeline.TargetProjectPath}");
        var iterText = pipeline.MaxIterations <= 0 ? $"{currentIteration.Number} (unlimited)" : $"{currentIteration.Number} / {pipeline.MaxIterations}";
        sb.AppendLine($"Iteration: {iterText}");
        sb.AppendLine();

        // Current iteration marker results
        sb.AppendLine("## MARKER RESULTS (THIS ITERATION)");
        foreach (var mr in currentIteration.MarkerResults)
        {
            var status = mr.Passed ? "PASS" : "FAIL";
            sb.AppendLine($"  - [{status}] {mr.MarkerName}: expected {mr.Operator} {mr.ExpectedValue}, got {mr.ActualValue}");
        }
        sb.AppendLine();

        // Budget-aware step output size: shrink if many iterations
        var outputCap = previousIterations.Count > 3 ? 2000 : 4000;
        var errorCap = previousIterations.Count > 3 ? 1000 : 2000;

        // Current iteration step outputs
        sb.AppendLine("## STEP OUTPUTS (THIS ITERATION)");
        foreach (var sr in currentIteration.StepResults)
        {
            sb.AppendLine($"### Step: {sr.StepName} (exit code: {sr.ExitCode})");
            if (!string.IsNullOrWhiteSpace(sr.Output))
            {
                var output = sr.Output.Length > outputCap ? sr.Output[^outputCap..] : sr.Output;
                sb.AppendLine("```");
                sb.AppendLine(output.TrimEnd());
                sb.AppendLine("```");
            }
            if (!string.IsNullOrWhiteSpace(sr.Error))
            {
                var error = sr.Error.Length > errorCap ? sr.Error[^errorCap..] : sr.Error;
                sb.AppendLine("Stderr:");
                sb.AppendLine("```");
                sb.AppendLine(error.TrimEnd());
                sb.AppendLine("```");
            }
        }
        sb.AppendLine();

        // Previous iterations — summarize older ones if approaching budget
        if (previousIterations.Count > 0)
        {
            sb.AppendLine("## PREVIOUS ITERATIONS");

            // Keep full detail for the last 2 iterations; summarize older ones
            var summarizeUpTo = Math.Max(0, previousIterations.Count - 2);

            for (int idx = 0; idx < previousIterations.Count; idx++)
            {
                var prev = previousIterations[idx];
                var passedCount = prev.MarkerResults.Count(m => m.Passed);
                var totalCount = prev.MarkerResults.Count;

                if (idx < summarizeUpTo)
                {
                    // Summarized: just marker deltas + changes list
                    sb.AppendLine($"### Iteration {prev.Number} (summary)");
                    sb.AppendLine($"Markers: {passedCount}/{totalCount} passed");
                    if (prev.ChangesMade.Count > 0)
                        sb.AppendLine($"Changes: {string.Join(", ", prev.ChangesMade)}");
                    sb.AppendLine();
                }
                else
                {
                    // Full detail
                    sb.AppendLine($"### Iteration {prev.Number}");
                    sb.AppendLine($"Markers: {passedCount}/{totalCount} passed");
                    foreach (var mr in prev.MarkerResults)
                    {
                        var status = mr.Passed ? "PASS" : "FAIL";
                        sb.AppendLine($"  - [{status}] {mr.MarkerName}: expected {mr.Operator} {mr.ExpectedValue}, got {mr.ActualValue}");
                    }
                    if (!string.IsNullOrEmpty(prev.ClaudeAnalysis))
                    {
                        var analysis = prev.ClaudeAnalysis.Length > 500
                            ? prev.ClaudeAnalysis[..500] + "..."
                            : prev.ClaudeAnalysis;
                        sb.AppendLine($"Your analysis: {analysis}");
                    }
                    if (prev.ChangesMade.Count > 0)
                    {
                        sb.AppendLine("Changes you made:");
                        foreach (var change in prev.ChangesMade)
                            sb.AppendLine($"  - {change}");
                    }
                    sb.AppendLine();
                }
            }
        }

        // Previous transcript entries — aggressively trimmed
        if (transcript.Count > 0)
        {
            sb.AppendLine("## PREVIOUS CLAUDE RESPONSES");
            // Only include the last 2 transcript entries
            var startIdx = Math.Max(0, transcript.Count - 2);
            if (startIdx > 0)
                sb.AppendLine($"({startIdx} earlier interaction{(startIdx != 1 ? "s" : "")} omitted for brevity)");

            var responseLimit = sb.Length > CharBudget / 2 ? 400 : 800;
            for (int i = startIdx; i < transcript.Count; i++)
            {
                var entry = transcript[i];
                var responsePreview = entry.Response.Length > responseLimit
                    ? entry.Response[..responseLimit] + "..."
                    : entry.Response;
                sb.AppendLine($"--- Iteration {i + 1} response ---");
                sb.AppendLine(responsePreview);
                sb.AppendLine();
            }
        }

        // File access rules
        sb.AppendLine("## FILE ACCESS RULES");
        var editable = pipeline.FileAccessRules.Where(r => r.AccessLevel == AccessLevel.Editable).Select(r => r.PathPattern);
        var readOnly = pipeline.FileAccessRules.Where(r => r.AccessLevel == AccessLevel.ReadOnly).Select(r => r.PathPattern);
        var excluded = pipeline.FileAccessRules.Where(r => r.AccessLevel == AccessLevel.Excluded).Select(r => r.PathPattern);

        sb.AppendLine($"Editable: {(editable.Any() ? string.Join(", ", editable) : "**/* (all files)")}");
        sb.AppendLine($"Read-only: {(readOnly.Any() ? string.Join(", ", readOnly) : "(none)")}");
        sb.AppendLine($"Excluded (do NOT touch): {(excluded.Any() ? string.Join(", ", excluded) : "(none)")}");
        sb.AppendLine();

        // Custom project-specific healing context
        if (!string.IsNullOrWhiteSpace(pipeline.HealingPromptTemplate))
        {
            sb.AppendLine("## PROJECT-SPECIFIC CONTEXT");
            sb.AppendLine(pipeline.HealingPromptTemplate);
            sb.AppendLine();
        }

        // Instructions
        sb.AppendLine("## INSTRUCTIONS");
        sb.AppendLine("1. Review the full history above — do NOT repeat changes that already failed");
        sb.AppendLine("2. If a previous change caused regression, consider reverting it");
        sb.AppendLine("3. Build on changes that showed improvement");
        sb.AppendLine("4. Analyze the FAILING markers and step outputs for THIS iteration carefully");
        sb.AppendLine("5. Read the actual error messages — they tell you exactly what's wrong");
        sb.AppendLine("6. Read relevant source files in the target project before editing");
        sb.AppendLine("7. Make targeted, minimal edits to fix the specific failures");
        sb.AppendLine("8. After editing, verify your fix makes sense (re-read the file if needed)");
        sb.AppendLine("9. Explain what you changed and why in 2-3 sentences");
        sb.AppendLine("10. Predict whether markers will pass after your changes");
        sb.AppendLine();
        sb.AppendLine("## IMPORTANT:");
        sb.AppendLine("- Fix the ROOT CAUSE, not symptoms. If a build fails, read the error and fix the actual code.");
        sb.AppendLine("- Do NOT add try/catch or error suppression to hide failures.");
        sb.AppendLine("- Do NOT delete tests to make them pass. Fix the code the tests are testing.");
        sb.AppendLine("- If you see the same error as a previous iteration, your last fix didn't work — try a different approach.");
        sb.AppendLine("- Keep changes minimal. Only touch files that are directly related to the failure.");

        // Enforce overall char budget — truncate from the middle if over
        var prompt = sb.ToString();
        if (prompt.Length > CharBudget)
        {
            var header = prompt[..(CharBudget / 3)];
            var footer = prompt[^(CharBudget * 2 / 3)..];
            prompt = header + "\n\n... [middle content truncated to fit token budget] ...\n\n" + footer;
        }

        return prompt;
    }
}
