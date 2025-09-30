using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Add CORS for local dev
builder.Services.AddCors();

// Add in-memory caching to reduce redundant work for repeated identical requests
// In-memory cache for fast, in-process caching
builder.Services.AddMemoryCache();
// Distributed cache (in-memory provider here). Swap to Redis or other provider for production.
builder.Services.AddDistributedMemoryCache();
// Response caching middleware (honors Cache-Control and Vary headers)
builder.Services.AddResponseCaching();

var app = builder.Build();

app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader()
);

// enable response caching middleware
app.UseResponseCaching();

// Optimized endpoint: returns standardized JSON
app.MapGet("/api/productlist", async (HttpContext http) =>
{
    // Use both IMemoryCache (fast, local) and IDistributedCache (shared across instances)
    var memoryCache = http.RequestServices.GetRequiredService<IMemoryCache>();
    var distributedCache = http.RequestServices.GetRequiredService<IDistributedCache>();
    const string cacheKey = "productlist";

    // Try memory cache first
    if (!memoryCache.TryGetValue(cacheKey, out (string Json, string ETag) cached))
    {
        // Try distributed cache (serialized JSON)
        var distJson = await distributedCache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(distJson))
        {
            // compute ETag from distributed JSON
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(distJson));
            var etag = '"' + Convert.ToBase64String(hash) + '"';
            cached = (distJson, etag);

            // populate memory cache for faster subsequent reads
            memoryCache.Set(cacheKey, cached, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });
        }
        else
        {
            // generate payload (expensive operation placeholder)
            var products = new[]
            {
                new
                {
                    Id = 1,
                    Name = "Laptop",
                    Price = 1200.50,
                    Stock = 25,
                    Category = new { Id = 101, Name = "Electronics" }
                },
                new
                {
                    Id = 2,
                    Name = "Headphones",
                    Price = 50.00,
                    Stock = 100,
                    Category = new { Id = 102, Name = "Accessories" }
                }
            };

            var json = JsonSerializer.Serialize(products);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
            var etag = '"' + Convert.ToBase64String(hash) + '"';

            cached = (json, etag);

            // set both caches
            memoryCache.Set(cacheKey, cached, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });

            await distributedCache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });
        }
    }

    // honor If-None-Match for conditional requests
    var requestEtag = http.Request.Headers["If-None-Match"].ToString();
    if (!string.IsNullOrEmpty(requestEtag) && requestEtag == cached.ETag)
    {
        http.Response.Headers["ETag"] = cached.ETag;
        return Results.StatusCode(304);
    }

    // Set caching headers: small client-side max-age and allow revalidation
    http.Response.Headers["ETag"] = cached.ETag;
    http.Response.Headers["Cache-Control"] = "public, max-age=10, must-revalidate";

    // Let the response caching middleware cache responses where appropriate
    // (it respects Cache-Control headers, so we set them above)
    return Results.Content(cached.Json, "application/json");
});

// Admin endpoint to invalidate cache keys (clears both memory and distributed caches)
app.MapDelete("/api/cache/{key}", (string key, HttpContext http) =>
{
    var memoryCache = http.RequestServices.GetRequiredService<IMemoryCache>();
    var distributedCache = http.RequestServices.GetRequiredService<IDistributedCache>();

    memoryCache.Remove(key);
    distributedCache.RemoveAsync(key);

    return Results.Ok(new { Cleared = key });
});

app.Run();
