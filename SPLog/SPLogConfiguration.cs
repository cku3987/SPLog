using System.Text.Json;

namespace SPLog;

public static class SPLogConfiguration
{
    public static void Update(SPLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Clone();
        normalized.Validate();
        options.CopyFrom(normalized);
    }

    public static void UpdateFromJson(SPLogOptions options, string json)
    {
        ArgumentNullException.ThrowIfNull(options);
        var loaded = LoadFromJson(json);
        options.CopyFrom(loaded);
    }

    public static void UpdateFromJsonFile(SPLogOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        var loaded = LoadFromJsonFile(path);
        options.CopyFrom(loaded);
    }

    public static string SaveToJson(SPLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Update(options);
        return JsonSerializer.Serialize(options, SPLogJson.SerializerOptions);
    }

    public static void SaveToJsonFile(SPLogOptions options, string path)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = SaveToJson(options);
        File.WriteAllText(fullPath, json);
    }

    public static SPLogOptions LoadFromJson(string json)
    {
        var options = JsonSerializer.Deserialize<SPLogOptions>(json, SPLogJson.SerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize SPLogOptions from JSON.");

        options.Validate();
        return options;
    }

    public static SPLogOptions LoadFromJsonFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        return LoadFromJson(json);
    }
}
