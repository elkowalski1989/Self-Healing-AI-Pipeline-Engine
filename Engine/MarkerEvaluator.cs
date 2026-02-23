using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SelfHealingPipeline.Helpers;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Engine;

public static class MarkerEvaluator
{
    public static List<MarkerResult> Evaluate(
        List<Marker> markers,
        Dictionary<string, string> stepData,
        string targetProjectPath)
    {
        var results = new List<MarkerResult>();

        foreach (var marker in markers)
        {
            var result = new MarkerResult
            {
                MarkerId = marker.Id,
                MarkerName = marker.Name,
                ExpectedValue = marker.TargetValue,
                Operator = marker.Operator
            };

            string? actualValue = null;

            // Validate marker has non-empty TargetValue
            if (string.IsNullOrEmpty(marker.TargetValue))
            {
                result.ActualValue = "(invalid marker: empty TargetValue)";
                result.Passed = false;
                results.Add(result);
                continue;
            }

            var source = marker.Source ?? "";

            switch (marker.Type)
            {
                case MarkerType.ExitCode:
                    // Source is the step output key; we look for exit code stored as "exitcode:<key>"
                    if (stepData.TryGetValue($"exitcode:{source}", out var exitStr))
                        actualValue = exitStr;
                    else if (stepData.TryGetValue(source, out var raw))
                    {
                        // If the output is just a number, treat it as exit code
                        if (int.TryParse(raw.Trim(), out _))
                            actualValue = raw.Trim();
                        else
                            actualValue = "0"; // step ran successfully
                    }
                    else
                        actualValue = null;
                    break;

                case MarkerType.JsonPath:
                    // Source format: "stepOutputKey:json.path"
                    var colonIdx = source.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var dataKey = source[..colonIdx];
                        var jsonPath = source[(colonIdx + 1)..];
                        if (stepData.TryGetValue(dataKey, out var jsonData))
                            actualValue = JsonPathHelper.Evaluate(jsonData, jsonPath);
                    }
                    break;

                case MarkerType.Regex:
                    // Source format: "stepOutputKey:regex_pattern"
                    var regexColonIdx = source.IndexOf(':');
                    if (regexColonIdx > 0)
                    {
                        var dataKey = source[..regexColonIdx];
                        var pattern = source[(regexColonIdx + 1)..];
                        if (stepData.TryGetValue(dataKey, out var textData))
                        {
                            try
                            {
                                var match = Regex.Match(textData, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                                actualValue = match.Success
                                    ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value)
                                    : null;
                            }
                            catch (RegexMatchTimeoutException)
                            {
                                actualValue = "(regex timed out)";
                            }
                            catch (ArgumentException)
                            {
                                actualValue = "(invalid regex pattern)";
                            }
                        }
                    }
                    break;

                case MarkerType.FileExists:
                    var filePath = Path.Combine(targetProjectPath, source);
                    actualValue = File.Exists(filePath) ? "true" : "false";
                    break;
            }

            if (actualValue == null)
            {
                // Provide a clear message about why evaluation failed
                var sourceKey = source.Split(':')[0];
                if (string.IsNullOrEmpty(sourceKey)) sourceKey = "(none)";
                result.ActualValue = $"(source '{sourceKey}' not found in step data)";
            }
            else
            {
                result.ActualValue = actualValue;
            }
            result.Passed = Compare(actualValue, marker.TargetValue, marker.Operator);
            results.Add(result);
        }

        return results;
    }

    private static bool Compare(string? actual, string expected, CompareOperator op)
    {
        if (actual == null) return false;

        // Try numeric comparison first
        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var actualNum) &&
            double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedNum))
        {
            return op switch
            {
                CompareOperator.Equals => Math.Abs(actualNum - expectedNum) < 0.0001,
                CompareOperator.NotEquals => Math.Abs(actualNum - expectedNum) >= 0.0001,
                CompareOperator.GreaterThan => actualNum > expectedNum,
                CompareOperator.GreaterThanOrEqual => actualNum >= expectedNum,
                CompareOperator.LessThan => actualNum < expectedNum,
                CompareOperator.LessThanOrEqual => actualNum <= expectedNum,
                CompareOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        // Fall back to string comparison
        return op switch
        {
            CompareOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            CompareOperator.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            CompareOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            CompareOperator.GreaterThan => string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase) > 0,
            CompareOperator.GreaterThanOrEqual => string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase) >= 0,
            CompareOperator.LessThan => string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase) < 0,
            CompareOperator.LessThanOrEqual => string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase) <= 0,
            _ => false
        };
    }
}
