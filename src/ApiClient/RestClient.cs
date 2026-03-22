using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient
{
    /// <summary>
    /// Fluent HTTP client for making REST API calls.
    /// Thread-safe: shares a single HttpClient internally.
    /// 
    /// Advantages over RestSharp:
    /// - Built-in middleware pipeline (logging, timing, error handling)
    /// - Built-in circuit breaker (no Polly needed)
    /// - Built-in response caching with TTL
    /// - Built-in rate limiter (token bucket)
    /// - Retry with exponential backoff and jitter
    /// </summary>
    public class RestClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly Dictionary<string, string> _defaultHeaders = new Dictionary<string, string>();
        private readonly RetryOptions _retry;
        private readonly List<MiddlewareFunc> _middlewares = new List<MiddlewareFunc>();

        /// <summary>
        /// Creates a new RestClient.
        /// </summary>
        public RestClient(string baseUrl = null, TimeSpan? timeout = null, RetryOptions retry = null)
        {
            _baseUrl = baseUrl?.TrimEnd('/');
            _http = new HttpClient();
            if (timeout.HasValue)
                _http.Timeout = timeout.Value;
            _retry = retry;
        }

        /// <summary>
        /// Creates a new RestClient wrapping an existing HttpClient (for DI/testing).
        /// </summary>
        public RestClient(HttpClient httpClient, string baseUrl = null, RetryOptions retry = null)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = baseUrl?.TrimEnd('/');
            _retry = retry;
        }

        /// <summary>
        /// Adds a middleware to the request pipeline.
        /// Middlewares execute in the order they are added.
        /// </summary>
        public RestClient Use(MiddlewareFunc middleware)
        {
            _middlewares.Add(middleware);
            return this;
        }

        /// <summary>
        /// Sets a default header for all requests.
        /// </summary>
        public RestClient DefaultHeader(string name, string value)
        {
            _defaultHeaders[name] = value;
            return this;
        }

        /// <summary>
        /// Sets the default Bearer token for all requests.
        /// </summary>
        public RestClient BearerToken(string token)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return this;
        }

        /// <summary>
        /// Sets basic authentication for all requests.
        /// </summary>
        public RestClient BasicAuth(string username, string password)
        {
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", creds);
            return this;
        }

        // ─── HTTP Methods ─────────────────────────────────────────

        /// <summary>Creates a GET request builder.</summary>
        public RequestBuilder Get(string path) => CreateRequest(HttpMethod.Get, path);

        /// <summary>Creates a POST request builder.</summary>
        public RequestBuilder Post(string path) => CreateRequest(HttpMethod.Post, path);

        /// <summary>Creates a PUT request builder.</summary>
        public RequestBuilder Put(string path) => CreateRequest(HttpMethod.Put, path);

        /// <summary>Creates a PATCH request builder.</summary>
        public RequestBuilder Patch(string path) => CreateRequest(new HttpMethod("PATCH"), path);

        /// <summary>Creates a DELETE request builder.</summary>
        public RequestBuilder Delete(string path) => CreateRequest(HttpMethod.Delete, path);

        // ─── Typed Shortcuts ──────────────────────────────────────

        /// <summary>GET and deserialize JSON response.</summary>
        public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
        {
            var resp = await Get(path).SendAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccess();
            return resp.As<T>();
        }

        /// <summary>POST JSON body and deserialize response.</summary>
        public async Task<TResponse> PostAsync<TRequest, TResponse>(
            string path, TRequest body, CancellationToken ct = default)
        {
            var resp = await Post(path).JsonBody(body).SendAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccess();
            return resp.As<TResponse>();
        }

        /// <summary>POST JSON body (no response body needed).</summary>
        public async Task PostAsync<TRequest>(
            string path, TRequest body, CancellationToken ct = default)
        {
            var resp = await Post(path).JsonBody(body).SendAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccess();
        }

        /// <summary>PUT JSON body and deserialize response.</summary>
        public async Task<TResponse> PutAsync<TRequest, TResponse>(
            string path, TRequest body, CancellationToken ct = default)
        {
            var resp = await Put(path).JsonBody(body).SendAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccess();
            return resp.As<TResponse>();
        }

        /// <summary>DELETE and return success.</summary>
        public async Task DeleteAsync(string path, CancellationToken ct = default)
        {
            var resp = await Delete(path).SendAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccess();
        }

        // ─── Internal ─────────────────────────────────────────────

        internal RequestBuilder CreateRequest(HttpMethod method, string path)
        {
            return new RequestBuilder(_http, method, ResolveUrl(path), _defaultHeaders, _retry, _middlewares);
        }

        internal string ResolveUrl(string path)
        {
            if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
                return path;
            if (_baseUrl == null)
                return path;
            return $"{_baseUrl}/{path.TrimStart('/')}";
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
