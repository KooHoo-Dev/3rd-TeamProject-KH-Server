# 3rd-TeamProject-KH-Server

유니티 2D 채굴 PvP 게임의 방 서버. ASP.NET Core WebSocket, .NET 10.

- 주고받는 메시지 전부 : [`서버 API.md`](서버%20API.md)
- 접속과 배포 : [`학생용. 서버 접속과 배포.md`](학생용.%20서버%20접속과%20배포.md)

---

## 실행

```bash
dotnet run --project HelloServer --no-launch-profile
```

`--no-launch-profile` 을 빼면 `launchSettings.json` 때문에 5203 포트로 뜹니다.
포트는 `ASPNETCORE_URLS` 로 정합니다. **코드에 적지 마세요** — `app.Run()` 은 비워 둡니다.

| | |
|---|---|
| `GET /ping` | `pong` 을 돌려줍니다. 살아 있는지 확인용 |
| `WS /room?code={방코드}` | 게임이 오가는 유일한 통로 |

방코드는 영문·숫자만 됩니다. 처음 들어온 사람이 방을 만들고, 마지막 사람이 나가면 방이 사라집니다.

---

## 접속 순서

**첫 메시지는 반드시 `hello` 여야 합니다.**

```
클라 → 서버   {"Type":"hello","NickName":"진호"}

서버 → 클라   welcome              내 Id 와 방에 있던 사람들
서버 → 클라   map_session          지형 시드 (아래 "지형" 참고)
서버 → 클라   world_item.snapshot  바닥에 떨어져 있는 아이템
서버 → 클라   inventory.snapshot   내 가방
```

`hello` 가 아니거나 닉네임이 비어 있으면 **연결이 그냥 끊깁니다.** 오류 메시지도 안 갑니다.
서버 콘솔에는 이유가 찍힙니다.

---

## 메시지 종류

`Type` 문자열로 구분합니다. 모든 메시지가 `Type` 을 가집니다.
요청에 `RequestId` 를 넣으면 그 응답에 그대로 담겨 돌아옵니다.

아래는 목록만입니다. **칸 하나하나와 실제 JSON 예시, 오류 코드는
[`서버 API.md`](서버%20API.md) 에 있습니다.**

### 클라 → 서버

| Type | 하는 일 | 처리하는 곳 |
|---|---|---|
| `hello` | 입장. 첫 메시지 | `Room.JoinAsync` |
| `player.move` | 내 위치 알리기 | `PlayerMovePacketHandler` |
| `move` | `player.move` 의 옛 이름 | 〃 |
| `chat.send` | 채팅 | `ChatSendPacketHandler` |
| `chat` | `chat.send` 의 옛 이름 | 〃 |
| `terrain_excavate` | 칸 하나 파기 | `TerrainExcavationPacketHandler` |
| `terrain_collapse_start` | 낙하 시작하겠다고 알리기 | `TerrainCollapseStartPacketHandler` |
| `terrain_collapse_place` | 낙하가 끝난 자리 확정 | `TerrainCollapsePlacementPacketHandler` |
| `world_item.pickup` | 바닥 아이템 줍기 | `WorldItemPickupPacketHandler` |

### 서버 → 클라

| Type | 언제 | 받는 사람 |
|---|---|---|
| `welcome` | 입장 직후 | 들어온 사람만 |
| `map_session` | 입장 직후 | 들어온 사람만 |
| `world_item.snapshot` | 입장 직후 | 들어온 사람만 |
| `inventory.snapshot` | 입장 직후, 아이템을 주웠을 때 | 그 사람만 |
| `join` | 누가 들어오면 | 그 사람 빼고 전부 |
| `leave` | 누가 나가면 | 그 사람 빼고 전부 |
| `state` | 초당 10번, 모두의 위치 | 전부 |
| `chat` | 누가 말하면 | 전부 |
| `terrain_batch` | 지형이 바뀌면 | 전부 |
| `terrain_collapse_started` | 낙하가 시작되면 | 전부 |
| `terrain_collapse_cancelled` | 낙하를 잡아 둔 사람이 나가거나 · 확정이 거절되거나 · 30초가 지나면 | 전부 |
| `world_item.spawned` | 아이템이 떨어지면 | 전부 |
| `world_item.removed` | 아이템을 누가 주우면 | 전부 |
| `error` | 요청이 거절되면 | 보낸 사람만 |

