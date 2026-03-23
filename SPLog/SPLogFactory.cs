namespace SPLog;

public static class SPLogFactory
{
    public static SPLogger Create(SPLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var snapshot = options.Clone();
        var processor = new AsyncLogProcessor(snapshot, CreateSink(snapshot));
        return new SPLogger(snapshot, processor);
    }

    public static SPLogger Create(Action<SPLogOptions>? configure = null)
    {
        var options = new SPLogOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public static SPLogger CreateFromJson(string json)
    {
        return Create(SPLogConfiguration.LoadFromJson(json));
    }

    public static SPLogger CreateFromJsonFile(string path)
    {
        return Create(SPLogConfiguration.LoadFromJsonFile(path));
    }

    private static ILogSink CreateSink(SPLogOptions options)
    {
        var sinks = new List<ILogSink>(2);

        if (options.EnableConsole)
        {
            sinks.Add(new ConsoleLogSink(options));
        }

        if (options.EnableFile)
        {
            sinks.Add(new FileLogSink(options));
        }

        if (sinks.Count == 0)
        {
            throw new InvalidOperationException("At least one sink must be enabled.");
        }

        return sinks.Count == 1 ? sinks[0] : new CompositeLogSink(sinks.ToArray());
    }
}
