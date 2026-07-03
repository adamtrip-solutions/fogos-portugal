using Fogos.Importer;
using Fogos.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Args are parsed by hand (no positional-arg config provider), so build the host without them.
var builder = Host.CreateApplicationBuilder();

// Lowest-priority dev defaults; appsettings and FOGOS_-prefixed env vars override.
builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
{
    InitialData = new Dictionary<string, string?>
    {
        ["Mongo:ConnectionString"] = "mongodb://localhost:27017/?directConnection=true",
        ["Mongo:Database"] = "fogos",
    },
});
builder.Configuration.AddEnvironmentVariables("FOGOS_");

builder.Services.AddFogosInfrastructure(builder.Configuration);

using var host = builder.Build();

var command = args.Length > 0 ? args[0] : "";
switch (command)
{
    case "seed":
        var path = GetOption(args, "--path") ?? "dev/seed";
        return await Seeder.RunAsync(host.Services, path);

    case "import":
        Console.WriteLine("importer arrives in Phase 1");
        return 2;

    default:
        Console.Error.WriteLine("Usage: Fogos.Importer <command>");
        Console.Error.WriteLine("  seed [--path <dir>]   load dev fixtures into MongoDB (default dev/seed)");
        Console.Error.WriteLine("  import ...            legacy Mongo import (Phase 1)");
        return 1;
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.Ordinal))
            return args[i + 1];
    return null;
}
