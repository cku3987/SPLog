#if NETFRAMEWORK
namespace SPLog.Tests;

internal static class FrameworkCompatibility
{
    public static bool Contains(this string value, string text, StringComparison comparison)
        => value.IndexOf(text, comparison) >= 0;

    public static async Task WaitAsync(this Task task, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource();
        var delay = Task.Delay(timeout, cancellation.Token);
        if (await Task.WhenAny(task, delay).ConfigureAwait(false) != task)
        {
            throw new TimeoutException("The test operation did not complete in time.");
        }

        cancellation.Cancel();
        await task.ConfigureAwait(false);
    }

    public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
    {
        await ((Task)task).WaitAsync(timeout).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }
}
#endif
