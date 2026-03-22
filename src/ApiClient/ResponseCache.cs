using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ApiClient
{
    /// <summary>
    /// Built-in response caching for GET requests.
    /// Caches responses by URL with configurable TTL.
    ///
    /// RestSharp does NOT offer built-in response caching.
    /// </summary>
    public class ResponseCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache
            = new ConcurrentDictionary<string, CacheEntry>();
        private readonly TimeSpan _defaultTtl;
        private readonly int _maxEntries;

        /// <summary>
        /// Creates a response cache.
        /// </summary>
        /// <param name="defaultTtl">Default time-to-live for cached responses.</param>
        /// <param name="maxEntries">Maximum number of cached responses (default: 1000).</param>
        public ResponseCache(TimeSpan defaultTtl, int maxEntries = 1000)
        {
            _defaultTtl = defaultTtl;
            _maxEntries = maxEntries;
        }

        /// <summary>Number of entries in the cache.</summary>
        public int Count => _cache.Count;

        /// <summary>
        /// Tries to get a cached response.
        /// </summary>
        public bool TryGet(string key, out ApiResponse response)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            {
                response = entry.Response;
                return true;
            }

            response = null;
            if (entry != null) // expired → remove
                _cache.TryRemove(key, out _);
            return false;
        }

        /// <summary>
        /// Stores a response in the cache.
        /// </summary>
        public void Set(string key, ApiResponse response, TimeSpan? ttl = null)
        {
            if (_cache.Count >= _maxEntries)
                EvictExpired();

            _cache[key] = new CacheEntry
            {
                Response = response,
                ExpiresAt = DateTime.UtcNow + (ttl ?? _defaultTtl)
            };
        }

        /// <summary>
        /// Invalidates a specific cache entry.
        /// </summary>
        public void Invalidate(string key)
        {
            _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// Invalidates all entries matching a prefix (e.g., "/api/users").
        /// </summary>
        public void InvalidatePrefix(string prefix)
        {
            foreach (var key in _cache.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Clears the entire cache.
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Creates a caching middleware that caches GET responses.
        /// </summary>
        public MiddlewareFunc AsMiddleware()
        {
            return async (ctx, next, ct) =>
            {
                // Only cache GET requests
                if (!ctx.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                    return await next(ct).ConfigureAwait(false);

                var cacheKey = ctx.Url;
                if (TryGet(cacheKey, out var cached))
                {
                    cached.Headers["X-Cache"] = "HIT";
                    return cached;
                }

                var resp = await next(ct).ConfigureAwait(false);
                if (resp.IsSuccess)
                {
                    Set(cacheKey, resp);
                    resp.Headers["X-Cache"] = "MISS";
                }

                return resp;
            };
        }

        private void EvictExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.ExpiresAt <= now)
                    _cache.TryRemove(kvp.Key, out _);
            }
        }

        private class CacheEntry
        {
            public ApiResponse Response { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
