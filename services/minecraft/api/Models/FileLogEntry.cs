namespace Nucleus.Minecraft.Models;

public record FileLogEntry(
    Guid Id,
    Guid UserId,
    string Operation,
    string FilePath,
    bool Success,
    string? Error,
    DateTimeOffset ExecutedAt,
    Guid? ServerId
);
