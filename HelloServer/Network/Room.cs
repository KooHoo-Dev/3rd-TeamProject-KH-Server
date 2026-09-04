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
        public DateTime LastLogAt;
        public bool IsReadyForBroadcast;
        public int MovesSinceLog;
        public int HasLeft;
        public int SendFailed;
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }

    private readonly record struct BroadcastWorkItem(
        object Message,
        string ExceptId,
        long EnqueuedAt);

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

    public Room(string code, int logMovesPerSecond)
    {
        this.code = code;
        this.logMovesPerSecond = logMovesPerSecond;
        gameSession = new GameSession(code);
        packetDispatcher = PacketHandlerRegistry.CreateDefault();
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

            // 확정도 취소도 오지 않은 낙하 예약을 여기서 거둡니다.
            TerrainCollapseCancelledMessage expired = gameSession.SweepExpiredCollapses();

            if (expired != null) EnqueueBroadcast(expired);
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
            await foreach (BroadcastWorkItem work in
                           broadcastQueue.Reader.ReadAllAsync(broadcastStop.Token))
            {
                if (ServerPerformanceMetrics.Enabled)
                {
                    double waitMs =
                        (Stopwatch.GetTimestamp() - work.EnqueuedAt) * 1000d / Stopwatch.Frequency;
                    Console.WriteLine($"[Perf] BroadcastQueueWaitMs={waitMs:F2}");
                }

                await BroadcastNowAsync(work.Message, work.ExceptId);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task StopAsync()
    {
        Stop();
        broadcastStop.CancelAfter(SendTimeoutMilliseconds + 1000);

        try
        {
            await broadcastLoop;
        }
        catch (OperationCanceledException)
        {
        }

        broadcastStop.Dispose();
    }

    public void Stop() => broadcastQueue.Writer.TryComplete();

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        StringBuilder builder = new();

        while (true)
        {
            WebSocketReceiveResult result =
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        token);
                }

                return null;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage) return builder.ToString();
        }
    }

    private async Task ReceiveLoopAsync(Member member, CancellationToken token)
    {
        PacketContext context = new(
            code,
            member.User,
            gameSession,
            message => SendAsync(member, message),
            message =>
            {
                EnqueueBroadcast(message);
                return Task.CompletedTask;
            },
            message => EnqueueBroadcast(message),
            ExecuteTerrainCommandAsync,
            move => LogMove(member, move));

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
        {
            if (member.IsReadyForBroadcast && member.User.Id != exceptId)
                sends.Add(SendRawAsync(member, json));
        }

        await Task.WhenAll(sends);
        ServerPerformanceMetrics.Write("Broadcast", startedAt);
    }

    private async Task SendRawAsync(Member member, string json)
    {
        if (member.Socket.State != WebSocketState.Open ||
            Volatile.Read(ref member.SendFailed) != 0)
            return;

        await member.SendLock.WaitAsync();

        try
        {
            if (member.Socket.State != WebSocketState.Open ||
                Volatile.Read(ref member.SendFailed) != 0)
                return;

            using CancellationTokenSource timeout = new(SendTimeoutMilliseconds);
            await member.Socket.SendAsync(
                Encoding.UTF8.GetBytes(json),
                WebSocketMessageType.Text,
                true,
                timeout.Token);
        }
        catch (OperationCanceledException)
        {
            MarkSendFailed(member);
        }
        catch (WebSocketException)
        {
            MarkSendFailed(member);
        }
        finally
        {
            member.SendLock.Release();
        }
    }

    private static void MarkSendFailed(Member member)
    {
        if (Interlocked.Exchange(ref member.SendFailed, 1) == 0)
            member.Socket.Abort();
    }

    private Task SendAsync(Member member, object message)
        => SendRawAsync(member, JsonSerializer.Serialize(message, message.GetType()));

    public Task BroadcastStateAsync()
    {
        if (!members.IsEmpty)
        {
            EnqueueBroadcast(new StateMessage
            {
                States = gameSession.CreatePlayerStateSnapshot(),
            });
        }

        return Task.CompletedTask;
    }

    private async Task<Member> JoinAsync(WebSocket socket, string id, CancellationToken token)
    {
        // 첫 메시지는 반드시 hello 여야 합니다. 아니면 그대로 연결을 놓습니다.
        // 왜 찍나 : 여기서 걸리면 오류 메시지도 close 프레임도 안 갑니다.
        //  콘솔에 남기지 않으면 클라이언트 쪽에서는 원인을 알 길이 없습니다.
        string first = await ReceiveTextAsync(socket, token);

        if (string.IsNullOrEmpty(first))
        {
            Console.WriteLine($"[{code}] {id} 첫 메시지 없이 끊김");
            return null;
        }

        if (JsonSerializer.Deserialize<PacketHeader>(first)?.Type != PacketTypes.Hello)
        {
            Console.WriteLine($"[{code}] {id} 첫 메시지가 hello 가 아님");
            return null;
        }

        HelloMessage hello = JsonSerializer.Deserialize<HelloMessage>(first);

        if (string.IsNullOrWhiteSpace(hello?.NickName))
        {
            Console.WriteLine($"[{code}] {id} hello 에 닉네임이 없음");
            return null;
        }

        Member member = new()
        {
            Socket = socket,
            LastLogAt = DateTime.Now,
            User = new User
            {
                Id = id,
                NickName = hello.NickName.Trim(),
            },
        };

        await gate.WaitAsync(token);

        try
        {
            WelcomeMessage welcome = new()
            {
                RoomCode = code,
                User = member.User,
                Users = members.Values.Select(x => x.User).ToArray(),
            };

            // 지형 스냅샷을 뜨는 것부터 Broadcast를 받을 자격이 생기는 것까지가 한 덩어리로 연결되어있어야 함.
            // 나눠져 있으면 다른곳에서 바꾸고 난리가 남. 그 변경은 스냅샷에도 없고, 아직 Broadcast 대상이 아니라서 받지도 못함.
            // 입장은 한 판에 한 번뿐이라 할만함.
            await terrainPipelineGate.WaitAsync(token);

            try
            {
                gameSession.AddPlayer(member.User, hello.DebugMode);
                gameSession.TryCreateMapSessionMessage(out MapSessionMessage mapSession);

                // 지형은 보내지 않습니다. 시드가 든 mapSession 만 보냄.
                // 클라이언트가 같은 절차로 같은 지형을 직접 생성함.
                await SendAsync(member, welcome);
                if (mapSession != null) await SendAsync(member, mapSession);
                await SendAsync(member, gameSession.CreateWorldItemSnapshotMessage());
                await SendAsync(member, gameSession.CreateInventorySnapshotMessage(id));
                await SendAsync(member, gameSession.CreatePlayerHealthSnapshotMessage());

                member.IsReadyForBroadcast = true;
                members[id] = member;
            }
            catch
            {
                gameSession.RemovePlayer(member.User.Id);
                throw;
            }
            finally
            {
                terrainPipelineGate.Release();
            }

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

            // 나간 사람이 잡아 둔 낙하 예약을 풉니다.
            //
            // 예약을 푸는 것은 지형을 바꾸지 않습니다. 칸은 그대로 있습니다.
            // 다른 사람 화면에서 떨어지던 덩어리만 멈춰 주면 되고,
            // 그건 취소된 번호만 알려 주면 됩니다.
            await ExecuteTerrainCommandAsync(() =>
            {
                TerrainCollapseCancelledMessage cancelled =
                    gameSession.RemovePlayer(member.User.Id);

                if (cancelled != null) EnqueueBroadcast(cancelled);
            });

            EnqueueBroadcast(new LeaveMessage { Id = member.User.Id }, member.User.Id);
        }
        finally
        {
            gate.Release();
        }

        Console.WriteLine($"[{code}] {member.User.NickName}({member.User.Id}) 나감");
    }

    public async Task HandleAsync(WebSocket socket, string id, CancellationToken token)
    {
        Member member = null;

        try
        {
            member = await JoinAsync(socket, id, token);

            if (member != null) await ReceiveLoopAsync(member, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException e)
        {
            string nickName = member?.User?.NickName ?? id;
            string userId = member?.User?.Id ?? id;
            Console.WriteLine($"[{code}] {nickName}({userId}) 연결 종료: {e.WebSocketErrorCode}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[{code}] WebSocket 처리 예외 ({id}): {e}");
        }
        finally
        {
            if (member != null) await LeaveAsync(member);
        }
    }

    private void LogMove(Member member, MoveMessage move)
    {
        member.MovesSinceLog++;

        if (logMovesPerSecond <= 0 ||
            !gameSession.State.Players.TryGetValue(member.User.Id, out PlayerRoomState player))
            return;

        TimeSpan gap = DateTime.Now - member.LastLogAt;

        if (gap.TotalSeconds < 1d / logMovesPerSecond) return;

        Console.WriteLine(
            $"[{code}] 받음 {member.User.NickName}({member.User.Id}) " +
            $"({player.X,7:F2}, {player.Y,7:F2}) " +
            $"지난 {gap.TotalSeconds:F1}초에 {member.MovesSinceLog}번");

        member.MovesSinceLog = 0;
        member.LastLogAt = DateTime.Now;
    }
}
