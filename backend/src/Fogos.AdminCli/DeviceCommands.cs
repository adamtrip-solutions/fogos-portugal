using Fogos.Domain.Devices;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.AdminCli;

/// <summary>
/// <c>devices</c> commands: revoke a mobile app device by id. Revoking sets <c>Revoked=true</c>, which kills
/// its <c>X-Device-Key</c> credential (honoured within the API resolver's ≤60s cache) and makes push/expo
/// delivery skip it. Exit codes: 0 ok, 2 usage error.
/// </summary>
public static class DeviceCommands
{
    private const int Ok = 0;
    private const int Usage = 2;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        var mongo = services.GetRequiredService<MongoContext>();

        return sub switch
        {
            "revoke" => await RevokeAsync(mongo, args),
            _ => UsageError("Unknown 'devices' subcommand. Use: revoke."),
        };
    }

    private static async Task<int> RevokeAsync(MongoContext mongo, string[] args)
    {
        var id = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : GetOption(args, "--id");
        if (string.IsNullOrWhiteSpace(id))
            return UsageError("devices revoke requires an <id>.");

        var result = await mongo.Devices.UpdateOneAsync(
            Builders<Device>.Filter.Eq(d => d.Id, id),
            Builders<Device>.Update.Set(d => d.Revoked, true));

        if (result.MatchedCount == 0)
        {
            Console.Error.WriteLine($"No device with id '{id}'.");
            return Usage;
        }

        Console.WriteLine($"Revoked device '{id}'. Its X-Device-Key credential dies within ~60s and push/expo delivery now skips it.");
        return Ok;
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
}
