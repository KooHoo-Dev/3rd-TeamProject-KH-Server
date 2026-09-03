namespace HelloServer;

// Handler가 WebSocket 구현을 직접 참조하지 않도록 필요한 기능만 전달합니다.
public sealed class PacketContext
{
    private readonly Func<object, Task> broadcastAsync;
    private readonly Func<object, Task> sendAsync;
    private readonly Action<object> enqueueBroadcast;
    private readonly Func<Action, Task> executeTerrainCommandAsync;
    private readonly Action<MoveMessage> moveReceived;

    public string RoomCode { get; }
    public User User { get; }
    public GameSession GameSession { get; }

    public PacketContext(
        string roomCode,
        User user,
        GameSession gameSession,
        Func<object, Task> sendAsync,
        Func<object, Task> broadcastAsync,
        Action<object> enqueueBroadcast,
        Func<Action, Task> executeTerrainCommandAsync,
        Action<MoveMessage> moveReceived)
    {
        RoomCode = roomCode;
        User = user;
        GameSession = gameSession;
        this.sendAsync = sendAsync;
        this.broadcastAsync = broadcastAsync;
        this.enqueueBroadcast = enqueueBroadcast;
        this.executeTerrainCommandAsync = executeTerrainCommandAsync;
        this.moveReceived = moveReceived;
    }

    public Task SendAsync(object message)
    {
        return sendAsync(message);
    }

    public Task BroadcastAsync(object message)
    {
        return broadcastAsync(message);
    }

    // 지형 Gate 안의 상태 변경과 방송 큐 등록 전용 처리
    public void EnqueueBroadcast(object message) => enqueueBroadcast(message);

    public Task ExecuteTerrainCommandAsync(Action command)
    {
        return executeTerrainCommandAsync(command);
    }

    public void RecordMove(MoveMessage move)
    {
        moveReceived(move);
    }
}
