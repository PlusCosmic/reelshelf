using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using CoreRCON;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

public partial class RconService(ILogger<RconService> logger) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, ServerConnection> _connections = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    private sealed class ServerConnection : IDisposable
    {
        public RCON? Client { get; set; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public void Dispose()
        {
            Client?.Dispose();
            Lock.Dispose();
        }
    }

    private async Task<RCON> GetConnectedClientAsync(MinecraftServer server)
    {
        ServerConnection connection = _connections.GetOrAdd(server.Id, _ => new ServerConnection());

        await connection.Lock.WaitAsync();
        try
        {
            if (connection.Client != null && connection.Client.Connected)
            {
                return connection.Client;
            }

            (string host, int port, string password) = GetRconSettings(server);

            logger.LogInformation("Connecting to RCON for server {ServerId} at {Host}:{Port}", server.Id, host, port);

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
            IPAddress ip = addresses.First();

            connection.Client = new RCON(ip, (ushort)port, password);
            await connection.Client.ConnectAsync();
            logger.LogInformation("Successfully connected to RCON for server {ServerId}", server.Id);

            return connection.Client;
        }
        finally
        {
            connection.Lock.Release();
        }
    }

    public async Task<string> SendCommandAsync(MinecraftServer server, string command)
    {
        try
        {
            RCON client = await GetConnectedClientAsync(server);
            string response = await client.SendCommandAsync(command);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send RCON command to server {ServerId}: {Command}", server.Id, command);

            // Clear the failed connection
            if (_connections.TryGetValue(server.Id, out ServerConnection? connection))
            {
                connection.Client?.Dispose();
                connection.Client = null;
            }

            throw;
        }
    }

    public async Task<List<OnlinePlayer>> GetOnlinePlayersAsync(MinecraftServer server)
    {
        try
        {
            string response = await SendCommandAsync(server, "list");
            return ParsePlayerList(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get online players for server {ServerId}", server.Id);
            return new List<OnlinePlayer>();
        }
    }

    private static List<OnlinePlayer> ParsePlayerList(string listResponse)
    {
        List<OnlinePlayer> players = new();

        // Expected format: "There are X of a max of Y players online: player1, player2, player3"
        // Or: "There are 0 of a max of 20 players online:"
        Match match = PlayerListRegex().Match(listResponse);
        if (!match.Success)
        {
            return players;
        }

        string playersString = match.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(playersString))
        {
            return players;
        }

        string[] playerNames = playersString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string playerName in playerNames)
        {
            string trimmedName = playerName.Trim();
            if (!string.IsNullOrEmpty(trimmedName))
            {
                players.Add(new OnlinePlayer(trimmedName, Guid.Empty));
            }
        }

        return players;
    }

    [GeneratedRegex(@"online:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerListRegex();

    private static (string host, int port, string password) GetRconSettings(MinecraftServer server)
    {
        string host = server.ContainerName;
        int port = 25575;

        // Prefer database password (set during container provisioning)
        if (!string.IsNullOrEmpty(server.RconPassword))
        {
            return (host, port, server.RconPassword);
        }

        // Fall back to reading from server.properties (legacy/manual containers)
        string password = "";
        string serverPropertiesPath = Path.Combine(server.PersistenceLocation, "server.properties");
        if (File.Exists(serverPropertiesPath))
        {
            try
            {
                string[] lines = File.ReadAllLines(serverPropertiesPath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("rcon.password=", StringComparison.OrdinalIgnoreCase))
                    {
                        password = trimmed["rcon.password=".Length..];
                    }
                    else if (trimmed.StartsWith("rcon.port=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(trimmed["rcon.port=".Length..], out int parsedPort))
                        {
                            port = parsedPort;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors reading server.properties, use defaults
            }
        }

        return (host, port, password);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
        _connections.Clear();
        _connectionLock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
