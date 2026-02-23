using System;
using System.Collections.Generic;

namespace SelfHealingPipeline.Models;

public enum RunStatus
{
    Running,
    Succeeded,
    Failed,
    Aborted
}

public class ClaudeTranscriptEntry
{
    public string Prompt { get; set; } = "";
    public string Response { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class RunSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string PipelineId { get; set; } = "";
    public string PipelineName { get; set; } = "";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public List<Iteration> Iterations { get; set; } = new();
    public List<ClaudeTranscriptEntry> ClaudeTranscript { get; set; } = new();
}
