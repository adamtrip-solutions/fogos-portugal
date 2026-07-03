using System.Globalization;
using Fogos.Domain.Time;
using Fogos.Importer.Mapping;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Importer;

/// <summary>
/// Parses the <c>import</c> CLI, wires the source/target Mongo databases, runs the importer,
/// and returns a process exit code (non-zero if any collection failed catastrophically).
/// </summary>
public static class ImportCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var sourceUri = GetOption(args, "--source");
        if (string.IsNullOrWhiteSpace(sourceUri))
        {
            Console.Error.WriteLine("import: --source <mongo-uri> is required.");
            PrintUsage();
            return 1;
        }

        var registry = new MapperRegistry(new FogosClock());

        var sourceDb = GetOption(args, "--source-db") ?? "fires";
        var source = new MongoClient(sourceUri).GetDatabase(sourceDb);

        // Target defaults to the configured Mongo (like seed); --target/--target-db override.
        var mongoOptions = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        var targetUri = GetOption(args, "--target");
        var targetDbName = GetOption(args, "--target-db") ?? mongoOptions.Database;
        var targetClient = targetUri is null
            ? services.GetRequiredService<IMongoClient>()
            : new MongoClient(targetUri);
        var target = targetClient.GetDatabase(targetDbName);

        var collections = ParseCollections(GetOption(args, "--collections"), registry);
        if (collections.Count == 0)
        {
            Console.Error.WriteLine("import: no valid collections selected.");
            return 1;
        }

        DateTimeOffset? since = null;
        var sinceRaw = GetOption(args, "--since");
        if (sinceRaw is not null)
        {
            if (!DateTimeOffset.TryParse(sinceRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                Console.Error.WriteLine($"import: --since '{sinceRaw}' is not a valid ISO-8601 timestamp.");
                return 1;
            }
            since = parsed;
        }

        var dryRun = HasFlag(args, "--dry-run");
        var settings = new ImportSettings { Collections = collections, Since = since, DryRun = dryRun };

        Console.WriteLine($"Importing from '{sourceDb}' ({Redact(sourceUri)}) into '{targetDbName}'"
            + (dryRun ? " [DRY RUN — nothing written]" : "")
            + (since is { } s ? $" [delta since {s:O}]" : "")
            + $" — {collections.Count} collection(s).");

        var runner = new ImportRunner(source, target, registry);
        var reports = await runner.RunAsync(settings);

        var failed = reports.Where(r => r.Failed).ToList();
        Console.WriteLine();
        Console.WriteLine($"Totals: read {reports.Sum(r => r.Read)}, "
            + $"{(dryRun ? "would upsert" : "upserted")} {reports.Sum(r => r.Upserted)}, "
            + $"quarantined {reports.Sum(r => r.Quarantined)}, "
            + $"skipped {reports.Sum(r => r.Skipped)}, "
            + $"failed {failed.Count}.");

        return failed.Count > 0 ? 3 : 0;
    }

    private static List<string> ParseCollections(string? raw, MapperRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return registry.DefaultCollections.ToList();

        var requested = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = new List<string>();
        foreach (var name in requested)
        {
            if (registry.TryGet(name, out _))
                valid.Add(name);
            else
                Console.Error.WriteLine($"import: skipping unknown collection '{name}'.");
        }
        return valid;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "  import --source <uri> [--source-db fires] [--target <uri>] [--target-db fogos]");
        Console.Error.WriteLine(
            "         [--collections a,b] [--since <ISO8601>] [--dry-run]");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        Array.Exists(args, a => string.Equals(a, name, StringComparison.Ordinal));

    /// <summary>Hides credentials in a connection string before logging it.</summary>
    private static string Redact(string uri)
    {
        var at = uri.IndexOf('@');
        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        return at > 0 && scheme > 0 ? uri[..(scheme + 3)] + "***@" + uri[(at + 1)..] : uri;
    }
}
