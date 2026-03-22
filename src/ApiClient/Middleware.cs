using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient
{
    /// <summary>
    /// Delegate representing the next handler in the middleware pipeline.
    /// </summary>
    public delegate Task<ApiResponse> MiddlewareNext(CancellationToken ct);

    /// <summary>
    /// Middleware function signature: receives the request context, the next handler, and returns a response.
    /// </summary>
    /// <param name="context">Request context with method, URL, headers, etc.</param>
    /// <param name="next">The next middleware or the actual HTTP call.</param>
    /// <param name="ct">Cancellation token.</param>
    public delegate Task<ApiResponse> MiddlewareFunc(
        RequestContext context,
        MiddlewareNext next,
        CancellationToken ct);

    /// <summary>
    /// Immutable context passed through the middleware pipeline.
    /// </summary>
    public class RequestContext
    {
        /// <summary>HTTP method.</summary>
        public string Method { get; set; }

        /// <summary>Full request URL.</summary>
        public string Url { get; set; }

        /// <summary>Request start time.</summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Custom properties bag for passing data between middlewares.</summary>
        public System.Collections.Generic.Dictionary<string, object> Properties { get; }
            = new System.Collections.Generic.Dictionary<string, object>();
    }

    /// <summary>
    /// Built-in middleware factories.
    /// </summary>
    public static class Middleware
    {
        /// <summary>
        /// Logs request/response timing to a callback.
        /// </summary>
        public static MiddlewareFunc Logging(Action<string> logger)
        {
            return async (ctx, next, ct) =>
            {
                logger($"→ {ctx.Method} {ctx.Url}");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var resp = await next(ct).ConfigureAwait(false);
                sw.Stop();
                logger($"← {resp.StatusCode} ({sw.ElapsedMilliseconds}ms)");
                return resp;
            };
        }

        /// <summary>
        /// Adds a default header to every request.
        /// </summary>
        public static MiddlewareFunc DefaultHeader(string name, string value)
        {
            return async (ctx, next, ct) =>
            {
                ctx.Properties[$"header:{name}"] = value;
                return await next(ct).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Throws ApiException when status code >= threshold.
        /// </summary>
        public static MiddlewareFunc ErrorOnStatus(int threshold = 400)
        {
            return async (ctx, next, ct) =>
            {
                var resp = await next(ct).ConfigureAwait(false);
                if (resp.StatusCode >= threshold)
                    throw new ApiException($"HTTP {resp.StatusCode} from {ctx.Method} {ctx.Url}: {resp.Body}");
                return resp;
            };
        }

        /// <summary>
        /// Measures request duration and stores it in response headers as X-Duration-Ms.
        /// </summary>
        public static MiddlewareFunc Timing()
        {
            return async (ctx, next, ct) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var resp = await next(ct).ConfigureAwait(false);
                sw.Stop();
                resp.Headers["X-Duration-Ms"] = sw.ElapsedMilliseconds.ToString();
                return resp;
            };
        }
    }
}
