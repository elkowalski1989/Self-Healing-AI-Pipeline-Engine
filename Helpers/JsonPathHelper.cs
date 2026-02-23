using System;
using System.Text.Json;

namespace SelfHealingPipeline.Helpers;

public static class JsonPathHelper
{
    /// <summary>
    /// Evaluates a simple dot-notation JSON path (e.g. "results.high_confidence_pct")
    /// against a JSON string. Returns the value as a string, or null if not found.
    /// </summary>
    public static string? Evaluate(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement;

            var segments = path.Split('.');
            foreach (var segment in segments)
            {
                // Handle array indexing: "items[0]"
                if (segment.Contains('[') && segment.EndsWith(']'))
                {
                    var bracketIdx = segment.IndexOf('[');
                    var prop = segment[..bracketIdx];
                    var idxStr = segment[(bracketIdx + 1)..^1];

                    if (!string.IsNullOrEmpty(prop))
                    {
                        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(prop, out element))
                            return null;
                    }

                    if (int.TryParse(idxStr, out int idx) && element.ValueKind == JsonValueKind.Array)
                    {
                        if (idx < 0 || idx >= element.GetArrayLength())
                            return null;
                        element = element[idx];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                        return null;
                }
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }
        catch
        {
            return null;
        }
    }
}
