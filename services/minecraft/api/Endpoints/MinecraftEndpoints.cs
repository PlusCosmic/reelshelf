using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Nucleus.Minecraft.Data;
using Nucleus.Minecraft.Models;
using Nucleus.Minecraft.Services;
using Nucleus.Shared.Auth;

namespace Nucleus.Minecraft.Endpoints;

public static class MinecraftEndpoints
{
    private static readonly Regex ContainerNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,62}$", RegexOptions.Compiled);

    public static void MapMinecraftEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("minecraft")
            .RequireAuthorization();

        // Server Management
        group.MapGet("servers", GetServers).WithName("GetMinecraftServers");
        group.MapPost("servers", CreateServer).WithName("CreateMinecraftServer")
            .RequirePermission(MinecraftPermissions.Console);
        group.MapGet("servers/{serverId:guid}", GetServer).WithName("GetMinecraftServer");
        group.MapPut("servers/{serverId:guid}", UpdateServer).WithName("UpdateMinecraftServer")
            .RequirePermission(MinecraftPermissions.Console);
        group.MapDelete("servers/{serverId:guid}", DeleteServer).WithName("DeleteMinecraftServer")
            .RequirePermission(MinecraftPermissions.Console);

        // Server-scoped endpoints
        RouteGroupBuilder serverGroup = group.MapGroup("servers/{serverId:guid}");

        // Status
        serverGroup.MapGet("status", GetStatus).WithName("GetMinecraftStatus");
        serverGroup.MapGet("players", GetPlayers).WithName("GetMinecraftPlayers");

        // Container Lifecycle
        serverGroup.MapGet("container", GetContainerState).WithName("GetContainerState");
        serverGroup.MapPost("container/start", StartContainer).WithName("StartContainer")
            .RequirePermission(MinecraftPermissions.Console);
        serverGroup.MapPost("container/stop", StopContainer).WithName("StopContainer")
            .RequirePermission(MinecraftPermissions.Console);
        serverGroup.MapPost("container/provision", ProvisionContainer).WithName("ProvisionContainer")
            .RequirePermission(MinecraftPermissions.Console);
        serverGroup.MapDelete("container", DestroyContainer).WithName("DestroyContainer")
            .RequirePermission(MinecraftPermissions.Console);

        // Console (REST)
        serverGroup.MapPost("console/command", SendCommand).WithName("SendMinecraftCommand")
            .RequirePermission(MinecraftPermissions.Console);
        serverGroup.MapGet("console/history", GetCommandHistory).WithName("GetCommandHistory")
            .RequirePermission(MinecraftPermissions.Console);

        // Console (WebSocket) - uses HttpContext directly for WebSocket upgrade
        serverGroup.MapGet("console/live", HandleConsoleWebSocket).WithName("ConsoleWebSocket")
            .RequirePermission(MinecraftPermissions.Console);

        // Files
        serverGroup.MapGet("files", ListFiles).WithName("ListMinecraftFiles")
            .RequirePermission(MinecraftPermissions.Files);
        serverGroup.MapGet("files/content", GetFileContent).WithName("GetMinecraftFileContent")
            .RequirePermission(MinecraftPermissions.Files);
        serverGroup.MapPut("files/content", SaveFileContent).WithName("SaveMinecraftFileContent")
            .RequirePermission(MinecraftPermissions.Files);
        serverGroup.MapDelete("files", DeleteFile).WithName("DeleteMinecraftFile")
            .RequirePermission(MinecraftPermissions.Files);
        serverGroup.MapPost("files/mkdir", CreateDirectory).WithName("CreateMinecraftDirectory")
            .RequirePermission(MinecraftPermissions.Files);

        // Backups
        serverGroup.MapGet("backups", GetBackupStatus).WithName("GetBackupStatus");
        serverGroup.MapPost("backups/sync", TriggerBackupSync).WithName("TriggerBackupSync")
            .RequirePermission(MinecraftPermissions.Console);
    }

    #region Server Management

    private static async Task<Ok<List<MinecraftServer>>> GetServers(
        MinecraftStatements statements,
        AuthenticatedUser user)
    {
        List<MinecraftServer> servers = await statements.GetServersByOwnerAsync(user.Id);
        return TypedResults.Ok(servers);
    }

    private static async Task<Results<Ok<MinecraftServer>, NotFound, ForbidHttpResult>> GetServer(
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await statements.GetServerByIdAsync(serverId);

        if (server is null)
            return TypedResults.NotFound();

        if (server.OwnerId != user.Id)
            return TypedResults.Forbid();

        return TypedResults.Ok(server);
    }

    private static async Task<Results<Created<MinecraftServer>, BadRequest<string>, Conflict<string>>> CreateServer(
        MinecraftStatements statements,
        AuthenticatedUser user,
        CreateMinecraftServerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return TypedResults.BadRequest("Server name cannot be empty");

        if (string.IsNullOrWhiteSpace(request.ContainerName))
            return TypedResults.BadRequest("Container name cannot be empty");

        string? validationError = ValidateServerRequest(
            request.ContainerName,
            request.CpuReservation,
            request.CpuLimit,
            request.RamReservation,
            request.RamLimit);
        if (validationError is not null)
            return TypedResults.BadRequest(validationError);

        if (await statements.ContainerNameExistsAsync(request.ContainerName))
            return TypedResults.Conflict($"Container name '{request.ContainerName}' is already in use");

        MinecraftServer server = await statements.CreateServerAsync(user.Id, request);
        return TypedResults.Created($"/minecraft/servers/{server.Id}", server);
    }

    private static async Task<Results<Ok<MinecraftServer>, NotFound, ForbidHttpResult, BadRequest<string>, Conflict<string>>> UpdateServer(
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        UpdateMinecraftServerRequest request)
    {
        MinecraftServer? existing = await statements.GetServerByIdAsync(serverId);

        if (existing is null)
            return TypedResults.NotFound();

        if (existing.OwnerId != user.Id)
            return TypedResults.Forbid();

        if (request.ContainerName is not null &&
            request.ContainerName != existing.ContainerName &&
            await statements.ContainerNameExistsAsync(request.ContainerName, serverId))
        {
            return TypedResults.Conflict($"Container name '{request.ContainerName}' is already in use");
        }

        string? validationError = ValidateServerRequest(
            request.ContainerName ?? existing.ContainerName,
            request.CpuReservation ?? existing.CpuReservation,
            request.CpuLimit ?? existing.CpuLimit,
            request.RamReservation ?? existing.RamReservation,
            request.RamLimit ?? existing.RamLimit);
        if (validationError is not null)
            return TypedResults.BadRequest(validationError);

        MinecraftServer? updated = await statements.UpdateServerAsync(serverId, request);
        return TypedResults.Ok(updated!);
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteServer(
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await statements.GetServerByIdAsync(serverId);

        if (server is null)
            return TypedResults.NotFound();

        if (server.OwnerId != user.Id)
            return TypedResults.Forbid();

        await statements.DeleteServerAsync(serverId);
        return TypedResults.NoContent();
    }

    #endregion

    #region Helpers

    private static async Task<MinecraftServer?> ValidateServerOwnership(
        MinecraftStatements statements,
        Guid serverId,
        Guid userId)
    {
        MinecraftServer? server = await statements.GetServerByIdAsync(serverId);
        if (server is null || server.OwnerId != userId)
            return null;
        return server;
    }

    private static string? ValidateServerRequest(
        string containerName,
        decimal cpuReservation,
        decimal cpuLimit,
        int ramReservation,
        int ramLimit)
    {
        if (!ContainerNamePattern.IsMatch(containerName) || containerName.Contains("..", StringComparison.Ordinal))
        {
            return "Container name contains unsupported characters";
        }

        if (ramReservation < 512 || ramLimit < ramReservation)
        {
            return "RAM limits are invalid";
        }

        if (cpuReservation < 0 || cpuLimit <= 0 || cpuLimit < cpuReservation)
        {
            return "CPU limits are invalid";
        }

        return null;
    }

    #endregion

    #region Container Lifecycle

    private static async Task<Results<Ok<ContainerStateResponse>, NotFound, ForbidHttpResult>> GetContainerState(
        DockerContainerService dockerService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        ContainerState state = await dockerService.GetContainerStateAsync(server.ContainerName);
        ContainerResourceStats? stats = state.IsRunning
            ? await dockerService.GetContainerStatsAsync(server.ContainerName)
            : null;

        return TypedResults.Ok(new ContainerStateResponse(
            state.Exists,
            state.Status,
            state.IsRunning,
            state.ContainerId,
            state.StartedAt,
            stats?.CpuPercent,
            stats?.MemoryUsedMb,
            stats?.MemoryLimitMb,
            stats?.MemoryPercent
        ));
    }

    private static async Task<Results<Ok<ContainerActionResponse>, NotFound, ForbidHttpResult, BadRequest<string>>> StartContainer(
        DockerContainerService dockerService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        ContainerState state = await dockerService.GetContainerStateAsync(server.ContainerName);

        if (!state.Exists)
            return TypedResults.BadRequest($"Container '{server.ContainerName}' does not exist");

        if (state.IsRunning)
            return TypedResults.BadRequest("Container is already running");

        await dockerService.StartContainerAsync(server.ContainerName);

        return TypedResults.Ok(new ContainerActionResponse(
            Success: true,
            Message: $"Container '{server.ContainerName}' started successfully",
            NewStatus: "running"
        ));
    }

    private static async Task<Results<Ok<ContainerActionResponse>, NotFound, ForbidHttpResult, BadRequest<string>>> StopContainer(
        DockerContainerService dockerService,
        RconService rconService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        int? timeout = 120,
        bool announce = true)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        ContainerState state = await dockerService.GetContainerStateAsync(server.ContainerName);

        if (!state.Exists)
            return TypedResults.BadRequest($"Container '{server.ContainerName}' does not exist");

        if (!state.IsRunning)
            return TypedResults.BadRequest("Container is not running");

        // Announce to players before stopping
        if (announce)
        {
            try
            {
                await rconService.SendCommandAsync(server, "say Server shutting down in 30 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
            catch
            {
                // RCON may be unavailable - proceed with stop anyway
            }
        }

        await dockerService.StopContainerAsync(server.ContainerName, timeout ?? 120);

        return TypedResults.Ok(new ContainerActionResponse(
            Success: true,
            Message: $"Container '{server.ContainerName}' stopped successfully",
            NewStatus: "exited"
        ));
    }

    private static async Task<Results<Ok<ProvisionResponse>, NotFound, ForbidHttpResult, BadRequest<string>, Conflict<string>>> ProvisionContainer(
        DockerContainerService dockerService,
        MinecraftStatements statements,
        IConfiguration configuration,
        AuthenticatedUser user,
        Guid serverId,
        CancellationToken ct)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        // Check if container already exists
        ContainerState existingState = await dockerService.GetContainerStateAsync(server.ContainerName, ct);
        if (existingState.Exists)
            return TypedResults.Conflict($"Container '{server.ContainerName}' already exists");

        // Generate RCON password
        string rconPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

        // Get CurseForge API key if needed
        string? curseForgeApiKey = server.ServerType == MinecraftServerType.Curseforge
            ? configuration["CurseforgeApiKey"]
            : null;

        // Create the container
        string containerId = await dockerService.CreateServerContainerAsync(server, rconPassword, curseForgeApiKey, ct);

        // Store the RCON password
        await statements.UpdateRconPasswordAsync(server.Id, rconPassword);

        // Update persistence location to match volume name
        string volumeName = $"mc-{server.ContainerName}-data";
        await statements.UpdatePersistenceLocationAsync(server.Id, volumeName);

        return TypedResults.Ok(new ProvisionResponse(
            Success: true,
            Message: $"Container '{server.ContainerName}' provisioned successfully",
            ContainerId: containerId,
            VolumeName: volumeName
        ));
    }

    private static async Task<Results<Ok<ContainerActionResponse>, NotFound, ForbidHttpResult, BadRequest<string>>> DestroyContainer(
        DockerContainerService dockerService,
        RconService rconService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        bool removeData = false,
        CancellationToken ct = default)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        ContainerState state = await dockerService.GetContainerStateAsync(server.ContainerName, ct);

        if (!state.Exists)
            return TypedResults.BadRequest($"Container '{server.ContainerName}' does not exist");

        // Safety check: verify no players are online if container is running
        if (state.IsRunning)
        {
            try
            {
                var players = await rconService.GetOnlinePlayersAsync(server);
                if (players.Count > 0)
                    return TypedResults.BadRequest($"Cannot destroy container with {players.Count} player(s) online. Stop the server first.");
            }
            catch
            {
                // RCON unavailable - proceed anyway
            }
        }

        // Destroy the container
        await dockerService.DestroyContainerAsync(server.ContainerName, removeData, ct);

        // If removing data, delete the server record; otherwise mark as inactive
        if (removeData)
        {
            await statements.DeleteServerAsync(server.Id);
        }
        else
        {
            await statements.MarkServerInactiveAsync(server.Id);
        }

        string message = removeData
            ? $"Container '{server.ContainerName}' and all data permanently destroyed"
            : $"Container '{server.ContainerName}' destroyed (data preserved)";

        return TypedResults.Ok(new ContainerActionResponse(
            Success: true,
            Message: message,
            NewStatus: "destroyed"
        ));
    }

    #endregion

    #region Console & Status

    private static async Task HandleConsoleWebSocket(
        HttpContext context,
        ConsoleWebSocketHandler handler,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        CancellationToken ct)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection required", ct);
            return;
        }

        WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleAsync(webSocket, context.User, server, ct);
    }

    private static async Task<Results<Ok<ServerStatus>, NotFound, ForbidHttpResult>> GetStatus(
        MinecraftStatusService statusService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        ServerStatus status = await statusService.GetServerStatusAsync(server);
        return TypedResults.Ok(status);
    }

    private static async Task<Results<Ok<List<OnlinePlayer>>, NotFound, ForbidHttpResult>> GetPlayers(
        RconService rconService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        List<OnlinePlayer> players = await rconService.GetOnlinePlayersAsync(server);
        return TypedResults.Ok(players);
    }

    private static async Task<Results<Ok<RconResponse>, NotFound, ForbidHttpResult, BadRequest<string>>> SendCommand(
        RconService rconService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        RconCommand request)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(request.Command))
            return TypedResults.BadRequest("Command cannot be empty");

        try
        {
            string response = await rconService.SendCommandAsync(server, request.Command);
            await statements.LogCommand(user.Id, request.Command, response, true, null, serverId);
            return TypedResults.Ok(new RconResponse(true, response, null));
        }
        catch (Exception ex)
        {
            await statements.LogCommand(user.Id, request.Command, null, false, ex.Message, serverId);
            return TypedResults.Ok(new RconResponse(false, null, ex.Message));
        }
    }

    private static async Task<Results<Ok<List<CommandLogEntry>>, NotFound, ForbidHttpResult>> GetCommandHistory(
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        int limit = 50)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        List<CommandLogEntry> history = await statements.GetRecentCommands(user.Id, Math.Min(limit, 100), serverId);
        return TypedResults.Ok(history);
    }

    #endregion

    #region File Operations

    private static async Task<Results<Ok<DirectoryListing>, NotFound, ForbidHttpResult, BadRequest<string>>> ListFiles(
        FileService fileService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        string path = "")
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        try
        {
            DirectoryListing listing = fileService.ListDirectory(server, path);
            await statements.LogFileOperation(user.Id, "list", path, true, null, serverId);
            return TypedResults.Ok(listing);
        }
        catch (DirectoryNotFoundException)
        {
            await statements.LogFileOperation(user.Id, "list", path, false, "Directory not found", serverId);
            return TypedResults.BadRequest($"Directory not found: {path}");
        }
        catch (System.Security.SecurityException ex)
        {
            await statements.LogFileOperation(user.Id, "list", path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<string>, NotFound, ForbidHttpResult, BadRequest<string>>> GetFileContent(
        FileService fileService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        string path)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(path))
            return TypedResults.BadRequest("Path cannot be empty");

        try
        {
            string content = await fileService.ReadFileAsync(server, path);
            await statements.LogFileOperation(user.Id, "read", path, true, null, serverId);
            return TypedResults.Ok(content);
        }
        catch (FileNotFoundException)
        {
            await statements.LogFileOperation(user.Id, "read", path, false, "File not found", serverId);
            return TypedResults.BadRequest($"File not found: {path}");
        }
        catch (System.Security.SecurityException ex)
        {
            await statements.LogFileOperation(user.Id, "read", path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await statements.LogFileOperation(user.Id, "read", path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok, NotFound, ForbidHttpResult, BadRequest<string>>> SaveFileContent(
        FileService fileService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        SaveFileRequest request)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(request.Path))
            return TypedResults.BadRequest("Path cannot be empty");

        try
        {
            await fileService.WriteFileAsync(server, request.Path, request.Content ?? "");
            await statements.LogFileOperation(user.Id, "write", request.Path, true, null, serverId);
            return TypedResults.Ok();
        }
        catch (System.Security.SecurityException ex)
        {
            await statements.LogFileOperation(user.Id, "write", request.Path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await statements.LogFileOperation(user.Id, "write", request.Path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok, NotFound, ForbidHttpResult, BadRequest<string>>> DeleteFile(
        FileService fileService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        string path)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(path))
            return TypedResults.BadRequest("Path cannot be empty");

        try
        {
            fileService.DeleteFile(server, path);
            await statements.LogFileOperation(user.Id, "delete", path, true, null, serverId);
            return TypedResults.Ok();
        }
        catch (FileNotFoundException)
        {
            await statements.LogFileOperation(user.Id, "delete", path, false, "File not found", serverId);
            return TypedResults.BadRequest($"File not found: {path}");
        }
        catch (System.Security.SecurityException ex)
        {
            await statements.LogFileOperation(user.Id, "delete", path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok, NotFound, ForbidHttpResult, BadRequest<string>>> CreateDirectory(
        FileService fileService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId,
        CreateDirectoryRequest request)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(request.Path))
            return TypedResults.BadRequest("Path cannot be empty");

        try
        {
            fileService.CreateDirectory(server, request.Path);
            await statements.LogFileOperation(user.Id, "mkdir", request.Path, true, null, serverId);
            return TypedResults.Ok();
        }
        catch (System.Security.SecurityException ex)
        {
            await statements.LogFileOperation(user.Id, "mkdir", request.Path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await statements.LogFileOperation(user.Id, "mkdir", request.Path, false, ex.Message, serverId);
            return TypedResults.BadRequest(ex.Message);
        }
    }

    #endregion

    #region Backups

    private static async Task<Results<Ok<BackupListResult>, NotFound, ForbidHttpResult>> GetBackupStatus(
        BackupService backupService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        BackupListResult status = await backupService.GetBackupStatusAsync(server);
        return TypedResults.Ok(status);
    }

    private static async Task<Results<Ok<BackupSyncResult>, NotFound, ForbidHttpResult>> TriggerBackupSync(
        BackupService backupService,
        MinecraftStatements statements,
        AuthenticatedUser user,
        Guid serverId)
    {
        MinecraftServer? server = await ValidateServerOwnership(statements, serverId, user.Id);
        if (server is null)
            return TypedResults.NotFound();

        BackupSyncResult result = await backupService.SyncBackupsAsync(server);
        return TypedResults.Ok(result);
    }

    #endregion

    public sealed record SaveFileRequest(string Path, string? Content);
    public sealed record CreateDirectoryRequest(string Path);

    // Container lifecycle response types
    public sealed record ContainerStateResponse(
        bool Exists,
        string Status,
        bool IsRunning,
        string? ContainerId,
        DateTimeOffset? StartedAt,
        double? CpuPercent,
        long? MemoryUsedMb,
        long? MemoryLimitMb,
        double? MemoryPercent
    );

    public sealed record ContainerActionResponse(
        bool Success,
        string Message,
        string NewStatus
    );

    public sealed record ProvisionResponse(
        bool Success,
        string Message,
        string ContainerId,
        string VolumeName
    );
}
