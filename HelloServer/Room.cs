using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HelloServer;

// 방 하나. 방에 있는 사람들을 들고 있다가
// 메세지를 전달해 준다.
// 방의 기능은 아래와 같습니다
// 1. 들어온다 : JoinAsync
// 2. 말한다   : ReceiveLoopAsync => 결국 요게 채팅임
// 3. 나간다   : LeaveAsync
// 4. 뿌린다   : BroadcastStateAsync

// Room은 메시지를 무리는 법만 알고
// 언제 뿌릴지는 결정하지 않습니다. 

public class Room
{
    // 접속자 한 명.
    private class Member
    {
        public User User;
        public WebSocket Socket;

        public int MovesSinceLog;
        
        // DateTime?
        // : 날짜랑 시간을 표현하고 조작할 때 사용하는 구조체 입니다.
        //  DateTime.Now : 현재 지역 시간을 나타낼 수 있ㅅ브니다
        //  DateTime.UtcNow : 협정 세계시(영국 본초 자오선(?))
        // 출력 서식을 따로 지정할 수 있습니다. 그거는 MS 홈페이지 가서 보세요
        public DateTime LastLogAt;
        
        // 보낼때 여러메시지를 동시에 보내지 않기 위에
        // 사람(멤버)마다 Gate를 하나씩 두고 한번에 하나씩 보내기 위해
        // 사용하는 클래스. (비동기에서 lock처리가 안되서 사용)
        // 읽는것은 여러 쓰레드에서 읽을 수 있는데 사용(Write)는
        // 하나의 쓰레드에서만 온전히 돌아갈 수 있도록 하게 해주는 클래스
        public readonly SemaphoreSlim SendLock 
            = new SemaphoreSlim(1, 1);
    }
    
    // race condition이 일어나도 여러 쓰레드에서 동시적으로
    // 참조 하여 읽을 수 있는 딕셔너리 입니다. 일반적인 Dictionary를 쓰면
    // race condition이 발생하면 깨질꺼에여.
    // 여러 쓰레드에서 동시에 사용하더라도 딕셔너리의 한 상태를 유지 시킬 수 있는
    // 안정성이 보장된 딕셔너리 입니다.
    private readonly ConcurrentDictionary<string, Member> members = new();

    // 들어오고 나가는 메시지 처리(일)을 한줄로 세우는 자물쇠 입니다.
    // lock블록이 await가 안먹어서 사용합니다.
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly string code; // 방번호
    private readonly int logMovesPerSecond; // 룸허브를 통해서 전달 받습니다.
    private readonly GameSession gameSession;
    private readonly PacketDispatcher packetDispatcher;
    private readonly SemaphoreSlim terrainPipelineGate = new(1, 1);

    public bool IsEmpty => members.IsEmpty;

    private async Task ExecuteTerrainCommandAsync(Func<Task> command)
    {
        await terrainPipelineGate.WaitAsync();

        try
        {
            await command();
        }
        finally
        {
            terrainPipelineGate.Release();
        }
    }
    
    public Room(string code, int logMovesPerSecond)
    {
        this.code = code;
        this.logMovesPerSecond = logMovesPerSecond;
        gameSession = new GameSession(code);
        packetDispatcher = PacketDispatcherFactory.CreateDefault();
    }

    #region 듣기

