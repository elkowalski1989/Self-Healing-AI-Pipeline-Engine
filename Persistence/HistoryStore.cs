using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SelfHealingPipeline.Models;

namespace SelfHealingPipeline.Persistence;

public static class HistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string HistoryDir
    {
        get
        {
            var dir = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "history");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task SaveSessionAsync(RunSession session)
    {
        var fileName = $"{session.StartTime:yyyyMMdd_HHmmss}_{session.Id}.json";
        var path = Path.Combine(HistoryDir, fileName);
        var tmpPath = path + ".tmp";
        var json = JsonSerializer.Serialize(session, Options);

        // Atomic write: write to temp file first, then rename
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    public static async Task<List<RunSession>> LoadAllAsync()
    {
        var sessions = new List<RunSession>();
        if (!Directory.Exists(HistoryDir)) return sessions;

        var files = Directory.GetFiles(HistoryDir, "*.json")
            .OrderByDescending(f => f);

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var session = JsonSerializer.Deserialize<RunSession>(json, Options);
                if (session != null)
                    sessions.Add(session);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Failed to load history file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        // Clean up any leftover temp files from interrupted writes
        foreach (var tmp in Directory.GetFiles(HistoryDir, "*.tmp"))
        {
            try { File.Delete(tmp); } catch { }
        }

        return sessions;
    }
}
