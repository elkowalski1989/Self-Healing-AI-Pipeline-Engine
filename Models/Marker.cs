namespace SelfHealingPipeline.Models;

public enum MarkerType
{
    JsonPath,
    ExitCode,
    Regex,
    FileExists
}

public enum CompareOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains
}

public class Marker
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public MarkerType Type { get; set; } = MarkerType.ExitCode;
    public string Source { get; set; } = "";
    public CompareOperator Operator { get; set; } = CompareOperator.Equals;
    public string TargetValue { get; set; } = "";
}

public class MarkerResult
{
    public string MarkerId { get; set; } = "";
    public string MarkerName { get; set; } = "";
    public bool Passed { get; set; }
    public string ActualValue { get; set; } = "";
    public string ExpectedValue { get; set; } = "";
    public CompareOperator Operator { get; set; }
}
