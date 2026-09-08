namespace Reelshelf.Twitch;

/// <summary>OAuth scopes the app asks Twitch for, and what each one is needed for.</summary>
public static class TwitchScopes
{
    /// <summary>Adds the account email to the Helix user payload at sign-in.</summary>
    public const string ReadEmail = "user:read:email";

    /// <summary>
    /// Lets the app request download URLs for the user's own clips (Helix "Get Clips Download" accepts
    /// channel:manage:clips or editor:manage:clips). Listing clips needs no scope.
    /// </summary>
    public const string ManageClips = "channel:manage:clips";

    public const string Default = ReadEmail + " " + ManageClips;
}
