using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ApiClient
{
    /// <summary>
    /// Represents an HTTP response from an API call.
    /// </summary>
    public class ApiResponse
    {
        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; set; }

        /// <summary>Response body as string.</summary>
        public string Body { get; set; }

        /// <summary>Response headers.</summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        /// <summary>Whether the response status code indicates success (2xx).</summary>
        public bool IsSuccess { get; set; }

        /// <summary>Content type of the response.</summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Deserializes the response body as JSON to the specified type.
        /// </summary>
        public T As<T>()
        {
            return JsonSerializer.Deserialize<T>(Body);
        }

        /// <summary>
        /// Returns the response body as a string.
        /// </summary>
        public override string ToString() => Body;

        /// <summary>
        /// Throws ApiException if the response is not successful.
        /// </summary>
        public ApiResponse EnsureSuccess()
        {
            if (!IsSuccess)
                throw new ApiException($"HTTP {StatusCode}: {Body}");
            return this;
        }

        /// <summary>
        /// Gets a specific response header value.
        /// </summary>
        public string GetHeader(string name)
        {
            Headers.TryGetValue(name, out var value);
            return value;
        }

        /// <summary>Whether the status code is 4xx.</summary>
        public bool IsClientError => StatusCode >= 400 && StatusCode < 500;

        /// <summary>Whether the status code is 5xx.</summary>
        public bool IsServerError => StatusCode >= 500;
    }

    /// <summary>
    /// Exception thrown by API operations.
    /// </summary>
    public class ApiException : Exception
    {
        public ApiException(string message) : base(message) { }
        public ApiException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Retry configuration for failed requests.
    /// </summary>
    public class RetryOptions
    {
        /// <summary>Maximum number of attempts (default: 3).</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Initial delay between retries (default: 200ms).</summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>Maximum delay between retries (default: 10s).</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Whether to add random jitter to delays (default: true).</summary>
        public bool UseJitter { get; set; } = true;

        /// <summary>
        /// Custom retry predicate. Return true to retry.
        /// Default: retries on 5xx and 429.
        /// </summary>
        public Func<ApiResponse, bool> ShouldRetry { get; set; }
    }
}
