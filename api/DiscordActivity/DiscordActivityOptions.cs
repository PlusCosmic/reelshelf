namespace Reelshelf.DiscordActivity;

public static class DiscordActivityOptions
{
    /// <summary>
    /// Bot token for the Discord application, used only to check who is in an Activity instance. The Activity
    /// is off until it is set. Environment variable: <c>DiscordActivity__BotToken</c>.
    /// </summary>
    public const string BotTokenKey = "DiscordActivity:BotToken";
}
