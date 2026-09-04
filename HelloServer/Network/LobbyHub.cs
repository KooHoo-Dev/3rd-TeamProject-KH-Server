using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HelloServer;

/// <summary>게임 시작 전 2~4인 대기방의 WebSocket 상태와 시작 신호를 담당한다.</summary>
public sealed class LobbyHub
{
    public const int MinimumPlayers = 2;
    public const int MaximumPlayers = 4;
    private const int CodeLength = 6;

    private sealed class Lobby
    {
        public string HostToken;
        public string HostClientId;
        public bool IsStarted;
        public DateTime LastTouchedUtc;
        public Dictionary<string, string> Players { get; } = new();
        public Dictionary<string, WebSocket> Members { get; } = new();
    }

    private readonly Dictionary<string, Lobby> lobbies = new();
    private readonly object gate = new();

    public bool IsStarted(string rawCode)
    {
        string code = Normalize(rawCode);
        if (code == null) return false;
        lock (gate) return lobbies.TryGetValue(code, out Lobby lobby) && lobby.IsStarted;
    }

    public async Task HandleAsync(WebSocket socket, CancellationToken token)
    {
        string code = null;
        string clientId = null;
        try
        {
            (code, clientId) = await AcceptFirstMessageAsync(socket, token);
            if (code == null) return;
            await BroadcastStateAsync(code, token);

            while (socket.State == WebSocketState.Open && token.IsCancellationRequested == false)
            {
                string json = await ReceiveTextAsync(socket, token);
                LobbyMessageHeader header = JsonSerializer.Deserialize<LobbyMessageHeader>(json);
                if (header?.Type != "lobby.start")
                {
                    await SendAsync(socket, new LobbyErrorMessage { Code = "lobby.unsupported_message" }, token);
                    continue;
                }

                LobbyStartRequest request = JsonSerializer.Deserialize<LobbyStartRequest>(json);
                if (TryStart(code, request?.HostToken, out string error) == false)
                {
                    await SendAsync(socket, new LobbyErrorMessage { Code = error }, token);
                    continue;
                }
                await BroadcastStartedAsync(code, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (JsonException)
        {
            if (socket.State == WebSocketState.Open)
                await SendAsync(socket, new LobbyErrorMessage { Code = "lobby.invalid_message" }, token);
        }
        finally
        {
            if (code != null && clientId != null)
            {
                Detach(code, clientId, socket);
                await BroadcastStateAsync(code, CancellationToken.None);
            }
        }
    }

    private async Task<(string Code, string ClientId)> AcceptFirstMessageAsync(WebSocket socket, CancellationToken token)
    {
        string json = await ReceiveTextAsync(socket, token);
        LobbyMessageHeader header = JsonSerializer.Deserialize<LobbyMessageHeader>(json);
        string code;
        string clientId;
        string error;

        if (header?.Type == "lobby.create")
        {
            LobbyCreateMessage request = JsonSerializer.Deserialize<LobbyCreateMessage>(json);
            if (TryCreate(request?.ClientID, request?.NickName, out code, out clientId, out error) == false)
            {
                await SendAsync(socket, new LobbyErrorMessage { Code = error }, token);
                return (null, null);
            }
        }
        else if (header?.Type == "lobby.join")
        {
            LobbyJoinMessage request = JsonSerializer.Deserialize<LobbyJoinMessage>(json);
            if (TryJoin(request?.RoomCode, request?.ClientID, request?.NickName, out code, out clientId, out error) == false)
            {
                await SendAsync(socket, new LobbyErrorMessage { Code = error }, token);
                return (null, null);
            }
        }
        else
        {
            await SendAsync(socket, new LobbyErrorMessage { Code = "lobby.first_message_required" }, token);
            return (null, null);
        }

        if (Attach(code, clientId, socket) == false)
        {
            await SendAsync(socket, new LobbyErrorMessage { Code = "room.not_found" }, token);
            return (null, null);
        }
        return (code, clientId);
    }

    private bool TryCreate(string rawClientId, string rawNickName, out string code, out string clientId, out string error)
    {
        code = null;
        if (TryNormalizePlayer(rawClientId, rawNickName, out clientId, out string nickName, out error) == false) return false;
        lock (gate)
        {
            SweepExpiredUnsafe();
            do { code = Random.Shared.Next(0, 1_000_000).ToString($"D{CodeLength}"); }
            while (lobbies.ContainsKey(code));
            Lobby lobby = new()
            {
                HostClientId = clientId,
                HostToken = Guid.NewGuid().ToString("N"),
                LastTouchedUtc = DateTime.UtcNow,
            };
            lobby.Players.Add(clientId, nickName);
            lobbies.Add(code, lobby);
            Console.WriteLine($"[{code}] 대기방 생성");
            return true;
        }
    }

    private bool TryJoin(string rawCode, string rawClientId, string rawNickName, out string code, out string clientId, out string error)
    {
        code = Normalize(rawCode);
        clientId = null;
        if (code == null) { error = "room.invalid_code"; return false; }
        if (TryNormalizePlayer(rawClientId, rawNickName, out clientId, out string nickName, out error) == false) return false;
        lock (gate)
        {
            SweepExpiredUnsafe();
            if (lobbies.TryGetValue(code, out Lobby lobby) == false) { error = "room.not_found"; return false; }
            if (lobby.IsStarted) { error = "room.already_started"; return false; }
            if (lobby.Players.ContainsKey(clientId) == false && lobby.Players.Count >= MaximumPlayers) { error = "room.full"; return false; }
            lobby.Players[clientId] = nickName;
            lobby.LastTouchedUtc = DateTime.UtcNow;
            return true;
        }
    }

    private bool Attach(string code, string clientId, WebSocket socket)
    {
        lock (gate)
        {
            if (lobbies.TryGetValue(code, out Lobby lobby) == false || lobby.IsStarted || lobby.Players.ContainsKey(clientId) == false)
                return false;
            lobby.Members[clientId] = socket;
            lobby.LastTouchedUtc = DateTime.UtcNow;
            return true;
        }
    }

    private bool TryStart(string code, string hostToken, out string error)
    {
        error = null;
        lock (gate)
        {
            if (lobbies.TryGetValue(code, out Lobby lobby) == false) { error = "room.not_found"; return false; }
            if (lobby.HostToken != hostToken) { error = "room.host_only"; return false; }
            if (lobby.Players.Count < MinimumPlayers) { error = "room.not_enough_players"; return false; }
            lobby.IsStarted = true;
            lobby.LastTouchedUtc = DateTime.UtcNow;
            Console.WriteLine($"[{code}] 대기방 시작 ({lobby.Players.Count}명)");
            return true;
        }
    }

    private void Detach(string code, string clientId, WebSocket socket)
    {
        lock (gate)
        {
            if (lobbies.TryGetValue(code, out Lobby lobby) == false) return;
            if (lobby.Members.TryGetValue(clientId, out WebSocket current) && current == socket) lobby.Members.Remove(clientId);
            if (lobby.IsStarted == false)
            {
                lobby.Players.Remove(clientId);
                if (lobby.Players.Count == 0) lobbies.Remove(code);
            }
            lobby.LastTouchedUtc = DateTime.UtcNow;
        }
    }

    private async Task BroadcastStateAsync(string code, CancellationToken token)
    {
        List<(WebSocket Socket, LobbyStateMessage Message)> recipients = new();
        lock (gate)
        {
            if (lobbies.TryGetValue(code, out Lobby lobby) == false) return;
            LobbyRoomInfo info = ToInfo(code, lobby);
            foreach ((string id, WebSocket socket) in lobby.Members)
                recipients.Add((socket, new LobbyStateMessage { Room = info, HostToken = id == lobby.HostClientId ? lobby.HostToken : null }));
        }
        foreach ((WebSocket socket, LobbyStateMessage message) in recipients) await SendAsync(socket, message, token);
    }

    private async Task BroadcastStartedAsync(string code, CancellationToken token)
    {
        List<WebSocket> recipients;
        lock (gate)
        {
            if (lobbies.TryGetValue(code, out Lobby lobby) == false) return;
            recipients = lobby.Members.Values.ToList();
        }
        LobbyStartedMessage message = new() { RoomCode = code };
        foreach (WebSocket socket in recipients) await SendAsync(socket, message, token);
    }

    private static LobbyRoomInfo ToInfo(string code, Lobby lobby) => new()
    {
        RoomCode = code, HostClientID = lobby.HostClientId, IsStarted = lobby.IsStarted,
        MaxPlayers = MaximumPlayers, Players = lobby.Players.Values.OrderBy(name => name, StringComparer.Ordinal).ToList(),
    };

    private static bool TryNormalizePlayer(string clientId, string nickName, out string normalizedClientId, out string normalizedNickName, out string error)
    {
        normalizedClientId = clientId?.Trim(); normalizedNickName = nickName?.Trim(); error = null;
        if (string.IsNullOrWhiteSpace(normalizedClientId)) { error = "player.invalid_client"; return false; }
        if (string.IsNullOrWhiteSpace(normalizedNickName) || normalizedNickName.Length > 16) { error = "player.invalid_nickname"; return false; }
        return true;
    }

    private void SweepExpiredUnsafe()
    {
        DateTime now = DateTime.UtcNow;
        foreach (string code in lobbies.Where(pair => now - pair.Value.LastTouchedUtc > TimeSpan.FromMinutes(30)).Select(pair => pair.Key).ToArray())
            lobbies.Remove(code);
    }

    public static string Normalize(string raw)
    {
        string code = raw?.Trim();
        return code?.Length == CodeLength && code.All(char.IsDigit) ? code : null;
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[4096]; StringBuilder text = new();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Lobby connection closed.");
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) return text.ToString();
        }
    }

    private static Task SendAsync(WebSocket socket, object message, CancellationToken token)
    {
        if (socket.State != WebSocketState.Open) return Task.CompletedTask;
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }
}
