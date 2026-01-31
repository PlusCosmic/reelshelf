namespace Nucleus.Minecraft.Models;

public record CreateMinecraftServerRequest(
    string Name,
    string PersistenceLocation,
    string ContainerName,
    decimal CpuReservation,
    int RamReservation,
    decimal CpuLimit,
    int RamLimit,
    MinecraftServerType ServerType,
    string MinecraftVersion,
    string? ModloaderVersion = null,
    string? CurseforgePageUrl = null
);
