using Dapper;
using Npgsql;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Data;

public class MinecraftStatements(NpgsqlConnection connection)
{
    #region Server CRUD Operations

    public async Task<MinecraftServer> CreateServerAsync(Guid ownerId, CreateMinecraftServerRequest request)
    {
        const string sql = """
            INSERT INTO minecraft_server (
                owner_id, name, persistence_location, container_name,
                cpu_reservation, ram_reservation, cpu_limit, ram_limit,
                server_type, minecraft_version, modloader_version, curseforge_page_url
            )
            VALUES (
                @OwnerId, @Name, @PersistenceLocation, @ContainerName,
                @CpuReservation, @RamReservation, @CpuLimit, @RamLimit,
                @ServerType, @MinecraftVersion, @ModloaderVersion, @CurseforgePageUrl
            )
            RETURNING *
            """;

        return await connection.QuerySingleAsync<MinecraftServer>(sql, new
        {
            OwnerId = ownerId,
            request.Name,
            request.PersistenceLocation,
            request.ContainerName,
            request.CpuReservation,
            request.RamReservation,
            request.CpuLimit,
            request.RamLimit,
            request.ServerType,
            request.MinecraftVersion,
            request.ModloaderVersion,
            request.CurseforgePageUrl
        });
    }

    public async Task<MinecraftServer?> GetServerByIdAsync(Guid serverId)
    {
        const string sql = """
            SELECT * FROM minecraft_server WHERE id = @ServerId
            """;

        return await connection.QuerySingleOrDefaultAsync<MinecraftServer>(sql, new { ServerId = serverId });
    }

    public async Task<MinecraftServer?> GetServerByContainerNameAsync(string containerName)
    {
        const string sql = """
            SELECT * FROM minecraft_server WHERE container_name = @ContainerName
            """;

        return await connection.QuerySingleOrDefaultAsync<MinecraftServer>(sql, new { ContainerName = containerName });
    }

    public async Task<List<MinecraftServer>> GetServersByOwnerAsync(Guid ownerId)
    {
        const string sql = """
            SELECT * FROM minecraft_server
            WHERE owner_id = @OwnerId
            ORDER BY created_at DESC
            """;

        IEnumerable<MinecraftServer> results = await connection.QueryAsync<MinecraftServer>(sql, new { OwnerId = ownerId });
        return results.ToList();
    }

    public async Task<List<MinecraftServer>> GetAllActiveServersAsync()
    {
        const string sql = """
            SELECT * FROM minecraft_server
            WHERE is_active = TRUE
            ORDER BY name
            """;

        IEnumerable<MinecraftServer> results = await connection.QueryAsync<MinecraftServer>(sql);
        return results.ToList();
    }

    public async Task<List<MinecraftServer>> GetAllServersAsync()
    {
        const string sql = """
            SELECT * FROM minecraft_server ORDER BY created_at DESC
            """;

        IEnumerable<MinecraftServer> results = await connection.QueryAsync<MinecraftServer>(sql);
        return results.ToList();
    }

    public async Task<MinecraftServer?> UpdateServerAsync(Guid serverId, UpdateMinecraftServerRequest request)
    {
        const string sql = """
            UPDATE minecraft_server SET
                name = COALESCE(@Name, name),
                persistence_location = COALESCE(@PersistenceLocation, persistence_location),
                container_name = COALESCE(@ContainerName, container_name),
                cpu_reservation = COALESCE(@CpuReservation, cpu_reservation),
                ram_reservation = COALESCE(@RamReservation, ram_reservation),
                cpu_limit = COALESCE(@CpuLimit, cpu_limit),
                ram_limit = COALESCE(@RamLimit, ram_limit),
                server_type = COALESCE(@ServerType, server_type),
                minecraft_version = COALESCE(@MinecraftVersion, minecraft_version),
                modloader_version = COALESCE(@ModloaderVersion, modloader_version),
                curseforge_page_url = COALESCE(@CurseforgePageUrl, curseforge_page_url),
                is_active = COALESCE(@IsActive, is_active)
            WHERE id = @ServerId
            RETURNING *
            """;

        return await connection.QuerySingleOrDefaultAsync<MinecraftServer>(sql, new
        {
            ServerId = serverId,
            request.Name,
            request.PersistenceLocation,
            request.ContainerName,
            request.CpuReservation,
            request.RamReservation,
            request.CpuLimit,
            request.RamLimit,
            request.ServerType,
            request.MinecraftVersion,
            request.ModloaderVersion,
            request.CurseforgePageUrl,
            request.IsActive
        });
    }

