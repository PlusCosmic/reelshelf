using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Nucleus.Minecraft.Data;
using Nucleus.Minecraft.Models;
using Nucleus.Shared.Discord;

namespace Nucleus.Minecraft.Services;

/// <summary>
/// Handles WebSocket connections for the Minecraft console.
/// Provides bidirectional communication for:
/// - Sending RCON commands to the server
/// - Receiving real-time server log output
/// </summary>
public class ConsoleWebSocketHandler(
    RconService rconService,
    LogTailerService logTailerService,
    MinecraftStatements statements,
    DiscordStatements discordStatements,
    ILogger<ConsoleWebSocketHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task HandleAsync(WebSocket webSocket, ClaimsPrincipal user, MinecraftServer server, CancellationToken ct)
    {
        string? discordId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (discordId == null)
        {
            await CloseWithErrorAsync(webSocket, "Unauthorized", ct);
            return;
        }

        DiscordStatements.DiscordUserRow? discordUser = await discordStatements.GetUserByDiscordId(discordId);
        if (discordUser == null)
        {
            await CloseWithErrorAsync(webSocket, "User not found", ct);
            return;
        }

        Guid userId = discordUser.Id;
        logger.LogInformation("WebSocket console connected for user {UserId} to server {ServerId}", userId, server.Id);

        // Create a channel for log entries
        Channel<LogTailerService.LogEntry> logChannel = Channel.CreateBounded<LogTailerService.LogEntry>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

        try
        {
            // Send recent log history first
            List<LogTailerService.LogEntry> recentLogs = await logTailerService.GetRecentLinesAsync(server, 50);
            foreach (var entry in recentLogs)
            {
                await SendLogEntryAsync(webSocket, entry, ct);
            }

            // Send connected message
            await SendMessageAsync(webSocket, new WsMessage
            {
                Type = "connected",
                Message = $"Connected to Minecraft console: {server.Name}"
            }, ct);

            // Start concurrent tasks for reading commands, streaming logs, and tailing
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            Task readTask = ReadCommandsAsync(webSocket, userId, server, linkedCts.Token);
            Task writeTask = StreamLogsAsync(webSocket, logChannel, linkedCts.Token);
            Task tailTask = logTailerService.StreamLogsAsync(server, logChannel, linkedCts.Token);

            // Wait for any task to complete (connection closed or error)
            await Task.WhenAny(readTask, writeTask, tailTask);

            // Cancel the other tasks
            await linkedCts.CancelAsync();
        }
        catch (OperationCanceledException)
        {
            // Normal disconnection
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "WebSocket error for user {UserId} on server {ServerId}", userId, server.Id);
        }
        finally
        {
            logChannel.Writer.TryComplete();
            logger.LogInformation("WebSocket console disconnected for user {UserId} from server {ServerId}", userId, server.Id);

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", CancellationToken.None);
            }
        }
    }

    private async Task ReadCommandsAsync(WebSocket webSocket, Guid userId, MinecraftServer server, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];

        while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            using MemoryStream ms = new();

            do
            {
                result = await webSocket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string json = Encoding.UTF8.GetString(ms.ToArray());
                await ProcessCommandAsync(webSocket, json, userId, server, ct);
            }
        }
    }

    private async Task ProcessCommandAsync(WebSocket webSocket, string json, Guid userId, MinecraftServer server, CancellationToken ct)
    {
        try
        {
            WsMessage? message = JsonSerializer.Deserialize<WsMessage>(json, JsonOptions);
            if (message?.Type != "command" || string.IsNullOrWhiteSpace(message.Command))
            {
                return;
            }

            string command = message.Command;
            logger.LogInformation("User {UserId} executing command on server {ServerId}: {Command}", userId, server.Id, command);

            try
            {
                string response = await rconService.SendCommandAsync(server, command);
                await statements.LogCommand(userId, command, response, true, null, server.Id);

                await SendMessageAsync(webSocket, new WsMessage
                {
                    Type = "response",
                    Command = command,
                    Response = response,
                    Success = true,
                    Timestamp = DateTimeOffset.UtcNow
                }, ct);
            }
            catch (Exception ex)
            {
                await statements.LogCommand(userId, command, null, false, ex.Message, server.Id);

                await SendMessageAsync(webSocket, new WsMessage
                {
                    Type = "response",
                    Command = command,
                    Response = null,
                    Error = ex.Message,
                    Success = false,
                    Timestamp = DateTimeOffset.UtcNow
                }, ct);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON received from client");
        }
    }

    private static async Task StreamLogsAsync(WebSocket webSocket, Channel<LogTailerService.LogEntry> logChannel, CancellationToken ct)
    {
        await foreach (var entry in logChannel.Reader.ReadAllAsync(ct))
        {
            if (webSocket.State != WebSocketState.Open)
                break;

            await SendLogEntryAsync(webSocket, entry, ct);
        }
    }

    private static async Task SendLogEntryAsync(WebSocket webSocket, LogTailerService.LogEntry entry, CancellationToken ct)
    {
        await SendMessageAsync(webSocket, new WsMessage
        {
            Type = "log",
            Text = entry.Text,
            Level = entry.Level.ToString().ToLowerInvariant(),
            Timestamp = entry.Timestamp
        }, ct);
    }

    private static async Task SendMessageAsync(WebSocket webSocket, WsMessage message, CancellationToken ct)
    {
        if (webSocket.State != WebSocketState.Open)
            return;

        string json = JsonSerializer.Serialize(message, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static async Task CloseWithErrorAsync(WebSocket webSocket, string error, CancellationToken ct)
    {
        await SendMessageAsync(webSocket, new WsMessage
        {
            Type = "error",
            Error = error
        }, ct);
        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, error, ct);
    }

    public class WsMessage
    {
        public string Type { get; set; } = "";
        public string? Command { get; set; }
        public string? Response { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? Text { get; set; }
        public string? Level { get; set; }
        public bool? Success { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }
}
