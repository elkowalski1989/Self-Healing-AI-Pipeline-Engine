using System.Collections.Generic;

namespace SelfHealingPipeline.Models;

public class Pipeline
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TargetProjectPath { get; set; } = "";
    public int MaxIterations { get; set; } = 5;
    public List<PipelineStep> Steps { get; set; } = new();
    public List<Marker> Markers { get; set; } = new();
    public List<FileAccessRule> FileAccessRules { get; set; } = new();
    public string HealingPromptTemplate { get; set; } = "";
}
