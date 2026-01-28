namespace Nucleus.Minecraft.Models;

public record ServerStatus(
    bool IsOnline,
    int OnlinePlayers,
    int MaxPlayers,
    string? Motd,
    string? Version
);
