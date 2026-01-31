namespace Nucleus.Minecraft.Models;

public record MinecraftServer
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid OwnerId { get; init; }
    public string PersistenceLocation { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public decimal CpuReservation { get; init; }
    public int RamReservation { get; init; }
    public decimal CpuLimit { get; init; }
    public int RamLimit { get; init; }
    public MinecraftServerType ServerType { get; init; }
    public string MinecraftVersion { get; init; } = string.Empty;
    public string? ModloaderVersion { get; init; }
    public string? CurseforgePageUrl { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    // Container provisioning fields (V13 migration)
    public string? RconPassword { get; init; }
    public int MaxPlayers { get; init; } = 20;
    public string Motd { get; init; } = "A Minecraft Server";
}
