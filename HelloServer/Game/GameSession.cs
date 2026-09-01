namespace HelloServer;

// Room의 연결 수명과 분리된 경기 상태 접근 지점입니다.
public sealed class GameSession
{
    public RoomState State { get; } = new();

    public void AddPlayer(User user)
    {
        State.Players[user.Id] = new PlayerRoomState
        {
            Id = user.Id,
            NickName = user.NickName,
        };
    }

    public void RemovePlayer(string playerId)
    {
        State.Players.TryRemove(playerId, out _);
        State.Exploration.Players.TryRemove(playerId, out _);
    }

    public void MovePlayer(string playerId, float x, float y)
    {
        if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
            return;

        player.X = x;
        player.Y = y;
    }

    public PlayerState[] CreatePlayerStateSnapshot()
    {
        List<PlayerState> states = new List<PlayerState>();

        foreach (PlayerRoomState player in State.Players.Values)
        {
            states.Add(new PlayerState
            {
                Id = player.Id,
                X = player.X,
                Y = player.Y,
            });
        }

        return states.ToArray();
    }

    public void SetMapSession(MapSessionDescriptor descriptor)
    {
        if (descriptor?.IsValid() != true)
            throw new ArgumentException("유효하지 않은 맵 세션입니다.", nameof(descriptor));

        State.MapSession.Descriptor = descriptor;
    }

    public bool TryCreateMapSessionMessage(out MapSessionMessage message)
    {
        MapSessionDescriptor descriptor = State.MapSession.Descriptor;
        if (descriptor == null)
        {
            message = null;
            return false;
        }

        message = new MapSessionMessage { Session = descriptor };
        return true;
    }
}
