using System.Net.Http.Json;
using System.Text.Json;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.Minio;
using Testcontainers.MongoDb;
using Testcontainers.Redis;

namespace Fogos.Integration.Tests;

/// <summary>
/// Spins up MongoDB + Redis + MinIO via Testcontainers and boots the API through
/// <see cref="WebApplicationFactory{Program}"/>. Gates gracefully: when Docker is not
/// available, <see cref="Available"/> is false and tests short-circuit instead of failing.
/// </summary>
public sealed class ContainerFixture : IAsyncLifetime
{
    private MongoDbContainer? _mongo;
    private RedisContainer? _redis;
    private MinioContainer? _minio;
    private WebApplicationFactory<Program>? _factory;

    public bool Available { get; private set; }

    public string? SkipReason { get; private set; }

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Containers not available.");

    public string MongoConnectionString { get; private set; } = "";

    public string RedisConnectionString { get; private set; } = "";

    public string MinioEndpoint { get; private set; } = "";

    public string MinioAccessKey { get; private set; } = "";

    public string MinioSecretKey { get; private set; } = "";

    public const string PhotoBucket = "incident-photos";

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
        // Photo-upload gates likewise default high (all TestServer requests share one IP);
        // the gate-trip test builds its own low-limit factory.
        ["PhotoGate:PerIpPerMinute"] = "100000",
        ["PhotoGate:PerIncidentPerIpPerHour"] = "100000",
        ["PhotoGate:PerIncidentPerHour"] = "100000",
        ["PhotoGate:PendingPerIncident"] = "100000",
    };

    public async Task InitializeAsync()
    {
        try
        {
            _mongo = new MongoDbBuilder("mongo:7").Build();
            _redis = new RedisBuilder("redis:7").Build();
            // Pinned dated tag: the MinioBuilder default image is too old for AWS SDK v4's
            // chunked/trailing-checksum uploads (rejects them with an x-amz-content-sha256 mismatch).
            _minio = new MinioBuilder("minio/minio:RELEASE.2025-09-07T16-13-09Z").Build();
            await Task.WhenAll(_mongo.StartAsync(), _redis.StartAsync(), _minio.StartAsync());

            MongoConnectionString = _mongo.GetConnectionString();
            RedisConnectionString = _redis.GetConnectionString();
            MinioEndpoint = _minio.GetConnectionString();
            MinioAccessKey = _minio.GetAccessKey();
            MinioSecretKey = _minio.GetSecretKey();

            await EnsurePhotoBucketAsync();

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

    private async Task EnsurePhotoBucketAsync()
    {
        using var s3 = CreateS3Client();
        await s3.PutBucketAsync(PhotoBucket);
    }

    /// <summary>Raw S3 client against the MinIO container (bucket setup, object assertions).</summary>
    public AmazonS3Client CreateS3Client() =>
        new(MinioAccessKey, MinioSecretKey, new AmazonS3Config
        {
            ServiceURL = MinioEndpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        });

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_minio is not null)
            await _minio.DisposeAsync();
        if (_redis is not null)
            await _redis.DisposeAsync();
        if (_mongo is not null)
            await _mongo.DisposeAsync();
    }

    /// <summary>
    /// Builds a factory pointed at the shared Mongo/Redis/MinIO containers. Defaults to high limits; pass
    /// <paramref name="overrides"/> (e.g. a low tier limit) for rate-limit-specific tests.
    /// </summary>
    public WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? overrides = null)
    {
        var config = HighLimitConfig();
        config["Mongo:ConnectionString"] = MongoConnectionString;
        config["Mongo:Database"] = Database;
        config["Redis:ConnectionString"] = RedisConnectionString;
        config["ObjectStorage:PublicBaseUrl"] = "https://cdn.example.test";
        config["ObjectStorage:Endpoint"] = MinioEndpoint;
        config["ObjectStorage:AccessKey"] = MinioAccessKey;
        config["ObjectStorage:SecretKey"] = MinioSecretKey;
        config["ObjectStorage:Bucket"] = PhotoBucket;
        config["ObjectStorage:Region"] = "us-east-1";
        config["ObjectStorage:ForcePathStyle"] = "true";
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

    /// <summary>POST a GraphQL operation with an <c>X-API-Key</c> header (scope-gated fields).</summary>
    public async Task<JsonDocument> GraphQLAsync(string apiKey, string query, object? variables = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

[CollectionDefinition("fogos")]
public sealed class FogosCollection : ICollectionFixture<ContainerFixture>;
