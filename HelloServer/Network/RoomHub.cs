using System.Net.WebSockets;

namespace HelloServer;

// 여러개의 방들을 관리하는 클래스.
// 코드로 방을 찾아주고, 빈 방을 치운다, 

public class RoomHub
{
    public const int MinimumLobbyPlayers = 2;
    public const int MaximumLobbyPlayers = 4;
    private const int LobbyCodeLength = 6;

    // 방 하나와 그 방에 들어가겠다고 한 사람의 수
    // 방을 만들기 전에(방을 찾은 뒤) 실제로 방이 생성 될때까지는
    // 시간이 조금 걸립니다. 그 사이에 어떤 유저가 나가게되면
    // 바로 비어버리게 되니까 별도의 클래스를 만들어서 룸자체를 관리해줍니다. 
    // (룸을 한번 감싸는 거임)
    private class Entry
    {
        public Room Room;
        public int Users;
    }

    private readonly Dictionary<string, Entry> rooms = new();

    private sealed class LobbyEntry
    {
        public string HostToken;
        public bool IsStarted;
        public DateTime LastTouchedUtc;
        public Dictionary<string, string> Players { get; } = new();
    }

    // WebSocket 게임 Room과 분리된, 시작 전 로비 상태입니다.
    private readonly Dictionary<string, LobbyEntry> lobbies = new();

    private readonly int broadcastPerSecond;
    private readonly int logMovesPerSecond;

    private readonly object gate = new object();

    private int lastId;

    public RoomHub(int broadcastPerSecond, int logMovesPerSecond)
    {
        this.broadcastPerSecond = broadcastPerSecond;
        this.logMovesPerSecond = logMovesPerSecond;
    }

    #region 방 관리 함수들(찾기, 지우기)

    public bool TryCreateLobby(string clientId, string nickName,
        out LobbyCreateResponse response, out string error)
    {
        response = null;
        error = null;
        if (TryNormalizeLobbyPlayer(clientId, nickName, out clientId, out nickName, out error) == false)
            return false;

        lock (gate)
        {
            SweepExpiredLobbiesUnsafe();
            string code;
            do { code = CreateLobbyCodeUnsafe(); }
            while (lobbies.ContainsKey(code) || rooms.ContainsKey(code));

            LobbyEntry entry = new()
            {
                HostToken = Guid.NewGuid().ToString("N"),
                LastTouchedUtc = DateTime.UtcNow,
            };
            entry.Players.Add(clientId, nickName);
            lobbies.Add(code, entry);
            response = new LobbyCreateResponse
            {
                Room = CreateLobbyInfoUnsafe(code, entry),
                HostToken = entry.HostToken,
            };
            Console.WriteLine($"[{code}] 대기방 생성");
            return true;
        }
    }

    public bool TryJoinLobby(string rawCode, string clientId, string nickName,
        out LobbyRoomInfo room, out string error)
    {
        room = null;
        error = null;
        string code = Normalize(rawCode);
        if (string.IsNullOrEmpty(code)) { error = "room.invalid_code"; return false; }
        if (TryNormalizeLobbyPlayer(clientId, nickName, out clientId, out nickName, out error) == false)
            return false;

        lock (gate)
        {
            SweepExpiredLobbiesUnsafe();
            if (lobbies.TryGetValue(code, out LobbyEntry entry) == false)
            {
                error = "room.not_found";
                return false;
            }
            if (entry.IsStarted) { error = "room.already_started"; return false; }
            if (entry.Players.ContainsKey(clientId) == false && entry.Players.Count >= MaximumLobbyPlayers)
            {
                error = "room.full";
                return false;
            }

            entry.Players[clientId] = nickName;
            entry.LastTouchedUtc = DateTime.UtcNow;
            room = CreateLobbyInfoUnsafe(code, entry);
            return true;
        }
    }

    public bool TryGetLobby(string rawCode, out LobbyRoomInfo room)
    {
        room = null;
        string code = Normalize(rawCode);
        if (string.IsNullOrEmpty(code)) return false;
        lock (gate)
        {
            SweepExpiredLobbiesUnsafe();
            if (lobbies.TryGetValue(code, out LobbyEntry entry) == false) return false;
            entry.LastTouchedUtc = DateTime.UtcNow;
            room = CreateLobbyInfoUnsafe(code, entry);
            return true;
        }
    }

    public bool TryStartLobby(string rawCode, string hostToken,
        out LobbyRoomInfo room, out string error)
    {
        room = null;
        error = null;
        string code = Normalize(rawCode);
        if (string.IsNullOrEmpty(code)) { error = "room.invalid_code"; return false; }
        lock (gate)
        {
            if (lobbies.TryGetValue(code, out LobbyEntry entry) == false)
            {
                error = "room.not_found";
                return false;
            }
            if (entry.HostToken != hostToken) { error = "room.host_only"; return false; }
            if (entry.Players.Count < MinimumLobbyPlayers)
            {
                error = "room.not_enough_players";
                return false;
            }

            entry.IsStarted = true;
            entry.LastTouchedUtc = DateTime.UtcNow;
            room = CreateLobbyInfoUnsafe(code, entry);
            Console.WriteLine($"[{code}] 대기방 시작 ({entry.Players.Count}명)");
            return true;
        }
    }

