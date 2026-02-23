using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Persistence;

public static class PipelineStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string PipelinesDir
    {
        get
        {
            var dir = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "pipelines");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<Pipeline> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<Pipeline>(json, Options)
               ?? throw new InvalidDataException($"Failed to parse pipeline: {filePath}");
    }

    public static async Task SaveAsync(Pipeline pipeline, string filePath)
    {
        var json = JsonSerializer.Serialize(pipeline, Options);
        var tmpPath = filePath + ".tmp";

        // Atomic write: write to temp file first, then rename
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, filePath, overwrite: true);
    }

    public static string GetDefaultPath(string pipelineName)
    {
        var safeName = string.Join("_", pipelineName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(PipelinesDir, $"{safeName}.json");
    }
}
