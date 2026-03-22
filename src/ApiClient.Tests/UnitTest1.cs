using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ApiClient.Tests
{
    // ─── Mock HttpMessageHandler ──────────────────────────────────

    public class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
        public int CallCount => Requests.Count;

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public MockHandler(HttpStatusCode status, string body = "")
            : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }) { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }

    // ─── RestClient Tests ─────────────────────────────────────────

    public class RestClientTests
    {
        [Fact]
        public async Task Get_ReturnsResponse()
        {
            var handler = new MockHandler(HttpStatusCode.OK, "{\"name\":\"test\"}");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            var resp = await client.Get("/users").SendAsync();

            Assert.True(resp.IsSuccess);
            Assert.Equal(200, resp.StatusCode);
            Assert.Contains("test", resp.Body);
        }

        [Fact]
        public async Task Post_SendsJsonBody()
        {
            var handler = new MockHandler(req =>
            {
                var body = req.Content.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(body)
                };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            var resp = await client.Post("/users")
                .JsonBody(new { Name = "John", Age = 30 })
                .SendAsync();

            Assert.Equal(201, resp.StatusCode);
            Assert.Contains("John", resp.Body);
        }

        [Fact]
        public async Task QueryParams_AppendedToUrl()
        {
            var handler = new MockHandler(req =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(req.RequestUri.Query)
                };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            var resp = await client.Get("/search")
                .Query("q", "hello")
                .Query("page", "1")
                .SendAsync();

            Assert.Contains("q=hello", resp.Body);
            Assert.Contains("page=1", resp.Body);
        }

        [Fact]
        public async Task BearerToken_SetsAuthHeader()
        {
            var handler = new MockHandler(req =>
            {
                var auth = req.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(auth?.ToString() ?? "none")
                };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com").BearerToken("my-token");

            var resp = await client.Get("/me").SendAsync();
            Assert.Contains("Bearer my-token", resp.Body);
        }

        [Fact]
        public async Task TypedGet_DeserializesJson()
        {
            var handler = new MockHandler(HttpStatusCode.OK, "{\"Id\":1,\"Name\":\"Widget\"}");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            var product = await client.GetAsync<TestProduct>("/products/1");

            Assert.Equal(1, product.Id);
            Assert.Equal("Widget", product.Name);
        }

        [Fact]
        public async Task EnsureSuccess_ThrowsOnError()
        {
            var handler = new MockHandler(HttpStatusCode.NotFound, "Not Found");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            await Assert.ThrowsAsync<ApiException>(() =>
                client.GetAsync<TestProduct>("/products/999"));
        }

        [Fact]
        public async Task ResolveUrl_Absolute_PassesThrough()
        {
            var handler = new MockHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(req.RequestUri.ToString())
                });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            var resp = await client.Get("https://other.com/path").SendAsync();
            Assert.Contains("other.com/path", resp.Body);
        }

        [Fact]
        public async Task FormBody_SendsFormEncoded()
        {
            var handler = new MockHandler(req =>
            {
                Assert.Equal("application/x-www-form-urlencoded", req.Content.Headers.ContentType.MediaType);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            var resp = await client.Post("/login")
                .FormBody(new Dictionary<string, string>
                {
                    ["username"] = "admin",
                    ["password"] = "secret"
                })
                .SendAsync();

            Assert.True(resp.IsSuccess);
        }

        [Fact]
        public async Task AllHttpMethods_Work()
        {
            var methods = new List<string>();
            var handler = new MockHandler(req =>
            {
                methods.Add(req.Method.Method);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com");

            await client.Get("/x").SendAsync();
            await client.Post("/x").SendAsync();
            await client.Put("/x").SendAsync();
            await client.Patch("/x").SendAsync();
            await client.Delete("/x").SendAsync();

            Assert.Equal(new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }, methods);
        }
    }

    // ─── Middleware Tests ──────────────────────────────────────────

    public class MiddlewareTests
    {
        [Fact]
        public async Task LoggingMiddleware_LogsRequestAndResponse()
        {
            var logs = new List<string>();
            var handler = new MockHandler(HttpStatusCode.OK, "ok");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com")
                .Use(Middleware.Logging(msg => logs.Add(msg)));

            await client.Get("/test").SendAsync();

            Assert.Equal(2, logs.Count);
            Assert.Contains("→ GET", logs[0]);
            Assert.Contains("← 200", logs[1]);
        }

        [Fact]
        public async Task TimingMiddleware_AddsDurationHeader()
        {
            var handler = new MockHandler(HttpStatusCode.OK, "ok");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com")
                .Use(Middleware.Timing());

            var resp = await client.Get("/test").SendAsync();

            Assert.True(resp.Headers.ContainsKey("X-Duration-Ms"));
        }

        [Fact]
        public async Task ErrorOnStatus_ThrowsOnHighStatus()
        {
            var handler = new MockHandler(HttpStatusCode.BadRequest, "bad");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com")
                .Use(Middleware.ErrorOnStatus(400));

            await Assert.ThrowsAsync<ApiException>(() =>
                client.Get("/fail").SendAsync());
        }

        [Fact]
        public async Task MiddlewarePipeline_ExecutesInOrder()
        {
            var order = new List<string>();
            var handler = new MockHandler(HttpStatusCode.OK, "ok");
            var http = new HttpClient(handler);

            var client = new RestClient(http, "https://api.test.com")
                .Use(async (ctx, next, ct) => { order.Add("A-before"); var r = await next(ct); order.Add("A-after"); return r; })
                .Use(async (ctx, next, ct) => { order.Add("B-before"); var r = await next(ct); order.Add("B-after"); return r; });

            await client.Get("/test").SendAsync();

            Assert.Equal(new[] { "A-before", "B-before", "B-after", "A-after" }, order);
        }
    }

    // ─── CircuitBreaker Tests ─────────────────────────────────────

    public class CircuitBreakerTests
    {
        [Fact]
        public void InitialState_IsClosed()
        {
            var cb = new CircuitBreaker(3);
            Assert.Equal(CircuitState.Closed, cb.State);
        }

        [Fact]
        public void AfterThresholdFailures_OpensCircuit()
        {
            var cb = new CircuitBreaker(3);
            cb.RecordFailure();
            cb.RecordFailure();
            cb.RecordFailure();

            Assert.Equal(CircuitState.Open, cb.State);
        }

        [Fact]
        public void OpenCircuit_ThrowsOnEnsureAllowed()
        {
            var cb = new CircuitBreaker(2);
            cb.RecordFailure();
            cb.RecordFailure();

            Assert.Throws<CircuitOpenException>(() => cb.EnsureAllowed());
        }

        [Fact]
        public void Success_ResetsFailureCount()
        {
            var cb = new CircuitBreaker(3);
            cb.RecordFailure();
            cb.RecordFailure();
            cb.RecordSuccess(); // resets

            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.Equal(0, cb.FailureCount);
        }

        [Fact]
        public void OpenCircuit_TransitionsToHalfOpen_AfterTimeout()
        {
            var cb = new CircuitBreaker(2, TimeSpan.FromMilliseconds(50));
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);

            Thread.Sleep(100);
            Assert.Equal(CircuitState.HalfOpen, cb.State);
        }

        [Fact]
        public void Reset_ReturnsToClosed()
        {
            var cb = new CircuitBreaker(2);
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);

            cb.Reset();
            Assert.Equal(CircuitState.Closed, cb.State);
        }

        [Fact]
        public async Task AsMiddleware_OpensOnServerErrors()
        {
            var cb = new CircuitBreaker(2, TimeSpan.FromSeconds(30));
            var handler = new MockHandler(HttpStatusCode.InternalServerError, "error");
            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com")
                .Use(cb.AsMiddleware());

            await client.Get("/fail").SendAsync(); // failure 1
            await client.Get("/fail").SendAsync(); // failure 2 → opens

            await Assert.ThrowsAsync<CircuitOpenException>(() =>
                client.Get("/fail").SendAsync()); // blocked
        }
    }

    // ─── ResponseCache Tests ──────────────────────────────────────

    public class ResponseCacheTests
    {
        [Fact]
        public void Set_And_Get_Works()
        {
            var cache = new ResponseCache(TimeSpan.FromMinutes(5));
            var resp = new ApiResponse { StatusCode = 200, Body = "cached" };

            cache.Set("/test", resp);

            Assert.True(cache.TryGet("/test", out var hit));
            Assert.Equal("cached", hit.Body);
        }

        [Fact]
        public void ExpiredEntry_ReturnsNotFound()
        {
            var cache = new ResponseCache(TimeSpan.FromMilliseconds(50));
            cache.Set("/test", new ApiResponse { StatusCode = 200, Body = "cached" });

            Thread.Sleep(100);

            Assert.False(cache.TryGet("/test", out _));
        }

        [Fact]
        public void Invalidate_RemovesEntry()
        {
            var cache = new ResponseCache(TimeSpan.FromMinutes(5));
            cache.Set("/test", new ApiResponse { StatusCode = 200 });

            cache.Invalidate("/test");
            Assert.False(cache.TryGet("/test", out _));
        }

        [Fact]
        public void InvalidatePrefix_RemovesMatching()
        {
            var cache = new ResponseCache(TimeSpan.FromMinutes(5));
            cache.Set("/api/users/1", new ApiResponse { StatusCode = 200 });
            cache.Set("/api/users/2", new ApiResponse { StatusCode = 200 });
            cache.Set("/api/products/1", new ApiResponse { StatusCode = 200 });

            cache.InvalidatePrefix("/api/users");

            Assert.Equal(1, cache.Count);
            Assert.True(cache.TryGet("/api/products/1", out _));
        }

        [Fact]
        public async Task AsMiddleware_CachesGetRequests()
        {
            var cache = new ResponseCache(TimeSpan.FromMinutes(5));
            int callCount = 0;
            var handler = new MockHandler(_ =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data") };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com")
                .Use(cache.AsMiddleware());

            var resp1 = await client.Get("/data").SendAsync();
            var resp2 = await client.Get("/data").SendAsync(); // should hit cache

            Assert.Equal(1, callCount); // only one actual HTTP call
            Assert.Equal("HIT", resp2.Headers["X-Cache"]);
        }
    }

    // ─── RateLimiter Tests ────────────────────────────────────────

    public class RateLimiterTests
    {
        [Fact]
        public async Task AllowsRequestsUpToLimit()
        {
            var limiter = new RateLimiter(100); // 100/sec
            // Should be near-instant
            for (int i = 0; i < 10; i++)
                await limiter.WaitAsync();
        }

        [Fact]
        public void Constructor_CustomWindow_Works()
        {
            var limiter = new RateLimiter(10, TimeSpan.FromSeconds(5));
            Assert.True(limiter.AvailableTokens > 0);
        }
    }

    // ─── ApiResponse Tests ────────────────────────────────────────

    public class ApiResponseTests
    {
        [Fact]
        public void IsClientError_TrueFor4xx()
        {
            var resp = new ApiResponse { StatusCode = 404 };
            Assert.True(resp.IsClientError);
            Assert.False(resp.IsServerError);
        }

        [Fact]
        public void IsServerError_TrueFor5xx()
        {
            var resp = new ApiResponse { StatusCode = 503 };
            Assert.True(resp.IsServerError);
            Assert.False(resp.IsClientError);
        }

        [Fact]
        public void As_DeserializesJson()
        {
            var resp = new ApiResponse { Body = "{\"Id\":42,\"Name\":\"Test\"}" };
            var obj = resp.As<TestProduct>();
            Assert.Equal(42, obj.Id);
            Assert.Equal("Test", obj.Name);
        }

        [Fact]
        public void EnsureSuccess_ThrowsOnFailure()
        {
            var resp = new ApiResponse { StatusCode = 500, IsSuccess = false, Body = "Error" };
            Assert.Throws<ApiException>(() => resp.EnsureSuccess());
        }
    }

    // ─── Retry Tests ──────────────────────────────────────────────

    public class RetryTests
    {
        [Fact]
        public async Task RetryOnServerError_RetriesAndSucceeds()
        {
            int attempt = 0;
            var handler = new MockHandler(_ =>
            {
                attempt++;
                return attempt < 3
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("fail") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            });

            var http = new HttpClient(handler);
            var client = new RestClient(http, "https://api.test.com",
                retry: new RetryOptions
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(10),
                    UseJitter = false
                });

            var resp = await client.Get("/unstable").SendAsync();

            Assert.Equal(200, resp.StatusCode);
            Assert.Equal(3, attempt);
        }
    }

    // ─── Test Models ──────────────────────────────────────────────

    public class TestProduct
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
