using Fogos.AdminCli;
using Fogos.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

// Args are parsed by hand, so build the host without them (mirrors Fogos.Importer).
var builder = Host.CreateApplicationBuilder();

var command = args.Length > 0 ? args[0] : "";

builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
{
    InitialData = new Dictionary<string, string?>
    {
        ["Mongo:ConnectionString"] = "mongodb://localhost:27017/?directConnection=true",
        ["Mongo:Database"] = "fogos",
    },
});
builder.Configuration.AddEnvironmentVariables("FOGOS_");

// demo-seed pins its target database up-front (highest precedence) so the infrastructure binds to it and
// never the production `fogos` db — even if FOGOS_Mongo__Database points elsewhere in the environment.
if (command == "demo-seed")
{
    builder.Configuration.Sources.Add(new MemoryConfigurationSource
    {
        InitialData = new Dictionary<string, string?> { ["Mongo:Database"] = DemoSeedCommand.DatabaseArg(args) },
    });
}

builder.Services.AddFogosInfrastructure(builder.Configuration);

using var host = builder.Build();

switch (command)
{
    case "keys":
        return await KeyCommands.RunAsync(host.Services, args);

    case "devices":
        return await DeviceCommands.RunAsync(host.Services, args);

    case "demo-seed":
        return await DemoSeedCommand.RunAsync(host.Services, args);

    case "webpush-keys":
        return WebPushCommands.Run(args);

    default:
        Console.Error.WriteLine("Usage: Fogos.AdminCli <command>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  keys issue --name <n> --tier <registered|first-party|operator>");
        Console.Error.WriteLine("             [--scopes write:incidents,write:warnings,moderate:photos]  (operator only)");
        Console.Error.WriteLine("             [--public-context] [--origins https://fogos.pt,*.fogos.pt]");
        Console.Error.WriteLine("                 issue a new fgs_live_ API key (plaintext printed once)");
        Console.Error.WriteLine("  keys list       list issued keys (id, name, tier, scopes, publicContext, created, revoked)");
        Console.Error.WriteLine("  keys revoke <id>  revoke a key by id");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  devices revoke <id>  revoke a mobile app device by id (kills its X-Device-Key + push)");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  demo-seed [--database fogos_demo] [--drop] [--locations <path>]");
        Console.Error.WriteLine("                 populate a demo database with deterministic, live-looking sample data");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  webpush-keys [--subject mailto:you@example.com]");
        Console.Error.WriteLine("                 generate a VAPID keypair and print ready-to-paste WebPush__ env lines");
        return 2;
}
