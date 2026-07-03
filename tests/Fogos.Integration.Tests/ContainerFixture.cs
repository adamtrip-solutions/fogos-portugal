using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

    public string Database { get; } = "fogos_test_" + Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        try
        {
            _mongo = new MongoDbBuilder("mongo:7").Build();
            _redis = new RedisBuilder("redis:7").Build();
            await _mongo.StartAsync();
            await _redis.StartAsync();

            var mongoConn = _mongo.GetConnectionString();
            var redisConn = _redis.GetConnectionString();

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Mongo:ConnectionString"] = mongoConn,
                        ["Mongo:Database"] = Database,
                        ["Redis:ConnectionString"] = redisConn,
                        ["ObjectStorage:PublicBaseUrl"] = "https://cdn.example.test",
                    });
                });
            });

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
