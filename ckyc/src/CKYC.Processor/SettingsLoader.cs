using System.Text.Json;
using System.Text.Json.Serialization;
using CKYC.Core.Configuration;

namespace CKYC.Processor;

/// <summary>Loads application settings from JSON, falling back to built-in defaults.</summary>
public static class SettingsLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load(string? explicitPath = null)
    {
        var settings = new AppSettings();
        var path = explicitPath ?? ResolveDefaultPath();
        if (path is not null && File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (loaded is not null) settings = loaded;
        }
        return settings;
    }

    private static string? ResolveDefaultPath()
    {
        var cwd = Directory.GetCurrentDirectory();
        var candidate = Path.Combine(cwd, "appsettings.json");
        if (File.Exists(candidate)) return candidate;

        var exeDir = System.AppContext.BaseDirectory;
        candidate = Path.Combine(exeDir, "appsettings.json");
        return File.Exists(candidate) ? candidate : null;
    }
}
