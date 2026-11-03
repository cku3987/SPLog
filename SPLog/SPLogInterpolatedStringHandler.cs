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

    public void AppendFormatted<T>(T value, int alignment)
    {
        AppendFormatted(value, alignment, null);
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        if (_builder is null) return;
        var start = _builder.Length;
        AppendFormatted(value, format);
        Align(start, alignment);
    }

    public void AppendFormatted(string? value, int alignment = 0, string? format = null)
    {
        if (_builder is null) return;
        var start = _builder.Length;
        _builder.Append(value);
        Align(start, alignment);
    }

    public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
    {
        if (_builder is null) return;
        var start = _builder.Length;
        _builder.Append(value);
        Align(start, alignment);
    }

    private void Align(int start, int alignment)
    {
        var padding = Math.Abs(alignment) - (_builder!.Length - start);
        if (padding <= 0) return;
        if (alignment < 0) _builder.Append(' ', padding);
        else _builder.Insert(start, new string(' ', padding));
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
