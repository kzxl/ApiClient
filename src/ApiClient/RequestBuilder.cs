using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient
{
    /// <summary>
    /// Builds and sends a single HTTP request with fluent configuration.
    /// Supports middleware pipeline execution.
    /// </summary>
    public class RequestBuilder
    {
        private readonly HttpClient _http;
        private readonly HttpMethod _method;
        private readonly string _url;
        private readonly Dictionary<string, string> _headers;
        private readonly RetryOptions _retry;
        private readonly List<MiddlewareFunc> _middlewares;
        private readonly List<KeyValuePair<string, string>> _queryParams = new List<KeyValuePair<string, string>>();
        private HttpContent _body;

        internal RequestBuilder(
            HttpClient http,
            HttpMethod method,
            string url,
            Dictionary<string, string> defaultHeaders,
            RetryOptions retry,
            List<MiddlewareFunc> middlewares = null)
        {
            _http = http;
            _method = method;
            _url = url;
            _headers = new Dictionary<string, string>(defaultHeaders ?? new Dictionary<string, string>());
            _retry = retry;
            _middlewares = middlewares ?? new List<MiddlewareFunc>();
        }

        /// <summary>Adds a request header.</summary>
        public RequestBuilder Header(string name, string value)
        {
            _headers[name] = value;
            return this;
        }

        /// <summary>Adds a query string parameter.</summary>
        public RequestBuilder Query(string name, string value)
        {
            _queryParams.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }

        /// <summary>Sets the request body as JSON.</summary>
        public RequestBuilder JsonBody<T>(T body)
        {
            var json = JsonSerializer.Serialize(body);
            _body = new StringContent(json, Encoding.UTF8, "application/json");
            return this;
        }

        /// <summary>Sets the request body as form-urlencoded.</summary>
        public RequestBuilder FormBody(Dictionary<string, string> form)
        {
            _body = new FormUrlEncodedContent(form);
            return this;
        }

        /// <summary>Sets a raw string body with content type.</summary>
        public RequestBuilder RawBody(string content, string contentType = "text/plain")
        {
            _body = new StringContent(content, Encoding.UTF8, contentType);
            return this;
        }

        /// <summary>Sets Bearer token for this request only.</summary>
        public RequestBuilder BearerToken(string token)
        {
            _headers["Authorization"] = $"Bearer {token}";
            return this;
        }

        /// <summary>Sends the request through the middleware pipeline.</summary>
        public async Task<ApiResponse> SendAsync(CancellationToken ct = default)
        {
            var finalUrl = BuildUrl();
            var context = new RequestContext
            {
                Method = _method.Method,
                Url = finalUrl
            };

            // Build the pipeline: middlewares → actual HTTP call
            MiddlewareNext handler = (cancelToken) => ExecuteWithRetryAsync(cancelToken);

            // Wrap with middlewares in reverse order (so first added = first executed)
            for (int i = _middlewares.Count - 1; i >= 0; i--)
            {
                var mw = _middlewares[i];
                var next = handler;
                handler = (cancelToken) => mw(context, next, cancelToken);
            }

            return await handler(ct).ConfigureAwait(false);
        }

        // ─── Internal ─────────────────────────────────────────────

        private async Task<ApiResponse> ExecuteWithRetryAsync(CancellationToken ct)
        {
            if (_retry != null && _retry.MaxAttempts > 1)
                return await SendWithRetryAsync(ct).ConfigureAwait(false);
            return await ExecuteAsync(ct).ConfigureAwait(false);
        }

        private async Task<ApiResponse> ExecuteAsync(CancellationToken ct)
        {
            var request = BuildRequest();
            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var headers = new Dictionary<string, string>();
            foreach (var h in response.Headers)
                headers[h.Key] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);

            return new ApiResponse
            {
                StatusCode = (int)response.StatusCode,
                Body = body,
                Headers = headers,
                IsSuccess = response.IsSuccessStatusCode,
                ContentType = response.Content?.Headers?.ContentType?.MediaType
            };
        }

        private async Task<ApiResponse> SendWithRetryAsync(CancellationToken ct)
        {
            int attempt = 0;
            ApiResponse lastResponse = null;
            Exception lastError = null;

            while (attempt < _retry.MaxAttempts)
            {
                attempt++;
                try
                {
                    lastResponse = await ExecuteAsync(ct).ConfigureAwait(false);

                    if (_retry.ShouldRetry != null)
                    {
                        if (!_retry.ShouldRetry(lastResponse))
                            return lastResponse;
                    }
                    else
                    {
                        if (lastResponse.StatusCode < 500 && lastResponse.StatusCode != 429)
                            return lastResponse;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    lastError = ex;
                }

                if (attempt < _retry.MaxAttempts)
                {
                    var delay = CalculateDelay(attempt);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }

            if (lastResponse != null)
                return lastResponse;

            throw new ApiException($"Request failed after {_retry.MaxAttempts} attempts", lastError);
        }

        private TimeSpan CalculateDelay(int attempt)
        {
            var baseDelay = _retry.InitialDelay.TotalMilliseconds;
            var delay = baseDelay * Math.Pow(2, attempt - 1);
            if (delay > _retry.MaxDelay.TotalMilliseconds)
                delay = _retry.MaxDelay.TotalMilliseconds;
            if (_retry.UseJitter)
            {
                var jitter = new Random().NextDouble() * 0.3 * delay;
                delay += jitter;
            }
            return TimeSpan.FromMilliseconds(delay);
        }

        private string BuildUrl()
        {
            var url = _url;
            if (_queryParams.Count > 0)
            {
                var sb = new StringBuilder(url);
                sb.Append(url.Contains("?") ? "&" : "?");
                for (int i = 0; i < _queryParams.Count; i++)
                {
                    if (i > 0) sb.Append("&");
                    sb.Append(Uri.EscapeDataString(_queryParams[i].Key));
                    sb.Append("=");
                    sb.Append(Uri.EscapeDataString(_queryParams[i].Value));
                }
                url = sb.ToString();
            }
            return url;
        }

        private HttpRequestMessage BuildRequest()
        {
            var url = BuildUrl();
            var req = new HttpRequestMessage(_method, url);
            if (_body != null)
                req.Content = _body;

            foreach (var h in _headers)
                req.Headers.TryAddWithoutValidation(h.Key, h.Value);

            return req;
        }
    }
}