    // 글자를 받는다. 상대가 연결을 닫았으면 nulll을 돌려준다
    // PS
    // : 긴글자일 경우 가끔 조각으로 나뉘어서 오는 경우가 있다.
    //  우리가 StreamReader를 다뤘을때 처럼 메시지의 끝 EndOf~~
    //  을 정확하게 파악하여 메시시 한 단위를 만들어 준다.
    private static async Task<string> ReceiveTextAsync
        (WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        StringBuilder builder = new StringBuilder();
        // StringBuilder?
        // : 여러 문자들을 이어붙힐때 사용하는 객체. string은 각각 개별로
        // 생성되는 별도의 객체임("a" + "b" + "c" = "abc" 이런식이면 총 string 4개가 생성됨)
        // StringBuilder를 사용하게 되면 하나의 스트링 객체를 이어 붙힐 수 있게 됨.
        // 예전에 유니티에서도 많이 썼었음.
        // TMP_Text text 객체에게 text.SetText("abc"); 하면 내부에서 StringBuilder를
        // 이용해서 문자열을 취합해 줍니다. 최적화된 "문자열 계산기"라고 생각하면 됩니다

        while (true)
        {
            // 웹소켓 수신 결과를 저장할 수있는 객체를 선언해주고,
            // await 키워드를 이용하여 해당 소켓(유저와 연결된..)에
            // 메시지가 들어올때까지 기다려 줍니다.
            WebSocketReceiveResult result = 
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

            // 예외 처리부터 해줍니다. 소켓이 닫혔을 경우.
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        CancellationToken.None);
                }

