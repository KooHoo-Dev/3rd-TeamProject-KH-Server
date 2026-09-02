using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace HelloServer;

public sealed class Room
{
    private const int SendTimeoutMilliseconds = 3000;
    private sealed class Member
    {
        public User User;
        public WebSocket Socket;
        public int MovesSinceLog, HasLeft, SendFailed;
        public DateTime LastLogAt;
        public bool IsReadyForBroadcast;
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }
    private sealed record BroadcastWorkItem(object Message, string ExceptId, long EnqueuedAt);

    // 멤버 Gate 밖 방송 루프 읽기용 동시성 컬렉션
    private readonly ConcurrentDictionary<string, Member> members = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim terrainPipelineGate = new(1, 1);
    private readonly Channel<BroadcastWorkItem> broadcastQueue =
        Channel.CreateUnbounded<BroadcastWorkItem>(new() { SingleReader = true });
    private readonly CancellationTokenSource broadcastStop = new();
    private readonly Task broadcastLoop;
    private readonly string code;
    private readonly int logMovesPerSecond;
    private readonly GameSession gameSession;
    private readonly PacketDispatcher packetDispatcher;
    public bool IsEmpty => members.IsEmpty;

    public Room(string code, int logMovesPerSecond)
    {
        this.code = code; this.logMovesPerSecond = logMovesPerSecond;
        gameSession = new GameSession(code);
        packetDispatcher = PacketDispatcherFactory.CreateDefault();
        broadcastLoop = BroadcastQueueLoopAsync();
    }

    // 지형 명령의 상태 변경과 큐 등록 전용 Gate
    private async Task ExecuteTerrainCommandAsync(Action command)
    {
        long startedAt = ServerPerformanceMetrics.Timestamp();
        await terrainPipelineGate.WaitAsync();
        ServerPerformanceMetrics.Write("TerrainGateWait", startedAt);

        try
        {
            command();
        }
        finally
        {
            terrainPipelineGate.Release();
        }
    }

    private void EnqueueBroadcast(object message, string exceptId = null)
    {
        if (broadcastQueue.Writer.TryWrite(new(message, exceptId, Stopwatch.GetTimestamp())) == false)
            throw new InvalidOperationException("Room broadcast queue is closed.");
    }

