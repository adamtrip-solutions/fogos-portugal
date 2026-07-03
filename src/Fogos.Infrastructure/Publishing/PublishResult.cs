namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Outcome of a publish attempt. Publishers never throw — failures come back here as
/// <c>Success = false</c> with an <see cref="Error"/>, having already logged and pinged ops.
/// </summary>
public sealed record PublishResult
{
    public required bool Success { get; init; }

    /// <summary>Provider id of the created object (tweet/post/message), or a <c>dryrun-*</c> id in dry-run.</summary>
    public string? ExternalId { get; init; }

    public string? Error { get; init; }

    public static PublishResult Ok(string? externalId) => new() { Success = true, ExternalId = externalId };

    public static PublishResult Fail(string error) => new() { Success = false, Error = error };

    /// <summary>Channel is <c>Off</c>: nothing sent, nothing captured, treated as a benign success.</summary>
    public static readonly PublishResult Skipped = new() { Success = true, ExternalId = null };

    /// <summary>A fake but well-formed id so dry-run threading logic stays exercisable.</summary>
    public static string DryRunId() => $"dryrun-{Guid.NewGuid():N}";
}
