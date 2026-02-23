using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SelfHealingPipeline.Agents;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Engine;

public class PipelineEngine
{
    private readonly StepExecutor _stepExecutor = new();
    private readonly HealingLoop _healingLoop = new();
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseGate = new(true);
    private bool _isPaused;

    public event Action<string, string>? OnLog;
    public event Action<int, int>? OnIterationChanged;
    public event Action<List<MarkerResult>>? OnMarkersUpdated;
    public event Action<RunStatus>? OnStatusChanged;
    public event Action<ClaudeStreamEvent>? OnClaudeEvent;
    public event Action<bool>? OnRegressionWarning;
    public event Action<CostInfo>? OnCostUpdated;
    public event Action<string>? OnPhaseChanged;

    public RunSession? CurrentSession { get; private set; }
    public bool IsPaused => _isPaused;

    public void SetClaudePath(string path) => _healingLoop.SetClaudePath(path);

    public void Pause()
    {
        _isPaused = true;
        _pauseGate.Reset();
        OnLog?.Invoke("Engine", "Pipeline paused.");
    }

    public void Resume()
    {
        _isPaused = false;
        _pauseGate.Set();
        OnLog?.Invoke("Engine", "Pipeline resumed.");
    }

    public async Task<RunSession> RunAsync(Pipeline pipeline)
    {
        _cts = new CancellationTokenSource();
        _isPaused = false;
        _pauseGate.Set();
        var ct = _cts.Token;
        var costTracker = new CostInfo();

        var session = new RunSession
        {
            PipelineName = pipeline.Name,
            StartTime = DateTime.Now,
            Status = RunStatus.Running
        };
        CurrentSession = session;

        // Wire events
        _stepExecutor.OnLog += (src, msg) => OnLog?.Invoke(src, msg);
        _healingLoop.OnLog += (src, msg) => OnLog?.Invoke(src, msg);
        _healingLoop.OnClaudeEvent += evt =>
        {
            OnClaudeEvent?.Invoke(evt);

            // Track cost from Claude stream events
            if (evt.Type == "result" && evt.RawJson != null)
            {
                costTracker.TryParseFromResult(evt.RawJson);
                OnCostUpdated?.Invoke(costTracker);
            }
        };

        OnStatusChanged?.Invoke(RunStatus.Running);
        OnLog?.Invoke("Engine", $"Starting pipeline: {pipeline.Name}");
        OnLog?.Invoke("Engine", $"Target: {pipeline.TargetProjectPath}");
        // 0 means unlimited — use int.MaxValue as the effective cap
        var isUnlimited = pipeline.MaxIterations <= 0;
        var effectiveMax = isUnlimited ? int.MaxValue : pipeline.MaxIterations;
        OnLog?.Invoke("Engine", isUnlimited
            ? "Max iterations: unlimited (regression safety net active)"
            : $"Max iterations: {pipeline.MaxIterations}");

        try
        {
            for (int i = 1; i <= effectiveMax; i++)
            {
                // Check pause
                _pauseGate.Wait(ct);
                ct.ThrowIfCancellationRequested();

                var iterationSw = Stopwatch.StartNew();
                var iteration = new Iteration { Number = i };

                OnIterationChanged?.Invoke(i, isUnlimited ? 0 : pipeline.MaxIterations);
                OnLog?.Invoke("Engine", isUnlimited
                    ? $"═══ Iteration {i} ═══"
                    : $"═══ Iteration {i}/{pipeline.MaxIterations} ═══");

                // Execute all steps
                OnPhaseChanged?.Invoke("Running steps...");
                var stepData = new Dictionary<string, string>();
                bool aborted = false;

                foreach (var step in pipeline.Steps)
                {
                    _pauseGate.Wait(ct);
                    ct.ThrowIfCancellationRequested();

                    var result = await _stepExecutor.ExecuteAsync(step, stepData, pipeline.TargetProjectPath, ct);
                    iteration.StepResults.Add(result);

                    if (!string.IsNullOrEmpty(step.OutputKey))
                        stepData[$"exitcode:{step.OutputKey}"] = result.ExitCode.ToString();

                    if (result.Failed && step.FailBehavior == FailBehavior.Abort)
                    {
                        OnLog?.Invoke("Engine", $"Step '{step.Name}' failed with Abort behavior — stopping iteration");
                        aborted = true;
                        break;
                    }
                }

                // Evaluate markers
                OnPhaseChanged?.Invoke("Checking results...");
                var markerResults = MarkerEvaluator.Evaluate(pipeline.Markers, stepData, pipeline.TargetProjectPath);
                iteration.MarkerResults = markerResults;
                OnMarkersUpdated?.Invoke(markerResults);

                var passedCount = markerResults.Count(m => m.Passed);
                var totalCount = markerResults.Count;
                OnLog?.Invoke("Engine", $"Markers: {passedCount}/{totalCount} passed");

                foreach (var mr in markerResults)
                {
                    var icon = mr.Passed ? "PASS" : "FAIL";
                    OnLog?.Invoke("Engine", $"  [{icon}] {mr.MarkerName}: {mr.ActualValue} (target: {mr.Operator} {mr.ExpectedValue})");
                }

                session.Iterations.Add(iteration);

                // All markers pass → success
                if (passedCount == totalCount && !aborted)
                {
                    session.Status = RunStatus.Succeeded;
                    OnLog?.Invoke("Engine", "All markers passed! Pipeline succeeded.");
                    OnStatusChanged?.Invoke(RunStatus.Succeeded);
                    break;
                }

                // Last iteration → fail (skipped if unlimited)
                if (!isUnlimited && i == pipeline.MaxIterations)
                {
                    session.Status = RunStatus.Failed;
                    OnLog?.Invoke("Engine", "Max iterations reached. Pipeline failed.");
                    OnStatusChanged?.Invoke(RunStatus.Failed);
                    break;
                }

                // Check for regression
                if (HealingLoop.IsRegressing(session.Iterations))
                {
                    OnRegressionWarning?.Invoke(true);
                    session.Status = RunStatus.Aborted;
                    OnLog?.Invoke("Engine", "WARNING: Regression detected (3 consecutive worsening iterations). Aborting.");
                    OnStatusChanged?.Invoke(RunStatus.Aborted);
                    break;
                }

                // Heal
                OnPhaseChanged?.Invoke("Claude is fixing code...");
                _pauseGate.Wait(ct);
                ct.ThrowIfCancellationRequested();

                var previousIterations = session.Iterations.Take(session.Iterations.Count - 1).ToList();
                var (analysis, changes) = await _healingLoop.HealAsync(
                    pipeline, iteration, previousIterations, session.ClaudeTranscript, ct);

                iteration.ClaudeAnalysis = analysis;
                iteration.ChangesMade = changes;
                iterationSw.Stop();
                iteration.Duration = iterationSw.Elapsed;
                OnPhaseChanged?.Invoke("Retrying...");
            }
        }
        catch (OperationCanceledException)
        {
            session.Status = RunStatus.Aborted;
            OnLog?.Invoke("Engine", "Pipeline cancelled by user.");
            OnStatusChanged?.Invoke(RunStatus.Aborted);
        }
        catch (Exception ex)
        {
            session.Status = RunStatus.Failed;
            OnLog?.Invoke("Engine", $"Pipeline error: {ex.Message}");
            OnStatusChanged?.Invoke(RunStatus.Failed);
        }

        session.EndTime = DateTime.Now;
        CurrentSession = null;
        return session;
    }

    public void Cancel()
    {
        _isPaused = false;
        _pauseGate.Set();
        _cts?.Cancel();
    }
}

public class CostInfo
{
    public double TotalCost { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ClaudeInvocations { get; set; }

    public void TryParseFromResult(string resultJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            ClaudeInvocations++;

            if (root.TryGetProperty("total_cost_usd", out var cost))
                TotalCost += cost.GetDouble();
            else if (root.TryGetProperty("cost_usd", out var cost2))
                TotalCost += cost2.GetDouble();

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var inp))
                    InputTokens += inp.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var outp))
                    OutputTokens += outp.GetInt32();
            }
        }
        catch { }
    }

    public override string ToString()
    {
        if (TotalCost > 0)
            return $"${TotalCost:F4}  ({InputTokens + OutputTokens:N0} tokens, {ClaudeInvocations} calls)";
        if (ClaudeInvocations > 0)
            return $"{ClaudeInvocations} Claude call{(ClaudeInvocations != 1 ? "s" : "")}";
        return "";
    }
}