    /// <summary>호스트가 시작한 대기방이거나 이미 실행 중인 게임방인지 확인한다.</summary>
    public bool IsGameRoomOpen(string rawCode)
    {
        string code = Normalize(rawCode);
        if (string.IsNullOrEmpty(code)) return false;
        lock (gate)
        {
            if (rooms.ContainsKey(code)) return true;
            return lobbies.TryGetValue(code, out LobbyEntry entry) && entry.IsStarted;
        }
    }

    private static bool TryNormalizeLobbyPlayer(string clientId, string nickName,
        out string normalizedClientId, out string normalizedNickName, out string error)
    {
        normalizedClientId = clientId?.Trim();
        normalizedNickName = nickName?.Trim();
        error = null;
        if (string.IsNullOrWhiteSpace(normalizedClientId)) { error = "player.invalid_client"; return false; }
        if (string.IsNullOrWhiteSpace(normalizedNickName) || normalizedNickName.Length > 16)
        {
            error = "player.invalid_nickname";
            return false;
        }
        return true;
    }

    private string CreateLobbyCodeUnsafe()
    {
        return Random.Shared.Next(0, 1_000_000).ToString($"D{LobbyCodeLength}");
    }

    private static LobbyRoomInfo CreateLobbyInfoUnsafe(string code, LobbyEntry entry) => new()
    {
        RoomCode = code,
        IsStarted = entry.IsStarted,
        MaxPlayers = MaximumLobbyPlayers,
        Players = entry.Players.Values.OrderBy(name => name, StringComparer.Ordinal).ToList(),
    };

