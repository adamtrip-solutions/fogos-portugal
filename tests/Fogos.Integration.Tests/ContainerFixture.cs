using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.MongoDb;
using Testcontainers.Redis;

namespace Fogos.Integration.Tests;

/// <summary>
/// Spins up MongoDB + Redis via Testcontainers and boots the API through
/// <see cref="WebApplicationFactory{Program}"/>. Gates gracefully: when Docker is not
/// available, <see cref="Available"/> is false and tests short-circuit instead of failing.
/// </summary>
public sealed class ContainerFixture : IAsyncLifetime
{
    private MongoDbContainer? _mongo;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;

    public bool Available { get; private set; }

    public string? SkipReason { get; private set; }

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Containers not available.");

    public string MongoConnectionString { get; private set; } = "";

    public string RedisConnectionString { get; private set; } = "";

    public string Database { get; } = "fogos_test_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// High-limit defaults so the existing (anonymous) read/REST tests never trip the limiter.
    /// Rate-limit-specific tests spin up their own factory with low limits via <see cref="CreateFactory"/>.
    /// </summary>
    private static Dictionary<string, string?> HighLimitConfig() => new()
    {
        ["RateLimit:WindowSeconds"] = "60",
        ["RateLimit:Anonymous:Requests"] = "1000000",
        ["RateLimit:Anonymous:CostBudget"] = "1000000000",
        ["RateLimit:Anonymous:Subscriptions"] = "1000",
        ["RateLimit:Registered:Requests"] = "1000000",
        ["RateLimit:Registered:CostBudget"] = "1000000000",
        ["RateLimit:Registered:Subscriptions"] = "1000",
        ["RateLimit:FirstParty:Requests"] = "1000000",
        ["RateLimit:FirstParty:CostBudget"] = "1000000000",
        ["RateLimit:FirstParty:Subscriptions"] = "1000",
        ["RateLimit:Operator:Requests"] = "1000000",
        ["RateLimit:Operator:CostBudget"] = "1000000000",
        ["RateLimit:Operator:Subscriptions"] = "1000",
    };

    public async Task InitializeAsync()
    {
        try
        {
            _mongo = new MongoDbBuilder("mongo:7").Build();
            _redis = new RedisBuilder("redis:7").Build();
            await _mongo.StartAsync();
            await _redis.StartAsync();

            MongoConnectionString = _mongo.GetConnectionString();
            RedisConnectionString = _redis.GetConnectionString();

            _factory = CreateFactory();

            // Force the host to build so config/services are ready.
            _ = _factory.Services;
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = $"Docker/containers unavailable: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_redis is not null)
            await _redis.DisposeAsync();
        if (_mongo is not null)
            await _mongo.DisposeAsync();
    }

    /// <summary>
    /// Builds a factory pointed at the shared Mongo/Redis containers. Defaults to high limits; pass
    /// <paramref name="overrides"/> (e.g. a low tier limit) for rate-limit-specific tests.
    /// </summary>
    public WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? overrides = null)
    {
        var config = HighLimitConfig();
        config["Mongo:ConnectionString"] = MongoConnectionString;
        config["Mongo:Database"] = Database;
        config["Redis:ConnectionString"] = RedisConnectionString;
        config["ObjectStorage:PublicBaseUrl"] = "https://cdn.example.test";
        if (overrides is not null)
            foreach (var (k, v) in overrides)
                config[k] = v;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(config)));
    }

    /// <summary>Flushes Redis so limiter/counter state does not leak between tests.</summary>
    public async Task FlushRedisAsync()
    {
        var config = ConfigurationOptions.Parse(RedisConnectionString);
        config.AllowAdmin = true;
        await using var mux = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = mux.GetEndPoints()[0];
        await mux.GetServer(endpoint).FlushDatabaseAsync();
    }

    /// <summary>POST a GraphQL operation and return the parsed JSON document.</summary>
    public async Task<JsonDocument> GraphQLAsync(string query, object? variables = null)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

[CollectionDefinition("fogos")]
public sealed class FogosCollection : ICollectionFixture<ContainerFixture>;
