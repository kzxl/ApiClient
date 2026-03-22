# ⚡ ApiClient — High-Performance REST Client for .NET

> **Fluent API + Built-in Middleware, Circuit Breaker, Caching, and Rate Limiting — no Polly needed.**

[![.NET Standard](https://img.shields.io/badge/.NET%20Standard-2.0-blue)](https://docs.microsoft.com/en-us/dotnet/standard/net-standard)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

## Why ApiClient over RestSharp?

| Feature | ApiClient | RestSharp |
|---------|-----------|-----------|
| **Middleware Pipeline** | ✅ Built-in (logging, timing, error, custom) | ❌ Not available |
| **Circuit Breaker** | ✅ Built-in (3-state: Closed/Open/HalfOpen) | ❌ Requires Polly |
| **Response Caching** | ✅ Built-in (TTL, prefix invalidation) | ❌ Not available |
| **Rate Limiter** | ✅ Built-in (token bucket) | ❌ Not available |
| **JSON Serializer** | System.Text.Json (5x faster) | Newtonsoft.Json |
| **Retry** | ✅ Exponential backoff + jitter | ✅ Via interceptors |
| **Typed Shortcuts** | ✅ `GetAsync<T>()`, `PostAsync<T>()` | ✅ Generic methods |
| **Dependencies** | 1 (System.Text.Json) | Many |

---

## 🚀 Quick Start

```csharp
var client = new RestClient("https://api.example.com")
    .BearerToken("your-token")
    .Use(Middleware.Logging(Console.WriteLine))
    .Use(Middleware.Timing());

// Typed GET
var users = await client.GetAsync<List<User>>("/users");

// POST with JSON body
var created = await client.PostAsync<CreateUserDto, User>("/users", newUser);

// Fluent builder
var resp = await client.Get("/search")
    .Query("q", "keyword")
    .Query("page", "1")
    .Header("X-Custom", "value")
    .SendAsync();
```

---

## 🔌 Middleware Pipeline

Middlewares wrap the HTTP call in an **onion model** (like Express.js, ASP.NET Core):

```csharp
var client = new RestClient("https://api.example.com")
    .Use(Middleware.Logging(Console.WriteLine))   // log request/response
    .Use(Middleware.Timing())                      // add X-Duration-Ms header
    .Use(Middleware.ErrorOnStatus(400))            // throw on 4xx/5xx
    .Use(circuitBreaker.AsMiddleware())            // circuit breaker
    .Use(cache.AsMiddleware())                     // response caching
    .Use(rateLimiter.AsMiddleware());              // rate limiting

// Custom middleware
client.Use(async (ctx, next, ct) =>
{
    Console.WriteLine($"Before: {ctx.Method} {ctx.Url}");
    var response = await next(ct);
    Console.WriteLine($"After: {response.StatusCode}");
    return response;
});
```

---

## 🔒 Circuit Breaker (No Polly!)

```csharp
var breaker = new CircuitBreaker(
    failureThreshold: 5,
    openDuration: TimeSpan.FromSeconds(30)
);

var client = new RestClient("https://api.example.com")
    .Use(breaker.AsMiddleware());

// After 5 consecutive 5xx errors → circuit opens
// All requests throw CircuitOpenException for 30s
// Then transitions to HalfOpen → allows 1 probe request
```

## 📦 Response Caching

```csharp
var cache = new ResponseCache(
    defaultTtl: TimeSpan.FromMinutes(5),
    maxEntries: 1000
);

var client = new RestClient("https://api.example.com")
    .Use(cache.AsMiddleware());

// GET requests are automatically cached
// X-Cache header: HIT or MISS
cache.InvalidatePrefix("/api/users"); // invalidate by prefix
```

## ⏱️ Rate Limiter

```csharp
var limiter = new RateLimiter(maxRequestsPerSecond: 10);
// or: new RateLimiter(100, TimeSpan.FromMinutes(1));

var client = new RestClient("https://api.example.com")
    .Use(limiter.AsMiddleware());
```

## 🔄 Retry with Backoff

```csharp
var client = new RestClient("https://api.example.com",
    retry: new RetryOptions
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(10),
        UseJitter = true // prevent thundering herd
    });
```

---

## 📖 API Reference

```csharp
// RestClient
new RestClient(baseUrl, timeout?, retry?)
    .Use(middleware)                  // add middleware
    .BearerToken(token)              // set auth
    .BasicAuth(user, pass)           // basic auth
    .DefaultHeader(name, value)      // default header
    .Get/Post/Put/Patch/Delete(path) // create request

// RequestBuilder
    .Header(name, value)             // request header
    .Query(name, value)              // query param
    .JsonBody(object)                // JSON body
    .FormBody(dict)                  // form body
    .RawBody(content, contentType)   // raw body
    .BearerToken(token)              // per-request auth
    .SendAsync(ct)                   // execute

// ApiResponse
    .StatusCode / .Body / .IsSuccess
    .As<T>()                         // deserialize JSON
    .EnsureSuccess()                 // throw on error
    .IsClientError / .IsServerError
    .GetHeader(name)
```

---

## 📄 License

Apache License 2.0
