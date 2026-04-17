using System.Runtime.CompilerServices;
using System.Text;

namespace SPLog;

#if NET8_0_OR_GREATER
[InterpolatedStringHandler]
public ref struct SPLogInterpolatedStringHandler
{
    private readonly bool _enabled;
    private StringBuilder? _builder;

    public SPLogInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        SPLogger logger,
        LogLevel level,
        out bool shouldAppend)
    {
        _enabled = logger.IsEnabled(level);
        shouldAppend = _enabled;
        _builder = _enabled ? new StringBuilder(literalLength + (formattedCount * 16)) : null;
    }

    public void AppendLiteral(string value)
    {
        _builder?.Append(value);
    }

    public void AppendFormatted<T>(T value)
    {
        _builder?.Append(value);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        if (_builder is null)
        {
            return;
        }

        if (value is IFormattable formattable)
        {
            _builder.Append(formattable.ToString(format, null));
            return;
        }

        _builder.Append(value);
    }

    public void AppendFormatted(string? value)
    {
        _builder?.Append(value);
    }

    public void AppendFormatted(ReadOnlySpan<char> value)
    {
        _builder?.Append(value);
    }

    internal string GetFormattedText()
    {
        if (!_enabled || _builder is null)
        {
            return string.Empty;
        }

        return _builder.ToString();
    }
}
#endif
