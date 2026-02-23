namespace SelfHealingPipeline.Models;

public enum AccessLevel
{
    Editable,
    ReadOnly,
    Excluded
}

public class FileAccessRule
{
    public string PathPattern { get; set; } = "";
    public AccessLevel AccessLevel { get; set; } = AccessLevel.Editable;
}
