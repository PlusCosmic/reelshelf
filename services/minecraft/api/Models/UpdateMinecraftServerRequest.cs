namespace Nucleus.Minecraft.Models;

public record UpdateMinecraftServerRequest(
    string? Name = null,
    string? PersistenceLocation = null,
    string? ContainerName = null,
    decimal? CpuReservation = null,
    int? RamReservation = null,
    decimal? CpuLimit = null,
    int? RamLimit = null,
    MinecraftServerType? ServerType = null,
    string? MinecraftVersion = null,
    string? ModloaderVersion = null,
    string? CurseforgePageUrl = null,
    bool? IsActive = null
);
