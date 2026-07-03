using Fogos.AdminCli;
using Fogos.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

// Args are parsed by hand, so build the host without them (mirrors Fogos.Importer).
var builder = Host.CreateApplicationBuilder();

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
    case "keys":
        return await KeyCommands.RunAsync(host.Services, args);

    default:
        Console.Error.WriteLine("Usage: Fogos.AdminCli <command>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  keys issue --name <n> --tier <registered|first-party|operator>");
        Console.Error.WriteLine("             [--scopes write:incidents,write:warnings,moderate:photos]  (operator only)");
        Console.Error.WriteLine("             [--public-context] [--origins https://fogos.pt,*.fogos.pt]");
        Console.Error.WriteLine("                 issue a new fgs_live_ API key (plaintext printed once)");
        Console.Error.WriteLine("  keys list       list issued keys (id, name, tier, scopes, publicContext, created, revoked)");
        Console.Error.WriteLine("  keys revoke <id>  revoke a key by id");
        return 2;
}
