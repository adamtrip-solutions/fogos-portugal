namespace Fogos.Domain.Users;

/// <summary>A human account's authorization role. Ownership/role is the local authority, not Clerk.</summary>
public enum UserRole
{
    User,
    Admin,
}

/// <summary>
/// A local user record keyed by Clerk's stable user id. Lazy-provisioned on the first authenticated
/// request (see <c>UserProvisioningService</c>); this collection — not the Clerk token — is the
/// authority for <see cref="Role"/> and for ownership of API keys and subscriptions.
/// </summary>
public sealed class User
{
    public string Id { get; set; } = "";

    public required string ClerkUserId { get; set; }

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