    private void SweepExpiredLobbiesUnsafe()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan waitingLifetime = TimeSpan.FromMinutes(30);
        TimeSpan startedLifetime = TimeSpan.FromMinutes(2);
        foreach (string code in lobbies
                     .Where(pair => now - pair.Value.LastTouchedUtc >
                                    (pair.Value.IsStarted ? startedLifetime : waitingLifetime))
                     .Select(pair => pair.Key).ToArray())
            lobbies.Remove(code);
    }

    // 방을 먼저 찾아보고, 없다면 만든다.
    //
    // 방을 만드는 일은 자물쇠 밖에서 한다.
    // 지형을 만드는 데 20ms 가까이 걸리는데, 그동안 자물쇠를 쥐고 있으면
    // 초당 10번 도는 위치 방송이 이 방뿐 아니라 다른 방까지 통째로 멈춘다.
    private async Task<Room> EnterAsync(string code, int expectedPlayerCount)
    {
        lock (gate)
        {
            if (rooms.TryGetValue(code, out Entry found))
            {
                if (found.Room.IsGameStarted || found.Users >= expectedPlayerCount)
                    return null;

                found.Users++;
                return found.Room;
            }
        }

        Room created = new Room(code, logMovesPerSecond, expectedPlayerCount);
        Room discarded = null;
        Room selected = null;

        // lock(매개변수)?
        // : 해당 영역에 들어갈 수 있는 녀석들은. gate를 참조하고있는
        //  녀석들만 들어올 수 있습니다. 다른 녀석을 못들어옴.
        //  lock이라는게 걸리면 gate가 키의 역할을 한다고 생각하시면 됩니다.
        //  (gate를 알고있는 갈래의 객체들만 들어옴)
        //  안전한 처리를 위해 사용을 합니다.
        
        //  실제 컴퓨터에서 처리를 할때 해당 영역을 처음부터 끝까지
        //  처리함을 보장하는 장치 입니다. (이렇게 알고있으면 됩니다)
        //  매개변수에 객체는 private readonly로 하게되면
        //  나만 알고있는 키를 사용해서 더 안전하게 사용 할 수있습니다.
        //  (this로 걸게되면 이 클래스가 public이니까 외부에서도
        //  잠글 수 있음)
        lock (gate)
        {
            if (rooms.TryGetValue(code, out Entry entry))
            {
                // 내가 만드는 사이에 다른 사람이 먼저 만들었다. 내 것은 버린다.
                discarded = created;

                if (entry.Room.IsGameStarted || entry.Users >= expectedPlayerCount)
                    selected = null;
                else
                {
                    entry.Users++;
                    selected = entry.Room;
                }
            }
            else
            {
                entry = new Entry() { Room = created, Users = 1 };
                rooms.Add(code, entry);
                selected = created;
                Console.WriteLine($"[{code}] 방을 열었다. 총 방의 개수 : {rooms.Count}");
            }
        }

        if (discarded != null) await discarded.StopAsync();

        return selected;
    }

    // 방을 떠나고, 아무도 없으면 방을 지운다.
    private async Task LeaveAsync(string code)
    {
        Room removed = null;
        lock (gate)
        {
            // 예외 처리 한번 해준다
            if (rooms.TryGetValue(code, out Entry entry) == false) return;
            entry.Users--;
            if (entry.Users > 0) return;
            rooms.Remove(code);
            removed = entry.Room;
            Console.WriteLine($"[{code}] 아무도 없어서 방을 지움. 총 방의 개수 {rooms.Count}");
        }
        if (removed != null) await removed.StopAsync();
    }

    #endregion

    // 아래의 비동기 함수는 한 사람의 접속부터 끊김까지
    // 방을 찾아 넘기고, 끝나면 뒷정리하는 함수 입니다.
    // (RoomHub -> Room의 함수를 호출)
    public async Task HandleAsync(string code, int expectedPlayerCount,
        WebSocket socket, CancellationToken token)
    {
        Room room = await EnterAsync(code, expectedPlayerCount);

        if (room == null)
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                    "Game already started or room is full.",
                    CancellationToken.None);

            Console.WriteLine($"[{code}] 게임이 진행 중이거나 정원이 차서 입장이 거절됨");

            return;
        }

        // lastId를 여러 접속자가 동시에 수정 할수 있으므로
        // Interlocked를 이용해서 락을 걸어줍니다.
        // 이것도 외워버리십쇼.
        string id = $"u{Interlocked.Increment(ref lastId)}";

        try
        {
            // 방에 들어왔으니, 해당 유저의 소켓을 넘겨주고
            // join, receiveLoop, leave 처리를 room에게 위임합니다
            await room.HandleAsync(socket, id, token);
        }
        finally
        {
            // room.HandleAsync는 유저가 퇴장할때 끝납니다.
            await LeaveAsync(code);
        }
    }

    // 보든 방에 시간(일정 주기)별로 틱을 관리하는 함수 입니다.
    // 틱을 관리하면서 Task.WhenAll을 통해서 방의 메시지들을 방송합니다.
    // (RoomHub -> Room의 함수를 호출)
    public async Task BroadcastLoopAsync(CancellationToken token)
    {
        // 틱별로 상태 동기화 함수를 호출해줍니다.
        PeriodicTimer timer = new PeriodicTimer(
            TimeSpan.FromSeconds(1.0 / broadcastPerSecond));
        // PeriodicTimer?
        // 비동기 루프 안에서 주기적인 작업 처리를 안전하고 간편하게 해주는
        // 타이머 클래스 입니다. TimeSpan(검색해보세여)으로 1초당 몇펀 broadcast할지 타이머를
        // 설정해 놨습니다.

        try
        {
            // 타이머가 종료되면 자동으로 false를 반환합니다.
            while (await timer.WaitForNextTickAsync(token))
            {
                // 락을걸기전에 snapshot(복사본)이 들어갈
                // list를 생성해놓는다
                List<Room> snapshot = new List<Room>();

                // 위에 설정된 object 객체인 gate를 이용하여 lock을 걸어놓는다.
                lock (gate)
                {
                    if(rooms.Count ==0) continue;

                    foreach (Entry entry in rooms.Values)
                    {
                        snapshot.Add(entry.Room);
                    }
                }

                List<Task> sending = new List<Task>();
                foreach (Room room in snapshot)
                {
                    // 상태정보 보내는 Task를 가져와서 sending에 추가해준다.
                    sending.Add(room.BroadcastStateAsync());
                }
                
                await Task.WhenAll(sending);
            }

        }
        catch (OperationCanceledException)
        {
            // 서버가 꺼지는 중.
        }
        catch (Exception e)
        {
            Console.WriteLine($"[RoomHub] Exception: {e.Message}");
        }
    }

    public void StopAll()
    {
        lock (gate)
            foreach (Entry entry in rooms.Values) entry.Room.Stop();
    }

    // 방 코드를 정규화 하는 유틸 함수입니다.
    // 빈 문자처리, 특수문자 처리등을 합니다.
    public static string Normalize(string raw)
    {
        // 공백있니?
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // string을 컨버팅할때 문화권에 따라서 변경되는게 달라요
        // 우리는 소숫점을 3.141592 이렇게 쓰는데
        // 프랑스는 3,141592 이렇게 씀. 그래서 문화권에 따라서
        // string처리를 유연하게 해줘야함.
        //string code = raw.Trim().ToUpperInvariant();
        
        // 6자리 10진수 방 코드만 허용한다. 앞자리 0도 유효한 코드다.
        string code = raw.Trim();
        if (code.Length != LobbyCodeLength) return null;

        // string의 문자를 하나씩 검사해서
        // 특수문자가 있는지 확인해 준다
        foreach (char c in code)
        {
            if (char.IsDigit(c) == false) return null;
        }

        return code;
    }
}
