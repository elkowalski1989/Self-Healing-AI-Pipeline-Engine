namespace SelfHealingPipeline.Models;

public enum StepType
{
    Execute,
    Extract,
    Validate
}

public enum FailBehavior
{
    Continue,
    Abort
}

public class PipelineStep
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public StepType Type { get; set; } = StepType.Execute;
    public string Command { get; set; } = "";
    public string WorkingDir { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ExtractionPattern { get; set; } = "";
    public string OutputKey { get; set; } = "";
    public int Timeout { get; set; } = 120;
    public FailBehavior FailBehavior { get; set; } = FailBehavior.Continue;
}
