using System;
using System.Collections.Generic;

namespace SelfHealingPipeline.Models;

public class StepResult
{
    public string StepId { get; set; } = "";
    public string StepName { get; set; } = "";
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Failed { get; set; }
    public TimeSpan Duration { get; set; }
}

public class Iteration
{
    public int Number { get; set; }
    public List<StepResult> StepResults { get; set; } = new();
    public List<MarkerResult> MarkerResults { get; set; } = new();
    public string ClaudeAnalysis { get; set; } = "";
    public List<string> ChangesMade { get; set; } = new();
    public TimeSpan Duration { get; set; }
}