> **이름이 세 갈래로 섞여 있습니다.** 그냥 붙인 이름(`hello`, `state`), 점(`player.move`, `world_item.pickup`),
> 밑줄(`map_session`, `terrain_excavate`). 규칙이 아니라 **자라난 흔적**입니다.
> 유니티 클라이언트가 이 문자열 그대로 맞춰 두었으니 **바꾸면 안 됩니다.**

메시지 순서는 방마다 하나씩 있는 방송 큐가 지킵니다. 지형 메시지의 `BaseRevision` / `ResultRevision` 이
"이 변경은 몇 번에서 몇 번으로 간다"를 말해 주고, 클라이언트는 번호가 이어질 때만 적용합니다.

---

## 지형은 서버가 보내지 않습니다

서버는 `map_session` 에 **시드**만 보냅니다. 클라이언트가 그 시드로 지형을 직접 만듭니다.
예전에는 지형 전체(482KB)를 보냈습니다.

**두 생성기는 한 몸입니다. 한쪽만 고치면 그 방의 지형이 어긋납니다.**

```
서버 : HelloServer/Game/Terrain/ServerTerrainGenerator*.cs   (7개 파일 전부)
클라 : Assets/02. Scripts/TerrainCollapse/Generation/

서버 쪽 파일 이름은 클라이언트 쪽과 짝이 맞게 붙였다. 나란히 놓고 비교하라는 뜻이다.
```

난수를 언제 몇 번 뽑는지, 격자를 어느 순서로 도는지, 조건을 어느 순서로 보는지가 전부 결과를 정합니다.
보기 좋게 다듬는 것도 결과를 바꿉니다. 고쳐야 한다면 **양쪽을 함께** 고치고 확인하세요.

---

## 새 메시지 종류 추가하기

**네 곳을 고쳐야 합니다. 하나라도 빠지면 동작하지 않습니다.**

1. `Packets/PacketTypes.cs` 에 문자열 상수 추가
2. `Packets/Messages/` 의 알맞은 파일에 메시지 클래스 추가 (`PacketHeader` 를 상속)
3. `Packets/Handlers/` 에 `IPacketHandler` 를 구현한 핸들러 추가
4. **`Packets/Handlers/PacketHandlerRegistry.cs` 목록에 그 핸들러 등록**

4번을 빠뜨리면 서버가 그 메시지를 받고도 아무 일도 하지 않습니다.
콘솔에 `[방] 모르는 메시지 종류` 가 한 번 찍히니 그것으로 알 수 있습니다.

클라이언트 쪽도 짝이 필요합니다. `Assets/02. Scripts/Networking/` 의
`Contracts/`(메시지 모양)와 `RoomClient.RouteIncoming`(받는 갈래)입니다.

---

## 폴더

| | |
|---|---|
| `Program.cs` | 서버를 띄우고 `/ping` 과 `/room` 을 연다 |
| `Network/Room.cs` | 방 하나. 소켓 읽기·쓰기, 입장·퇴장, 방송 큐 |
| `Network/RoomHub.cs` | 방 목록. 만들고 지우고, 초당 10번 위치를 뿌린다 |
| `Packets/` | 오가는 메시지 종류(`PacketTypes.cs`)와 공통 헤더 |
| `Packets/Messages/` | 메시지의 모양 |
| `Packets/Handlers/` | 메시지가 왔을 때 실행되는 것 |
| `Game/` | 게임 규칙과 상태. 소켓을 모른다 |
| `Game/GameSession.Terrain.cs` | 굴착과 낙하. `GameSession` 의 한 조각이다 |
| `Game/Terrain/` | 지형 표 읽기와 지형 생성 (클라와 짝이 맞는 7개 파일) |
| `Data/` | 지형·아이템 표 (TSV) |

---

## 배포

학생은 서버에 붙어 `./deploy.sh` 한 줄이면 됩니다.
자세한 것은 [`학생용. 서버 접속과 배포.md`](학생용.%20서버%20접속과%20배포.md) 에 있습니다.

`.github/workflows/deploy-feature-server-api.yml` 도 있습니다.
`feature/server-api` 에 push 하면 도는데, 쓰려면 리포 시크릿
(`DEPLOY_HOST`, `DEPLOY_PORT`, `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS`)이 있어야 합니다.
