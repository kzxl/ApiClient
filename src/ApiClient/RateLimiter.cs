using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient
{
    /// <summary>
    /// Token bucket rate limiter for controlling API request rate.
    /// Prevents hitting API rate limits by throttling outgoing requests.
    ///
    /// RestSharp does NOT have built-in rate limiting.
    /// </summary>
    public class RateLimiter
    {
        private readonly int _maxTokens;
        private readonly TimeSpan _refillInterval;
        private double _tokens;
        private DateTime _lastRefill;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Creates a rate limiter using the token bucket algorithm.
        /// </summary>
        /// <param name="maxRequestsPerSecond">Maximum requests per second.</param>
        public RateLimiter(int maxRequestsPerSecond)
        {
            _maxTokens = maxRequestsPerSecond;
            _tokens = maxRequestsPerSecond;
            _refillInterval = TimeSpan.FromSeconds(1.0 / maxRequestsPerSecond);
            _lastRefill = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a rate limiter with custom window.
        /// </summary>
        /// <param name="maxRequests">Maximum requests in the window.</param>
        /// <param name="window">Time window for the limit.</param>
        public RateLimiter(int maxRequests, TimeSpan window)
        {
            _maxTokens = maxRequests;
            _tokens = maxRequests;
            _refillInterval = TimeSpan.FromMilliseconds(window.TotalMilliseconds / maxRequests);
            _lastRefill = DateTime.UtcNow;
        }

        /// <summary>
        /// Waits until a request is allowed by the rate limiter.
        /// </summary>
        public async Task WaitAsync(CancellationToken ct = default)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                await _semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    RefillTokens();

                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return; // allowed
                    }
                }
                finally
                {
                    _semaphore.Release();
                }

                // Wait for next token
                await Task.Delay(_refillInterval, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Available tokens (for monitoring).</summary>
        public double AvailableTokens
        {
            get
            {
                RefillTokens();
                return _tokens;
            }
        }

        /// <summary>
        /// Creates a rate limiting middleware.
        /// </summary>
        public MiddlewareFunc AsMiddleware()
        {
            return async (ctx, next, ct) =>
            {
                await WaitAsync(ct).ConfigureAwait(false);
                return await next(ct).ConfigureAwait(false);
            };
        }

        private void RefillTokens()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRefill;
            var tokensToAdd = elapsed.TotalMilliseconds / _refillInterval.TotalMilliseconds;

            if (tokensToAdd > 0)
            {
                _tokens = Math.Min(_maxTokens, _tokens + tokensToAdd);
                _lastRefill = now;
            }
        }
    }
}
