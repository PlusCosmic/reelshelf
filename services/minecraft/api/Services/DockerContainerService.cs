using Docker.DotNet;
using Docker.DotNet.Models;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

public class DockerContainerService
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerService> _logger;
    private readonly string _networkName;

    public DockerContainerService(IConfiguration configuration, ILogger<DockerContainerService> logger)
    {
        _logger = logger;
        _networkName = configuration["Docker:NetworkName"] ?? "nucleus_internal";

        string dockerHost = configuration["Docker:ProxyHost"] ?? "tcp://socket-proxy:2375";
        _client = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();

        _logger.LogInformation("DockerContainerService initialized with host: {Host}", dockerHost);
    }

    public async Task<ContainerState> GetContainerStateAsync(string containerName, CancellationToken ct = default)
    {
        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true }, ct);

            var container = containers.FirstOrDefault(c =>
                c.Names.Any(n => n.TrimStart('/') == containerName));

            if (container is null)
            {
                return new ContainerState(Exists: false, Status: "not_found", IsRunning: false);
            }

            bool isRunning = container.State?.Equals("running", StringComparison.OrdinalIgnoreCase) ?? false;

            return new ContainerState(
                Exists: true,
                Status: container.State ?? "unknown",
                IsRunning: isRunning,
                ContainerId: container.ID,
                StartedAt: container.Created != default ? new DateTimeOffset(container.Created) : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get container state for {ContainerName}", containerName);
            throw;
        }
    }

    public async Task StartContainerAsync(string containerName, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting container: {ContainerName}", containerName);

        try
        {
            await _client.Containers.StartContainerAsync(
                containerName,
                new ContainerStartParameters(),
                ct);

            _logger.LogInformation("Container started successfully: {ContainerName}", containerName);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            _logger.LogWarning("Container {ContainerName} is already running", containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start container: {ContainerName}", containerName);
            throw;
        }
    }

    public async Task StopContainerAsync(string containerName, int timeoutSeconds = 120, CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping container: {ContainerName} (timeout: {Timeout}s)", containerName, timeoutSeconds);

        try
        {
            await _client.Containers.StopContainerAsync(
                containerName,
                new ContainerStopParameters { WaitBeforeKillSeconds = (uint)timeoutSeconds },
                ct);

            _logger.LogInformation("Container stopped successfully: {ContainerName}", containerName);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            _logger.LogWarning("Container {ContainerName} is already stopped", containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop container: {ContainerName}", containerName);
            throw;
        }
    }

    #region Container Provisioning

    public async Task<string> CreateServerContainerAsync(
        MinecraftServer server,
        string rconPassword,
        string? curseForgeApiKey = null,
        CancellationToken ct = default)
    {
        string volumeName = $"mc-{server.ContainerName}-data";

        _logger.LogInformation("Creating container {ContainerName} with volume {VolumeName}",
            server.ContainerName, volumeName);

        // 1. Ensure the image exists
        await EnsureImageExistsAsync("itzg/minecraft-server:latest", ct);

        // 2. Create volume for persistent data
        try
        {
            await _client.Volumes.CreateAsync(new VolumesCreateParameters
            {
                Name = volumeName,
                Labels = new Dictionary<string, string>
                {
                    ["managed-by"] = "nucleus",
                    ["server-id"] = server.Id.ToString()
                }
            }, ct);
            _logger.LogInformation("Created volume: {VolumeName}", volumeName);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Volume {VolumeName} already exists, reusing", volumeName);
        }

        // 3. Build environment variables
        var env = BuildEnvironmentVariables(server, rconPassword, curseForgeApiKey);

        // 4. Create container
        var createParams = new CreateContainerParameters
        {
            Name = server.ContainerName,
            Image = "itzg/minecraft-server:latest",
            Env = env,
            Tty = true,
            OpenStdin = true,
            Labels = new Dictionary<string, string>
            {
                ["managed-by"] = "nucleus",
                ["server-id"] = server.Id.ToString(),
                ["server-type"] = server.ServerType.ToString().ToLowerInvariant()
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["25565/tcp"] = default,  // Game port
                ["25575/tcp"] = default,  // RCON
                ["9940/tcp"] = default    // Prometheus metrics
            },
            HostConfig = new HostConfig
            {
                Binds = [$"{volumeName}:/data"],
                NetworkMode = _networkName,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
                Memory = server.RamLimit * 1024L * 1024L,
                MemoryReservation = server.RamReservation * 1024L * 1024L,
                NanoCPUs = (long)(server.CpuLimit * 1_000_000_000),
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["25565/tcp"] = new List<PortBinding>
                    {
                        new() { HostPort = "25565" }
                    }
                }
            }
        };

        var response = await _client.Containers.CreateContainerAsync(createParams, ct);

        _logger.LogInformation("Created container {ContainerName} with ID {ContainerId}",
            server.ContainerName, response.ID);

        return response.ID;
    }

    public async Task DestroyContainerAsync(
        string containerName,
        bool removeVolume = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Destroying container: {ContainerName} (removeVolume: {RemoveVolume})",
            containerName, removeVolume);

        // 1. Stop container if running
        var state = await GetContainerStateAsync(containerName, ct);
        if (state.IsRunning)
        {
            _logger.LogInformation("Stopping container {ContainerName} before destruction", containerName);
            await StopContainerAsync(containerName, 60, ct);
        }

        // 2. Get container info to find associated volume
        string? volumeName = null;
        if (state.Exists)
        {
            try
            {
                var container = await _client.Containers.InspectContainerAsync(containerName, ct);
                volumeName = container.Mounts?
                    .FirstOrDefault(m => m.Destination == "/data")?.Name;
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Container {ContainerName} not found for inspection", containerName);
            }
        }

        // 3. Remove container
        if (state.Exists)
        {
            await _client.Containers.RemoveContainerAsync(containerName,
                new ContainerRemoveParameters { Force = true }, ct);
            _logger.LogInformation("Removed container: {ContainerName}", containerName);
        }

        // 4. Optionally remove volume (permanent data loss!)
        if (removeVolume && !string.IsNullOrEmpty(volumeName))
        {
            _logger.LogWarning("Removing volume {VolumeName} - this is permanent data loss!", volumeName);
            try
            {
                await _client.Volumes.RemoveAsync(volumeName, force: true, ct);
                _logger.LogInformation("Removed volume: {VolumeName}", volumeName);
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Volume {VolumeName} not found", volumeName);
            }
        }
    }

    public async Task EnsureImageExistsAsync(string image = "itzg/minecraft-server:latest", CancellationToken ct = default)
    {
        try
        {
            await _client.Images.InspectImageAsync(image, ct);
            _logger.LogDebug("Image {Image} already exists", image);
        }
        catch (DockerImageNotFoundException)
        {
            _logger.LogInformation("Pulling image: {Image}", image);

            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>(msg =>
                {
                    if (!string.IsNullOrEmpty(msg.Status))
                    {
                        _logger.LogDebug("Pull progress: {Status}", msg.Status);
                    }
                }),
                ct);

            _logger.LogInformation("Image pulled successfully: {Image}", image);
        }
    }

    private static List<string> BuildEnvironmentVariables(
        MinecraftServer server,
        string rconPassword,
        string? curseForgeApiKey)
    {
        var env = new List<string>
        {
            "EULA=true",
            "ENABLE_RCON=true",
            "RCON_PORT=25575",
            $"RCON_PASSWORD={rconPassword}",
            "ENABLE_QUERY=true",
            "QUERY_PORT=25565",
            "SERVER_PORT=25565",
            $"MEMORY={server.RamLimit - 512}M",  // Leave headroom for JVM overhead
            $"MAX_PLAYERS={server.MaxPlayers}",
            $"MOTD={server.Motd}",
            "TZ=Europe/London",
            "USE_AIKAR_FLAGS=true"
        };

        switch (server.ServerType)
        {
            case MinecraftServerType.Curseforge:
                env.Add("TYPE=AUTO_CURSEFORGE");
                if (!string.IsNullOrEmpty(server.CurseforgePageUrl))
                    env.Add($"CF_PAGE_URL={server.CurseforgePageUrl}");
                if (!string.IsNullOrEmpty(curseForgeApiKey))
                    env.Add($"CF_API_KEY={curseForgeApiKey}");
                break;

            case MinecraftServerType.Neoforge:
                env.Add("TYPE=NEOFORGE");
                env.Add($"VERSION={server.MinecraftVersion}");
                if (!string.IsNullOrEmpty(server.ModloaderVersion))
                    env.Add($"NEOFORGE_VERSION={server.ModloaderVersion}");
                break;

            case MinecraftServerType.Fabric:
                env.Add("TYPE=FABRIC");
                env.Add($"VERSION={server.MinecraftVersion}");
                if (!string.IsNullOrEmpty(server.ModloaderVersion))
                    env.Add($"FABRIC_LOADER_VERSION={server.ModloaderVersion}");
                break;

            case MinecraftServerType.Vanilla:
            default:
                env.Add("TYPE=VANILLA");
                env.Add($"VERSION={server.MinecraftVersion}");
                break;
        }

        return env;
    }

    #endregion

    public async Task<ContainerResourceStats?> GetContainerStatsAsync(string containerName, CancellationToken ct = default)
    {
        try
        {
            var state = await GetContainerStateAsync(containerName, ct);
            if (!state.IsRunning)
            {
                return null;
            }

            ContainerStatsResponse? stats = null;
            var progress = new Progress<ContainerStatsResponse>(s => stats = s);

            await _client.Containers.GetContainerStatsAsync(
                containerName,
                new ContainerStatsParameters { Stream = false, OneShot = true },
                progress,
                ct);

            if (stats is null)
            {
                return null;
            }

            return CalculateStats(stats);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get container stats for {ContainerName}", containerName);
            return null;
        }
    }

    private static ContainerResourceStats CalculateStats(ContainerStatsResponse stats)
    {
        // CPU calculation
        double cpuPercent = 0;
        if (stats.CPUStats?.CPUUsage?.TotalUsage > 0 &&
            stats.PreCPUStats?.CPUUsage?.TotalUsage > 0 &&
            stats.CPUStats?.SystemUsage > 0 &&
            stats.PreCPUStats?.SystemUsage > 0)
        {
            var cpuDelta = stats.CPUStats.CPUUsage.TotalUsage - stats.PreCPUStats.CPUUsage.TotalUsage;
            var systemDelta = stats.CPUStats.SystemUsage - stats.PreCPUStats.SystemUsage;

            if (systemDelta > 0 && cpuDelta > 0)
            {
                ulong onlineCpus = stats.CPUStats.OnlineCPUs > 0 ? stats.CPUStats.OnlineCPUs : 1;
                cpuPercent = (cpuDelta / (double)systemDelta) * onlineCpus * 100.0;
            }
        }

        // Memory calculation (exclude cache for accurate usage)
        ulong memoryUsed = stats.MemoryStats?.Usage ?? 0;
        ulong cache = 0;
        if (stats.MemoryStats?.Stats != null && stats.MemoryStats.Stats.TryGetValue("cache", out var cacheValue))
        {
            cache = cacheValue;
        }
        ulong memoryActual = memoryUsed - cache;
        ulong memoryLimit = stats.MemoryStats?.Limit ?? 1;

        double memoryPercent = memoryLimit > 0 ? (memoryActual / (double)memoryLimit) * 100.0 : 0;

        return new ContainerResourceStats(
            CpuPercent: Math.Round(cpuPercent, 2),
            MemoryUsedMb: (long)(memoryActual / 1024 / 1024),
            MemoryLimitMb: (long)(memoryLimit / 1024 / 1024),
            MemoryPercent: Math.Round(memoryPercent, 2)
        );
    }
}

public record ContainerState(
    bool Exists,
    string Status,
    bool IsRunning,
    string? ContainerId = null,
    DateTimeOffset? StartedAt = null
);

public record ContainerResourceStats(
    double CpuPercent,
    long MemoryUsedMb,
    long MemoryLimitMb,
    double MemoryPercent
);
