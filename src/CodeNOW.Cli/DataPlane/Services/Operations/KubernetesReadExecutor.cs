using System.Net;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Executes Kubernetes read operations with retry and warning logging.
/// </summary>
internal sealed class KubernetesReadExecutor
{
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(250);
    private readonly ILogger logger;

    /// <summary>
    /// Creates a read executor that wraps Kubernetes read calls with logging and retry.
    /// </summary>
    public KubernetesReadExecutor(ILogger logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Executes a read operation with retry and fallback.
    /// </summary>
    /// <param name="action">Async operation to execute.</param>
    /// <param name="warningMessage">Warning message to log on failure.</param>
    /// <param name="fallback">Fallback value to return on failure.</param>
    /// <param name="maxAttempts">Maximum number of attempts.</param>
    /// <param name="retryDelay">Delay between retry attempts.</param>
    /// <returns>Result of the read operation.</returns>
    public Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        string warningMessage,
        T fallback,
        int maxAttempts = 2,
        TimeSpan? retryDelay = null)
    {
        return ExecuteAsync(action, _ => fallback, warningMessage, maxAttempts, retryDelay);
    }

    /// <summary>
    /// Executes a read operation with retry and exception-specific fallback.
    /// </summary>
    /// <param name="action">Async operation to execute.</param>
    /// <param name="fallbackFactory">Fallback factory for failed attempts.</param>
    /// <param name="warningMessage">Warning message to log on failure.</param>
    /// <param name="maxAttempts">Maximum number of attempts.</param>
    /// <param name="retryDelay">Delay between retry attempts.</param>
    /// <returns>Result of the read operation.</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        Func<Exception, T> fallbackFactory,
        string warningMessage,
        int maxAttempts = 2,
        TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? DefaultRetryDelay;
        var attempt = 0;

        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt + 1 < maxAttempts)
            {
                attempt++;
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, warningMessage);
                return fallbackFactory(ex);
            }
        }
    }

    private static bool IsRetryable(Exception ex)
    {
        if (ex is k8s.Autorest.HttpOperationException httpEx &&
            httpEx.Response.StatusCode == HttpStatusCode.NotFound)
            return false;

        return true;
    }
}
