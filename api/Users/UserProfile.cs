namespace Reelshelf.Users;

/// <summary>Public shape of an account as returned by the API.</summary>
public record UserProfile(Guid Id, string Username, string? GlobalName, string? Avatar);
