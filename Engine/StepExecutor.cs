using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SelfHealingPipeline.Helpers;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Engine;

public class StepExecutor
{
    public event Action<string, string>? OnLog; // (source, message)

    public async Task<StepResult> ExecuteAsync(
        PipelineStep step,
        Dictionary<string, string> stepData,
        string targetProjectPath,
        CancellationToken ct = default)
    {
        var result = new StepResult
        {
            StepId = step.Id,
            StepName = step.Name
        };

        var workingDir = !string.IsNullOrEmpty(step.WorkingDir)
            ? step.WorkingDir
            : targetProjectPath;

        // Replace {{variables}} in command with step data
        var command = ResolveVariables(step.Command, stepData);

        // Auto-resolve MSB1011: when a dotnet command doesn't specify a project/solution
        // file and the working directory contains both .sln and .csproj files, MSBuild
        // cannot determine which to use. Automatically append the .sln file.
        command = ResolveDotnetAmbiguity(command, workingDir);

        try
        {
            switch (step.Type)
            {
                case StepType.Execute:
                    OnLog?.Invoke("Step", $"Executing: {step.Name}");
                    var processResult = await RunCommandAsync(command, workingDir, step.Timeout * 1000, ct);
                    result.ExitCode = processResult.ExitCode;
                    result.Output = processResult.Output;
                    result.Error = processResult.Error;
                    result.Duration = processResult.Duration;
                    result.Failed = processResult.ExitCode != 0;

                    if (result.Failed)
                        OnLog?.Invoke("Step", $"  FAILED (exit code {result.ExitCode})");
                    else
                        OnLog?.Invoke("Step", $"  OK ({result.Duration.TotalSeconds:F1}s)");
                    break;

                case StepType.Extract:
                    OnLog?.Invoke("Step", $"Extracting: {step.Name}");

                    if (!string.IsNullOrEmpty(step.FilePath))
                    {
                        var filePath = ResolveVariables(step.FilePath, stepData);
                        if (File.Exists(filePath))
                        {
                            result.Output = await File.ReadAllTextAsync(filePath, ct);
                            OnLog?.Invoke("Step", $"  Read {result.Output.Length} chars from {filePath}");
                        }
                        else
                        {
                            result.Failed = true;
                            result.Error = $"File not found: {filePath}";
                            OnLog?.Invoke("Step", $"  FAILED: {result.Error}");
                        }
                    }
                    else if (!string.IsNullOrEmpty(command))
                    {
                        var processResult2 = await RunCommandAsync(command, workingDir, step.Timeout * 1000, ct);
                        result.ExitCode = processResult2.ExitCode;
                        result.Output = processResult2.Output;
                        result.Error = processResult2.Error;
                        result.Duration = processResult2.Duration;
                        result.Failed = processResult2.ExitCode != 0;
                    }

                    // Apply extraction pattern if specified
                    if (!result.Failed && !string.IsNullOrEmpty(step.ExtractionPattern))
                    {
                        var match = Regex.Match(result.Output, step.ExtractionPattern);
                        if (match.Success)
                        {
                            result.Output = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                            OnLog?.Invoke("Step", $"  Extracted: {result.Output[..Math.Min(100, result.Output.Length)]}");
                        }
                    }
                    break;

                case StepType.Validate:
                    OnLog?.Invoke("Step", $"Validating: {step.Name}");
                    var validateResult = await RunCommandAsync(command, workingDir, step.Timeout * 1000, ct);
                    result.ExitCode = validateResult.ExitCode;
                    result.Output = validateResult.Output;
                    result.Error = validateResult.Error;
                    result.Duration = validateResult.Duration;
                    result.Failed = validateResult.ExitCode != 0;

                    OnLog?.Invoke("Step", result.Failed
                        ? $"  Validation FAILED (exit code {result.ExitCode})"
                        : "  Validation PASSED");
                    break;
            }

            // Store output in step data
            if (!string.IsNullOrEmpty(step.OutputKey) && !result.Failed)
                stepData[step.OutputKey] = result.Output;
        }
        catch (OperationCanceledException)
        {
            result.Failed = true;
            result.Error = "Step was cancelled or timed out";
            OnLog?.Invoke("Step", $"  TIMEOUT: {step.Name}");
        }
        catch (Exception ex)
        {
            result.Failed = true;
            result.Error = ex.Message;
            OnLog?.Invoke("Step", $"  ERROR: {ex.Message}");
        }

        // Cap output/error to 50KB each (keep tail — errors are usually at the end)
        const int MaxOutputSize = 50 * 1024;
        if (result.Output.Length > MaxOutputSize)
            result.Output = "... [truncated] ...\n" + result.Output[^MaxOutputSize..];
        if (result.Error.Length > MaxOutputSize)
            result.Error = "... [truncated] ...\n" + result.Error[^MaxOutputSize..];

        return result;
    }

    private static async Task<ProcessResult> RunCommandAsync(
        string command, string workingDir, int timeoutMs, CancellationToken ct)
    {
        // Parse "cmd /c <command>" on Windows
        string fileName;
        string arguments;

        if (command.StartsWith('"') || !command.Contains(' '))
        {
            fileName = command.Trim('"');
            arguments = "";
        }
        else
        {
            fileName = "cmd";
            arguments = $"/c {command}";
        }

        return await ProcessRunner.RunAsync(fileName, arguments, workingDir,
            timeoutMs: timeoutMs, ct: ct);
    }

    /// <summary>
    /// Detects bare dotnet commands (restore, build, test, etc.) that don't specify a
    /// project/solution file, and appends the .sln file when the working directory
    /// contains both .sln and .csproj files (which causes MSBuild error MSB1011).
    /// </summary>
    private static string ResolveDotnetAmbiguity(string command, string workingDir)
    {
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
            return command;

        // Only process bare dotnet commands that don't already specify a project/sln file
        var dotnetCommands = new[] { "dotnet restore", "dotnet build", "dotnet test", "dotnet run", "dotnet publish" };
        string? matchedPrefix = null;

        foreach (var prefix in dotnetCommands)
        {
            // Match "dotnet restore", "dotnet restore --flags", but not "dotnet restore foo.sln"
            if (command.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                command.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                matchedPrefix = prefix;
                break;
            }
        }

        if (matchedPrefix == null)
            return command;

        // Check if the command already references a .sln or .csproj file
        var remainder = command[matchedPrefix.Length..];
        if (remainder.Contains(".sln", StringComparison.OrdinalIgnoreCase) ||
            remainder.Contains(".csproj", StringComparison.OrdinalIgnoreCase))
            return command;

        // Check if there's ambiguity: both .sln and .csproj in the working directory
        var slnFiles = Directory.GetFiles(workingDir, "*.sln");
        var csprojFiles = Directory.GetFiles(workingDir, "*.csproj");

        if (slnFiles.Length >= 1 && csprojFiles.Length >= 1 && (slnFiles.Length + csprojFiles.Length) > 1)
        {
            // Prefer the .sln file; insert it right after the dotnet command verb
            var slnName = Path.GetFileName(slnFiles[0]);
            var args = command[matchedPrefix.Length..];
            return $"{matchedPrefix} \"{slnName}\"{args}";
        }

        return command;
    }

    private static string ResolveVariables(string template, Dictionary<string, string> data)
    {
        foreach (var (key, value) in data)
            template = template.Replace($"{{{{{key}}}}}", value);
        return template;
    }
}
