namespace Nucleus.Minecraft.Models;

public record CommandLogEntry(
    Guid Id,
    Guid UserId,
    string Command,
    string? Response,
    bool Success,
    string? Error,
    DateTimeOffset ExecutedAt,
    Guid? ServerId
);