                return null;
            }
            // 일단 메세지가 도착을 했으면 StringBuilder에 이어 붙혀 줍니다. 
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            // 메세지가 끝났니?. 끝났다면
            // StringBuilder.ToString을 호출해서 String을 생성하여 반환합니다. 
            if (result.EndOfMessage) return builder.ToString();

            // 아니라면 다시 루프를 반복합니다
        }
    }
    
    // 멤버와 연결이 끊길때까지 멤버가 보낸 메시지를 계속 듣는다. 
    private async Task ReceiveLoopAsync(Member member, CancellationToken token)
    {
        PacketContext context = new PacketContext(
            code,
            member.User,
            gameSession,
            message => SendAsync(member, message),
            message => BroadcastAsync(message),
            ExecuteTerrainCommandAsync,
            move => LogMove(member, move));

        // 토큰에 취소 요청이 없으면 계속 돈다
        while (token.IsCancellationRequested == false)
        {
            string text = await ReceiveTextAsync(member.Socket, token);
            // text가 비어있으면 닫았다는 뜻
            if (string.IsNullOrEmpty(text)) return;

            // Type 분기와 도메인 처리는 Dispatcher와 Handler에 위임합니다.
            // 등록되지 않은 Type은 기존과 같이 무시합니다.
            await packetDispatcher.DispatchAsync(context, text, token);
        }
    }

    #endregion

    #region 뿌리기

    // 메시지를 여러명한테 뿌리는 함수
    private async Task BroadcastAsync(object message, string exceptId = null)
    {
        string json = JsonSerializer.Serialize(message, message.GetType());
        
        // 보낼 json객체를 미리 생성하고,
        // 유저수에 맞게 보내는 작업을 처리한다.
        List<Task> sending = new List<Task>();

        // 딕셔너리에 있는 모든 멤버를 순회한다
        foreach (Member member in members.Values)
        {
            // 제외 대상이라면 건너 뛴다
            if(member.User.Id == exceptId) continue;
            // 한명단위 메시지 Task를 만들어서 List에 넣어준다
            sending.Add(SendRawAsync(member, json));
        }
        
        await Task.WhenAll(sending);
    }

    // 한명의 User에게 메시지를 보내는 함수
    private async Task SendRawAsync(Member member, string json)
    {
        // 소켓이 끊겨있는지 확인을 해준다. 보내기전에 마지막 체크
        if (member.Socket.State != WebSocketState.Open) return;
        
        // 보내는 중인 메시지가 있다면 lock이 풀릴때까지 잠깐 기다린다.
        // 그리고 내가 보낼 턴이면 잠궈버린다. 두가지를 동시에 수행합니다.
        await member.SendLock.WaitAsync();

        try
        {
            // 보낼때는 string이 아니라 byte배열로 바꿔준다
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await member.Socket.SendAsync(
                bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // 보내는 순간 끊길 수 있음.
            // 나가기 처리는 다른 곳에서 함.
        }
        finally // 예외가 발생하든 안하든 꼭 처리되는 finally 구문(찾아 보십쇼) 
        {
            member.SendLock.Release();
        }
    }

    // 단순 호출용 유틸 함수
    private Task SendAsync(Member member, object message)
    {
        return SendRawAsync(member, JsonSerializer.Serialize(message, message.GetType()));
    }

    // 지금 이 방의 사람들 위치를 한번씩 뿌린다.
    // 언제 뿌릴지는 RoomHub에서 정한다.
    public async Task BroadcastStateAsync()
    {
        // 방에 멤버가 없다면(방이 사라질때) 보내지 않는다.
        if (members.IsEmpty) return;
        
        PlayerState[] states = gameSession.CreatePlayerStateSnapshot();
        await BroadcastAsync(new StateMessage { States = states });
    }
    
    #endregion

    #region 들어오기, 나가기

    private async Task<Member> JoinAsync(WebSocket socket, 
        string id, CancellationToken token)
    {
        // 첫 메시지를 들어 봅니다. 지금 서버코드 규약에 따르면
        // hello 여야 합니다
        string first = await ReceiveTextAsync(socket, token);
        // 만약에 메시지가 이상하다면 종료
        if (string.IsNullOrEmpty(first)) return null;
        
        // 타입을 꺼내준다
        PacketHeader kind = JsonSerializer.Deserialize<PacketHeader>(first);
        // hello인지 확인해준다
        if (kind?.Type != PacketTypes.Hello)
        {
            Console.WriteLine($"[{code}] 첫 메시지가 hello가 아님 : {first}");
            return null;
        }
        
        // 아래서 부터는 정상처리
        HelloMessage hello = JsonSerializer.Deserialize<HelloMessage>(first);
        // 메시지와 매개변수를 조합해서 Member객체를 생성한다.
        Member member = new Member();
        member.Socket = socket;
        member.LastLogAt = DateTime.Now; // 들어온 시각으로 맞춰 둔다.
        member.User = new User();
        member.User.Id = id;
        member.User.NickName = hello.NickName.Trim();
        
        // 들어오고 나가는 일은 한사람에 한명씩 해야합니다.
        // 사람이 들어오면 현재 방에 있는 멤버들에게도 메시지를 보내줘야겠죠?
        // 그래서 여기서도 lock을 걸어줘야 하는데 await이기 때문에
        // lock을 못걸어서 gate를 이용해서 대기하여 처리합니다.
        await gate.WaitAsync(token);

        try
        {
            // 누군가가 hello 메시지를 보냈으면
            // welcome 메시지를 이용해서
            // 현재 방 사람들을 접속한 유저에게 전송하고,
            // join 메시지를 다른 사람들에게 보내준다
            List<User> already = new List<User>();

            foreach (Member other in members.Values)
                already.Add(other.User);

            // welcome 메시지를 전송
            WelcomeMessage welcome = new WelcomeMessage();
            welcome.RoomCode = code; // 서버 방정보를 보낸다
            welcome.User = member.User; // 서버에서 생성한 유저 정보를 접속자에게 보낸다
            welcome.Users = already.ToArray(); // 현재 방에 있는 유저들 정보를 보낸다
            await SendAsync(member, welcome);

            gameSession.AddPlayer(member.User);
            if (gameSession.TryCreateMapSessionMessage(out MapSessionMessage mapSession))
                await SendAsync(member, mapSession);
            await SendAsync(member, gameSession.CreateTerrainSnapshotMessage());
            await SendAsync(member, gameSession.CreateWorldItemSnapshotMessage());
            await SendAsync(
                member,
                gameSession.CreateInventorySnapshotMessage(member.User.Id));

            members[member.User.Id] = member;
            // join 메시지를 뿌린다. 접속자인 member 에게는 보내지 않는다
            await BroadcastAsync(new JoinMessage { User = member.User }, member.User.Id);

        }
        finally
        {
            gate.Release();
        }
        
        Console.WriteLine($"[{code}] {member.User.NickName}({member.User.Id}) 들어옴");
        return member;
    }

    private async Task LeaveAsync(Member member)
    {
        // 들어오기과 같은 자물쇠를 사용합니다.
        // 들어오기 나가기는 방의 멤버를 수정하고 메시지를 처리하기 때문에
        // 같은 자물쇠를 사용해줘야 합니다.
        // (안하면 유령객체 생길수도?)
        await gate.WaitAsync();

        try
        {
            members.TryRemove(member.User.Id, out _);
            gameSession.RemovePlayer(member.User.Id);
            // 퇴장한것을 알려줍니다.
            await BroadcastAsync(new LeaveMessage { Id = member.User.Id }, member.User.Id);
        }
        finally
        {
            gate.Release();
        }
        
        Console.WriteLine($"[{code}] {member.User.NickName}({member.User.Id}) 나감");
    }

    // 한 사람의 접속부터 끊김까지 통째로 관리하느 ㄴ함수
    // id를 외부에서 전달받는 이유
    // : userID는 겹치면 안되기에 모든 유저를 관리하느 RoomHub에서 전달해 준다.
    public async Task HandleAsync(WebSocket socket,
        string id, CancellationToken token)
    {
        Member member = null;

        try
        {
            // Join 단계의 Snapshot 전송 예외도 아래 공통 오류 처리에서 기록한다.
            member = await JoinAsync(socket, id, token);
            // hello 안보내고 딴소리 했다. 방에 못 들인다
            if (member == null) return;

            // 접속완료 했으면 메시지를 계속 들을 수 있게
            // 루프를 호출해준다.
            await ReceiveLoopAsync(member, token);
        }
        catch (OperationCanceledException)
        {
            // 서버 꺼지는 중. 정상임
        }
        catch (WebSocketException exception)
        {
            // 클라이언트 프로세스가 정상 close 프레임 없이 종료된 경우에도
            // 방의 Leave 정리 경로를 그대로 수행합니다.
            Console.WriteLine(
                $"[{code}] {member.User.NickName}({member.User.Id}) 연결 종료: {exception.WebSocketErrorCode}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[{code}] WebSocket 처리 예외 ({id}): {exception}");

            if (member?.Socket.State == WebSocketState.Open)
            {
                try
                {
                    await SendAsync(member, new ErrorMessage
                    {
                        Code = "server.internal_error",
                        Message = "서버가 메시지를 처리하는 중 오류가 발생했습니다.",
                    });
                }
                catch (WebSocketException)
                {
                    // 이미 연결이 끊긴 경우에는 로그만 남기고 종료한다.
                }
            }
        }
        finally
        {
            // 루프가 종료되었으면 연결이 끊어진 것
            // 퇴장 처리 해준다
            if (member != null)
                await LeaveAsync(member);
        }
    }
    
    #endregion
    
    
    // LogMove 함수는 수업에서 안한 부분
    // 위치가 들어오고 있다는 것을 눈으로 보여 주는 함수. 
    // 사실 없어도 그만.
    
    // 받을 때마다 찍지 않고 간격을 두는 이유?
    // : 오는 것을 다 찍으면 콘솔이 위치로만 채워져 정작 중요한 들어옴,나감이 안 보인다.
    //  대신 그동안 몇 번 받았는지 출력해줌.
    private void LogMove(Member member, MoveMessage move)
    {
        member.MovesSinceLog++;

        if (logMovesPerSecond <= 0) return;

        if (gameSession.State.Players.TryGetValue(
                member.User.Id, out PlayerRoomState player) == false)
            return;

        TimeSpan gap = DateTime.Now - member.LastLogAt;
        if (gap.TotalSeconds < 1.0 / logMovesPerSecond) return;

        // 보낸 쪽이 적은 번호가 서버가 아는 번호와 다르면 그대로 드러내 준다.
        // 평소에는 같으므로 아무것도 붙지 않는다.
        string claimed = move.Id == member.User.Id ? "" : $"  (보낸 쪽이 적은 번호 : {move.Id})";

        Console.WriteLine(
            $"[{code}] 받음 {member.User.NickName}({member.User.Id}) " +
            $"({player.X,7:F2}, {player.Y,7:F2})  " +
            $"지난 {gap.TotalSeconds:F1}초에 {member.MovesSinceLog}번{claimed}");

        member.MovesSinceLog = 0;
        member.LastLogAt = DateTime.Now;
    }
}
