using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

public class MinecraftStatusService(RconService rconService, ILogger<MinecraftStatusService> logger)
{
    public async Task<ServerStatus> GetServerStatusAsync(MinecraftServer server)
    {
        try
        {
            // Try to get player list to determine if server is online
            string listResponse = await rconService.SendCommandAsync(server, "list");

            // Parse the response to extract player counts
            // Expected format: "There are X of a max of Y players online: ..."
            int onlinePlayers = 0;
            int maxPlayers = 20; // Default

            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(listResponse, @"There are (\d+) of a max of (\d+) players online");

            if (match.Success)
            {
                onlinePlayers = int.Parse(match.Groups[1].Value);
                maxPlayers = int.Parse(match.Groups[2].Value);
            }

            return new ServerStatus(
                IsOnline: true,
                OnlinePlayers: onlinePlayers,
                MaxPlayers: maxPlayers,
                Motd: null,
                Version: null
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get server status for {ServerId}, assuming offline", server.Id);
            return new ServerStatus(
                IsOnline: false,
                OnlinePlayers: 0,
                MaxPlayers: 0,
                Motd: null,
                Version: null
            );
        }
    }
}