    private async Task BroadcastQueueLoopAsync()
    {
        try
        {
            await foreach (BroadcastWorkItem work in broadcastQueue.Reader.ReadAllAsync(broadcastStop.Token))
            {
                if (ServerPerformanceMetrics.Enabled)
                    Console.WriteLine($"[Perf] BroadcastQueueWaitMs=" +
                                      $"{(Stopwatch.GetTimestamp() - work.EnqueuedAt) * 1000d / Stopwatch.Frequency:F2}");

                await BroadcastNowAsync(work.Message, work.ExceptId);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task StopAsync()
    {
        Stop();
        broadcastStop.CancelAfter(SendTimeoutMilliseconds + 1000);
        try { await broadcastLoop; } catch (OperationCanceledException) { }
        broadcastStop.Dispose();
    }

    public void Stop() => broadcastQueue.Writer.TryComplete();

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[4096]; StringBuilder builder = new();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, token);
                return null;
            }
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) return builder.ToString();
        }
    }

    private async Task ReceiveLoopAsync(Member member, CancellationToken token)
    {
        PacketContext context = new(code, member.User, gameSession, message => SendAsync(member, message),
            message => { EnqueueBroadcast(message); return Task.CompletedTask; }, message => EnqueueBroadcast(message),
            ExecuteTerrainCommandAsync, move => LogMove(member, move));
        while (!token.IsCancellationRequested)
        {
            string text = await ReceiveTextAsync(member.Socket, token);
            if (string.IsNullOrEmpty(text)) return;
            await packetDispatcher.DispatchAsync(context, text, token);
        }
    }

    private async Task BroadcastNowAsync(object message, string exceptId)
    {
        long startedAt = ServerPerformanceMetrics.Timestamp();
        string json = JsonSerializer.Serialize(message, message.GetType());
        List<Task> sends = new();
        foreach (Member member in members.Values)
            if (member.IsReadyForBroadcast && member.User.Id != exceptId) sends.Add(SendRawAsync(member, json));
        await Task.WhenAll(sends);
        ServerPerformanceMetrics.Write("Broadcast", startedAt);
    }

    private async Task SendRawAsync(Member member, string json)
    {
        if (member.Socket.State != WebSocketState.Open || Volatile.Read(ref member.SendFailed) != 0) return;
        await member.SendLock.WaitAsync();
        try
        {
            if (member.Socket.State != WebSocketState.Open || Volatile.Read(ref member.SendFailed) != 0) return;
            using CancellationTokenSource timeout = new(SendTimeoutMilliseconds);
            await member.Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, timeout.Token);
        }
        catch (OperationCanceledException) { MarkSendFailed(member); }
        catch (WebSocketException) { MarkSendFailed(member); }
        finally { member.SendLock.Release(); }
    }

    private static void MarkSendFailed(Member member)
    {
        if (Interlocked.Exchange(ref member.SendFailed, 1) == 0) member.Socket.Abort();
    }
    private Task SendAsync(Member member, object message) => SendRawAsync(member, JsonSerializer.Serialize(message, message.GetType()));
    public Task BroadcastStateAsync()
    {
        if (!members.IsEmpty) EnqueueBroadcast(new StateMessage { States = gameSession.CreatePlayerStateSnapshot() });
        return Task.CompletedTask;
    }

    private async Task<Member> JoinAsync(WebSocket socket, string id, CancellationToken token)
    {
        string first = await ReceiveTextAsync(socket, token);
        if (string.IsNullOrEmpty(first) || JsonSerializer.Deserialize<PacketHeader>(first)?.Type != PacketTypes.Hello) return null;
        HelloMessage hello = JsonSerializer.Deserialize<HelloMessage>(first);
        if (string.IsNullOrWhiteSpace(hello?.NickName)) return null;
        Member member = new() { Socket = socket, LastLogAt = DateTime.Now, User = new User { Id = id, NickName = hello.NickName.Trim() } };
        WelcomeMessage welcome; MapSessionMessage mapSession = null; TerrainSnapshotMessage terrain; WorldItemSnapshotMessage drops; InventorySnapshotMessage inventory;
        await gate.WaitAsync(token);

        try
        {
            welcome = new WelcomeMessage { RoomCode = code, User = member.User, Users = members.Values.Select(x => x.User).ToArray() };
            gameSession.AddPlayer(member.User);
            gameSession.TryCreateMapSessionMessage(out mapSession);
            terrain = gameSession.CreateTerrainSnapshotMessage(); drops = gameSession.CreateWorldItemSnapshotMessage(); inventory = gameSession.CreateInventorySnapshotMessage(id);
            members[id] = member; // 초기 직접 메시지 전송 완료 전 일반 방송 제외 상태
        }
        finally { gate.Release(); }

        try
        {
            await SendAsync(member, welcome);
            if (mapSession != null) await SendAsync(member, mapSession);
            await SendAsync(member, terrain);
            await SendAsync(member, drops);
            await SendAsync(member, inventory);
        }
        catch
        {
            await LeaveAsync(member); throw;
        }

        await gate.WaitAsync(token);

        try
        {
            member.IsReadyForBroadcast = true;
            EnqueueBroadcast(new JoinMessage { User = member.User }, id);
        }
        finally
        {
            gate.Release();
        }

        Console.WriteLine($"[{code}] {member.User.NickName}({id}) 들어옴");
        return member;
    }

    private async Task LeaveAsync(Member member)
    {
        if (Interlocked.Exchange(ref member.HasLeft, 1) != 0) return;
        await gate.WaitAsync();
        try
        {
            members.TryRemove(member.User.Id, out _);
            await ExecuteTerrainCommandAsync(() =>
            {
                if (gameSession.RemovePlayer(member.User.Id)) EnqueueBroadcast(gameSession.CreateTerrainSnapshotMessage());
            });
            EnqueueBroadcast(new LeaveMessage { Id = member.User.Id }, member.User.Id);
        }
        finally { gate.Release(); }
        Console.WriteLine($"[{code}] {member.User.NickName}({member.User.Id}) 나감");
    }

    public async Task HandleAsync(WebSocket socket, string id, CancellationToken token)
    {
        Member member = null;
        try { member = await JoinAsync(socket, id, token); if (member != null) await ReceiveLoopAsync(member, token); }
        catch (OperationCanceledException) { }
        catch (WebSocketException e) { Console.WriteLine($"[{code}] {(member?.User?.NickName ?? id)}({member?.User?.Id ?? id}) 연결 종료: {e.WebSocketErrorCode}"); }
        catch (Exception e) { Console.Error.WriteLine($"[{code}] WebSocket 처리 예외 ({id}): {e}"); }
        finally { if (member != null) await LeaveAsync(member); }
    }

    private void LogMove(Member member, MoveMessage move)
    {
        member.MovesSinceLog++;
        if (logMovesPerSecond <= 0 || !gameSession.State.Players.TryGetValue(member.User.Id, out PlayerRoomState player)) return;
        TimeSpan gap = DateTime.Now - member.LastLogAt;
        if (gap.TotalSeconds < 1d / logMovesPerSecond) return;
        Console.WriteLine($"[{code}] 받음 {member.User.NickName}({member.User.Id}) ({player.X,7:F2}, {player.Y,7:F2}) 지난 {gap.TotalSeconds:F1}초에 {member.MovesSinceLog}번");
        member.MovesSinceLog = 0; member.LastLogAt = DateTime.Now;
    }
}
