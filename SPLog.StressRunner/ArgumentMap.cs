namespace SPLog.StressRunner;

internal sealed class ArgumentMap
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowHelp => _values.ContainsKey("help");

    public string? ConfigPath => Get("config");

    public string? WriteSamplePath => Get("write-sample");

    public string? Duration => Get("duration");

    public string? StatusInterval => Get("status");

    public int? ProducerCount => ParseInt("producers");

    public int? MessagePayloadLength => ParseInt("payload");

    public int? MessagesPerBurst => ParseInt("burst");

    public int? PausePerBurstMs => ParseInt("pause-ms");

    public static ArgumentMap Parse(string[] args)
    {
        var map = new ArgumentMap();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (!raw.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = raw[2..];
            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            map._values[key] = value;
        }

        return map;
    }

    private string? Get(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    private int? ParseInt(string key)
    {
        var value = Get(key);
        return string.IsNullOrWhiteSpace(value) ? null : int.Parse(value);
    }
}
