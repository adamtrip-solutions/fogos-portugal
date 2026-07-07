using System.Security.Cryptography;

namespace Fogos.AdminCli;

/// <summary>
/// <c>webpush-keys</c>: generates a VAPID (P-256) keypair and prints ready-to-paste env lines. The public
/// key is the uncompressed EC point (0x04‖X‖Y) base64url-encoded; the private key is the <c>d</c> scalar
/// base64url-encoded — the exact shape <c>Lib.Net.Http.WebPush</c>'s <c>VapidAuthentication</c> and browser
/// <c>PushManager.subscribe({ applicationServerKey })</c> expect.
/// </summary>
public static class WebPushCommands
{
    public static int Run(string[] args)
    {
        var subject = GetOption(args, "--subject") ?? "mailto:admin@fogos.pt";

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);

        // Uncompressed point: 0x04 ‖ X(32) ‖ Y(32).
        var pointBytes = new byte[65];
        pointBytes[0] = 0x04;
        Buffer.BlockCopy(p.Q.X!, 0, pointBytes, 1, 32);
        Buffer.BlockCopy(p.Q.Y!, 0, pointBytes, 33, 32);

        var publicKey = Base64Url(pointBytes);
        var privateKey = Base64Url(p.D!);

        Console.WriteLine("VAPID keypair generated. Paste these into the api + worker environment:");
        Console.WriteLine();
        Console.WriteLine($"WebPush__Subject={subject}");
        Console.WriteLine($"WebPush__PublicKey={publicKey}");
        Console.WriteLine($"WebPush__PrivateKey={privateKey}");
        Console.WriteLine();
        Console.WriteLine("The public key is also the browser's applicationServerKey. Keep the private key secret.");
        Console.WriteLine("Mode stays DryRun until you also set WebPush__Mode=Live.");
        return 0;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
