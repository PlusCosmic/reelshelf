using NpgsqlTypes;

namespace Nucleus.Minecraft.Models;

public enum MinecraftServerType
{
    [PgName("vanilla")]
    Vanilla,

    [PgName("curseforge")]
    Curseforge,

    [PgName("neoforge")]
    Neoforge,

    [PgName("fabric")]
    Fabric
}
