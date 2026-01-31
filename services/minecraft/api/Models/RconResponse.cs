namespace Nucleus.Minecraft.Models;

public record RconResponse(
    bool Success,
    string? Response,
    string? Error
);