    public async Task<bool> DeleteServerAsync(Guid serverId)
    {
        const string sql = """
            DELETE FROM minecraft_server WHERE id = @ServerId
            """;

        int rowsAffected = await connection.ExecuteAsync(sql, new { ServerId = serverId });
        return rowsAffected > 0;
    }

    public async Task<bool> ServerExistsAsync(Guid serverId)
    {
        const string sql = """
            SELECT EXISTS(SELECT 1 FROM minecraft_server WHERE id = @ServerId)
            """;

        return await connection.ExecuteScalarAsync<bool>(sql, new { ServerId = serverId });
    }

    public async Task<bool> ContainerNameExistsAsync(string containerName, Guid? excludeServerId = null)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM minecraft_server
                WHERE container_name = @ContainerName
                AND (@ExcludeServerId IS NULL OR id != @ExcludeServerId)
            )
            """;

        return await connection.ExecuteScalarAsync<bool>(sql, new { ContainerName = containerName, ExcludeServerId = excludeServerId });
    }

    public async Task UpdateRconPasswordAsync(Guid serverId, string rconPassword)
    {
        const string sql = """
            UPDATE minecraft_server
            SET rcon_password = @RconPassword
            WHERE id = @ServerId
            """;

        await connection.ExecuteAsync(sql, new { ServerId = serverId, RconPassword = rconPassword });
    }

    public async Task MarkServerInactiveAsync(Guid serverId)
    {
        const string sql = """
            UPDATE minecraft_server
            SET is_active = FALSE
            WHERE id = @ServerId
            """;

        await connection.ExecuteAsync(sql, new { ServerId = serverId });
    }

    public async Task UpdatePersistenceLocationAsync(Guid serverId, string persistenceLocation)
    {
        const string sql = """
            UPDATE minecraft_server
            SET persistence_location = @PersistenceLocation
            WHERE id = @ServerId
            """;

        await connection.ExecuteAsync(sql, new { ServerId = serverId, PersistenceLocation = persistenceLocation });
    }

    #endregion

    #region Logging Operations

    public async Task LogCommand(Guid userId, string command, string? response, bool success, string? error, Guid? serverId = null)
    {
        const string sql = """
            INSERT INTO minecraft_command_log (user_id, command, response, success, error, executed_at, server_id)
            VALUES (@UserId, @Command, @Response, @Success, @Error, @ExecutedAt, @ServerId)
            """;

        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            Command = command,
            Response = response,
            Success = success,
            Error = error,
            ExecutedAt = DateTimeOffset.UtcNow,
            ServerId = serverId
        });
    }

    public async Task LogFileOperation(Guid userId, string operation, string filePath, bool success, string? error, Guid? serverId = null)
    {
        const string sql = """
            INSERT INTO minecraft_file_log (user_id, operation, file_path, success, error, executed_at, server_id)
            VALUES (@UserId, @Operation, @FilePath, @Success, @Error, @ExecutedAt, @ServerId)
            """;

        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            Operation = operation,
            FilePath = filePath,
            Success = success,
            Error = error,
            ExecutedAt = DateTimeOffset.UtcNow,
            ServerId = serverId
        });
    }

    public async Task<List<CommandLogEntry>> GetRecentCommands(Guid userId, int limit = 50, Guid? serverId = null)
    {
        const string sql = """
            SELECT id, user_id, command, response, success, error, executed_at, server_id
            FROM minecraft_command_log
            WHERE user_id = @UserId
            AND (@ServerId IS NULL OR server_id = @ServerId)
            ORDER BY executed_at DESC
            LIMIT @Limit
            """;

        IEnumerable<CommandLogEntry> results = await connection.QueryAsync<CommandLogEntry>(sql, new
        {
            UserId = userId,
            Limit = limit,
            ServerId = serverId
        });

        return results.ToList();
    }

    public async Task<List<FileLogEntry>> GetRecentFileOperations(Guid userId, int limit = 50, Guid? serverId = null)
    {
        const string sql = """
            SELECT id, user_id, operation, file_path, success, error, executed_at, server_id
            FROM minecraft_file_log
            WHERE user_id = @UserId
            AND (@ServerId IS NULL OR server_id = @ServerId)
            ORDER BY executed_at DESC
            LIMIT @Limit
            """;

        IEnumerable<FileLogEntry> results = await connection.QueryAsync<FileLogEntry>(sql, new
        {
            UserId = userId,
            Limit = limit,
            ServerId = serverId
        });

        return results.ToList();
    }

    #endregion
}
