using Fogos.Domain.Auth;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.AdminCli;

/// <summary>
/// <c>keys</c> commands: issue (prints the plaintext once), list, revoke. Exit codes: 0 ok, 2 usage error.
/// </summary>
public static class KeyCommands
{
    private const int Ok = 0;
    private const int Usage = 2;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        var mongo = services.GetRequiredService<MongoContext>();

        return sub switch
        {
            "issue" => await IssueAsync(mongo, args),
            "list" => await ListAsync(mongo),
            "revoke" => await RevokeAsync(mongo, args),
            _ => UsageError("Unknown 'keys' subcommand. Use: issue | list | revoke."),
        };
    }

    private static async Task<int> IssueAsync(MongoContext mongo, string[] args)
    {
        var name = GetOption(args, "--name");
        var tierArg = GetOption(args, "--tier");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tierArg))
            return UsageError("keys issue requires --name and --tier.");

        if (!TryParseTier(tierArg, out var tier))
            return UsageError($"Invalid --tier '{tierArg}'. Use: registered | first-party | operator.");

        var scopes = ParseList(GetOption(args, "--scopes"));
        if (scopes.Count > 0)
        {
            // Scopes are only meaningful for operator credentials.
            if (tier != ApiTier.Operator)
                return UsageError("--scopes may only be set for --tier operator.");

            var unknown = scopes.Where(s => !ApiScopes.All.Contains(s)).ToList();
            if (unknown.Count > 0)
                return UsageError($"Unknown scope(s): {string.Join(", ", unknown)}. Valid: {string.Join(", ", ApiScopes.All)}.");
        }

        var publicContext = HasFlag(args, "--public-context");
        var origins = ParseList(GetOption(args, "--origins"));

        var plaintext = ApiKeyGenerator.NewPlaintext();
        var client = new ApiClient
        {
            Name = name,
            KeyHash = ApiKeyGenerator.Hash(plaintext),
            Tier = tier,
            Scopes = scopes,
            PublicContext = publicContext,
            AllowedOrigins = origins,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await mongo.ApiClients.InsertOneAsync(client);

        Console.WriteLine("API key issued. Store the plaintext now — it is not recoverable.");
        Console.WriteLine();
        Console.WriteLine($"  id:      {client.Id}");
        Console.WriteLine($"  name:    {client.Name}");
        Console.WriteLine($"  tier:    {tier}");
        if (scopes.Count > 0)
            Console.WriteLine($"  scopes:  {string.Join(", ", scopes)}");
        if (publicContext)
            Console.WriteLine($"  origins: {(origins.Count > 0 ? string.Join(", ", origins) : "(none)")}");
        Console.WriteLine();
        Console.WriteLine($"  API KEY: {plaintext}");
        return Ok;
    }

    private static async Task<int> ListAsync(MongoContext mongo)
    {
        var clients = await mongo.ApiClients
            .Find(FilterDefinition<ApiClient>.Empty)
            .SortBy(c => c.CreatedAt)
            .ToListAsync();

        Console.WriteLine($"{"ID",-26} {"NAME",-24} {"TIER",-11} {"PUBLIC",-6} {"CREATED",-20} {"REVOKED",-20} SCOPES");
        foreach (var c in clients)
        {
            Console.WriteLine(
                $"{c.Id,-26} {Trim(c.Name, 24),-24} {c.Tier,-11} {(c.PublicContext ? "yes" : "no"),-6} " +
                $"{c.CreatedAt:yyyy-MM-dd HH:mm:ss}  {(c.RevokedAt is { } r ? r.ToString("yyyy-MM-dd HH:mm:ss") : "-"),-20} " +
                $"{string.Join(",", c.Scopes)}");
        }

        Console.WriteLine();
        Console.WriteLine($"{clients.Count} key(s).");
        return Ok;
    }

    private static async Task<int> RevokeAsync(MongoContext mongo, string[] args)
    {
        var id = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : GetOption(args, "--id");
        if (string.IsNullOrWhiteSpace(id))
            return UsageError("keys revoke requires an <id>.");

        var result = await mongo.ApiClients.UpdateOneAsync(
            Builders<ApiClient>.Filter.Eq(c => c.Id, id),
            Builders<ApiClient>.Update.Set(c => c.RevokedAt, DateTimeOffset.UtcNow));

        if (result.MatchedCount == 0)
        {
            Console.Error.WriteLine($"No API key with id '{id}'.");
            return Usage;
        }

        Console.WriteLine($"Revoked API key '{id}'.");
        return Ok;
    }

    private static bool TryParseTier(string value, out ApiTier tier)
    {
        tier = value.ToLowerInvariant() switch
        {
            "registered" => ApiTier.Registered,
            "first-party" or "firstparty" => ApiTier.FirstParty,
            "operator" => ApiTier.Operator,
            _ => ApiTier.Anonymous,
        };
        return tier != ApiTier.Anonymous;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        return Usage;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }

    private static bool HasFlag(string[] args, string name) => args.Contains(name);

    private static List<string> ParseList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
