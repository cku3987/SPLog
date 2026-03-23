using System.Text;

namespace SPLog;

internal static class ExceptionDetails
{
    public static string Build(Exception exception)
    {
        var builder = new StringBuilder(512);
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            if (depth > 0)
            {
                builder.AppendLine();
                builder.Append("INNER[").Append(depth).Append("]: ");
            }

            builder.Append(current.GetType().FullName);

            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                builder.Append(" | ");
                builder.Append(current.Message);
            }

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                builder.AppendLine();
                builder.Append(current.StackTrace);
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }
}
