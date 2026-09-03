# 서버 API

유니티 클라이언트와 서버가 주고받는 것 전부.

> **이 문서의 JSON 은 지어낸 것이 아니라 실제로 서버에 붙어서 받아 적은 것이다.**
> 계약을 고치면 이 문서도 함께 고쳐야 한다.

서버를 띄우고 배포하는 법은 [`README.md`](README.md) 와
[`학생용. 서버 접속과 배포.md`](학생용.%20서버%20접속과%20배포.md) 에 있다.

---

## 1. 연결

| | |
|---|---|
| `GET /ping` | 살아 있으면 `pong` 이라는 글자를 돌려준다. 그게 전부다 |
| `WS /room?code={방코드}` | 게임이 오가는 유일한 통로 |

검사는 **웹소켓 여부가 먼저다.** 웹소켓이 아닌 요청으로 `/room` 에 오면
방코드가 무엇이든 `400 웹소켓으로 접속하시오` 다.
브라우저 주소창으로는 방코드 오류를 볼 수 없다.

웹소켓으로 붙었으면 그때 방코드를 본다. 앞뒤 공백을 떼고 **대문자로 바꾼 뒤**
글자·숫자만 통과한다. 아니면 `400 방코드 해석 불가능` 이 돌아오고 소켓은 열리지 않는다.

> 유니코드 기준(`char.IsLetterOrDigit`)이라 `방1` 같은 한글 방코드도 그대로 통과한다.
> 막히는 것은 공백과 특수문자뿐이다.
>
> 대문자로 바꿀 때 `ToUpperInvariant` 가 아니라 `ToUpper` 를 쓴다(`RoomHub.Normalize`).
> 터키어 같은 문화권에서 서버가 돌면 `i` 가 `İ` 로 올라가 클라이언트가 보낸 `I` 와
> 다른 방이 조용히 만들어진다. 한국어·영어 환경에서는 차이가 없다.

접속한 사람에게는 서버가 `u1`, `u2` … 하는 번호를 붙인다.
**서버 프로세스 하나에서 이어지는 번호**라 방이 달라도 겹치지 않고, 다시 쓰이지도 않는다.

방은 처음 들어온 사람이 만들고 마지막 사람이 나가면 사라진다.
**로그인도 비밀번호도 없다. 다시 붙기(재접속)도 없다.** 끊기면 그 사람은 그 판에서 끝이다.

### 들어가는 순서

**첫 메시지는 반드시 `hello` 여야 한다.**

```
클라 → 서버   {"Type":"hello","NickName":"진호"}

서버 → 클라   welcome              내 번호와 이미 있던 사람들
서버 → 클라   map_session          지형 시드
서버 → 클라   world_item.snapshot  바닥에 떨어져 있는 것
서버 → 클라   inventory.snapshot   내 가방
```

이 네 개는 **한 덩어리로 나에게만** 온다. 그다음부터 방송을 받는다.

`hello` 가 아니거나 닉네임이 비어 있으면 **연결이 그냥 끊긴다.**
오류 메시지도 close 프레임도 안 간다. 서버 콘솔에만 이유가 찍힌다.

> 이 네 개를 기다리지 않고 바로 다음 메시지를 보내도 된다.
> 서버는 입장 처리를 끝낸 뒤에 그것을 읽으므로 순서가 뒤집히지 않는다.
> 다만 `welcome` 을 받기 전에는 내 번호를 모르고 `map_session` 을 받기 전에는 지형이 없어서,
> 지금 클라이언트는 `welcome` 을 받은 뒤부터 보낸다.

---

## 2. 공통 규칙

모든 메시지는 UTF-8 JSON 한 덩어리이고 `Type` 이라는 글자를 가진다.

```json
{ "Type": "terrain_excavate", "RequestId": "dig-7", ... }
```

| | |
|---|---|
| `Type` | 무슨 메시지인지. **대소문자를 가린다** |
| `RequestId` | 넣으면 그 응답에 그대로 담겨 돌아온다. 안 넣으면 응답에서 아예 빠진다 |

이름은 `PascalCase` 이고 **칸 이름도 대소문자를 가린다.**
`nickname` 이나 `x` 로 보내면 오류 없이 조용히 빈 값(`null`, `0`)이 들어간다.
모르는 칸이 들어오면 조용히 무시하므로 서버가 모르는 칸을 보내도 안 깨진다.

> 웹소켓 경로에는 `JsonSerializerOptions` 를 아무것도 안 준다.
> `Program.cs` 의 `ConfigureHttpJsonOptions` 는 HTTP 응답용이라 여기에는 안 걸린다.

### `RequestId` 는 나만 보는 것이 아니다

지형 변경 같은 방송에도 요청자의 `RequestId` 가 그대로 실린다. 그 방송은 **방 사람 전부**가 받는다.

```json
{"Batch":{...},"Type":"terrain_batch","RequestId":"dig0"}
```

받는 쪽은 이걸로 "이게 내가 부탁한 것인지"를 가린다.

굴착과 낙하의 `RequestId` 는 `excavate-{인스턴스ID}-{번호}` 처럼 가운데에 GUID 가 들어가 남과 겹치지 않는다.
**하지만 줍기는 다르다.** 지금 클라이언트는 `pickup-1` 처럼 번호만 붙여 보낸다.
두 사람이 각각 첫 줍기를 하면 둘 다 `pickup-1` 이다.
**`world_item.removed` 는 `RequestId` 가 아니라 `DropID` 로 가려라.**

### 순서

방마다 방송 큐가 하나 있어서 **방송끼리는 순서가 뒤집히지 않는다.**

> 큐를 타는 것은 방송뿐이다. 한 사람에게만 가는 `error` 와 줍기 응답 `inventory.snapshot` 은
> 큐를 건너뛰고 바로 나간다. 그래서 주운 사람은 `world_item.removed` 보다
> `inventory.snapshot` 을 먼저 받을 수 있고, `error` 가 먼저 쌓인 `terrain_batch` 를 앞지를 수 있다.

지형 메시지에는 `BaseRevision` 과 `ResultRevision` 이 붙는다.
"이 변경은 몇 번에서 몇 번으로 간다"는 뜻이고, 클라이언트는 **번호가 이어질 때만** 적용한다.
번호는 방에 하나뿐이고 지형이 바뀔 때마다 1씩 오른다.

### 번호가 안 이어지면

클라이언트는 그 Batch 를 **버퍼에 재워 두고 앞 번호를 기다린다.**
앞 번호가 오면 쌓아 둔 것을 차례로 푼다.

> **앞 번호가 끝내 안 오면 낫지 않는다.** 서버에 지형을 다시 달라고 할 방법이 없다.
> 다시 붙기도 없으니 그 사람의 지형은 그 판이 끝날 때까지 어긋난 채로 남는다.
> `[RoomClient] Terrain batch buffered` 경고가 계속 찍히면 그 상태다.
> 그래서 `terrain_batch` 는 순서대로 하나도 빠짐없이 도착해야 하고, 방송 큐가 그것을 지킨다.

### 두 사람이 같은 칸을 동시에 건드리면

방마다 지형 자물쇠가 하나 있어서 **한 번에 한 요청만 처리된다.**
먼저 들어온 쪽이 이기고 `Revision` 이 1 오른다. 진 쪽은 바뀐 뒤의 상태를 보고 거절당한다 —
남이 먼저 팠으면 `terrain.empty_cell`, 남이 먼저 예약했으면 `terrain.collapse_pending` 이다.
같은 아이템을 동시에 주우면 늦은 쪽이 `item.not_found` 를 받는다.

> **클라이언트는 요청을 보낸 뒤 방송이 올 때까지 그 칸을 미리 지우지 마라.**

### 모르는 `Type` 을 보내면

**아무 일도 일어나지 않는다.** 오류도 안 온다.
서버 콘솔에 종류마다 딱 한 번 찍힌다.

```
[방코드] 모르는 메시지 종류 : player.jump  (PacketHandlerRegistry 에 핸들러를 등록했는지 보세요)
```

이 기억은 **방마다 따로다.** 다른 방에서 같은 것을 보내면 또 찍히고,
방이 비어 사라졌다 다시 열려도 또 찍힌다. `Type` 칸이 아예 없으면 `(Type 없음)` 으로 찍힌다.

### JSON 이 깨졌거나 칸의 형이 다르면 — **연결이 끊긴다**

모르는 `Type` 은 무시되지만, JSON 자체가 깨졌거나 `X` 자리에 숫자 대신 글자가 들어오면
(`{"Type":"player.move","X":"abc"}`) 서버가 그 자리에서 예외를 내고 그 사람의 소켓을 놓는다.
오류 메시지도 close 프레임도 안 간다. 서버 콘솔에 예외가 통째로 찍히니 그것으로 안다.

### 메시지 크기

**제한이 없다.** 서버는 4096바이트 조각으로 받아서 끝 조각이 올 때까지 이어 붙인다.
그래서 4096바이트가 넘는 메시지도 정상으로 도착한다.
다만 상한이 없어서 아주 큰 메시지 하나가 그대로 서버 메모리에 쌓인다.

> **한 메시지가 4096바이트를 넘으면 한글 한 글자가 깨질 수 있다.**
> 조각마다 따로 UTF-8 로 푸는 탓에(`Room.ReceiveTextAsync`) 글자가 조각 경계에 걸치면
> 그 글자만 `?` 가 된다. 한글은 한 글자에 3바이트다. 클라이언트 수신부도 같은 모양이다.
> 긴 한글 채팅은 아직 안전하지 않다.

---

## 3. 클라 → 서버 (9종)

### `hello` — 입장

| 칸 | 형 | |
|---|---|---|
| `NickName` | string | 앞뒤 공백은 떼어 낸다. 비면 연결이 끊긴다 |

```json
{"Type":"hello","NickName":"진호"}
```

> **이 한 종류만 `PacketDispatcher` 를 거치지 않는다.** `Room.JoinAsync` 가 첫 메시지를
> 직접 읽어 처리하므로 `PacketHandlerRegistry` 에 `hello` 는 없다 — 8장의 「네 곳」 규칙에서
> 빠지는 유일한 예외다. `HelloPacketHandler.cs` 를 찾지 마라. 없다.
> 그래서 입장한 뒤에 `hello` 를 또 보내면 콘솔에 `모르는 메시지 종류 : hello` 만 찍힌다.

### `player.move` — 내 위치 알리기

옛 이름 **`move`** 도 받는다.

| 칸 | 형 | |
|---|---|---|
| `X`, `Y` | float | 월드 좌표. 칸 번호가 아니다 |
| `Id` | string | 서버가 읽지 않는다. 누가 보냈는지는 소켓으로 안다 |

```json
{"Type":"player.move","X":0.5,"Y":-40.5}
```

서버는 그냥 받아 적는다. **속도나 벽을 검사하지 않는다.**
값이 `NaN` 이나 무한이면 버린다.

### `chat.send` — 채팅

옛 이름 **`chat`** 도 받는다.

| 칸 | 형 | |
|---|---|---|
| `Text` | string | 앞뒤 공백을 떼어 콘솔에 찍는다 |
| `Id`, `NickName` | string | **클라이언트가 채워야 한다** (아래 참고) |

> **서버는 보낸 사람을 채워 주지 않는다.** 받은 것을 그대로 방 전체에 되뿌린다.
> `Id` 와 `NickName` 을 안 넣으면 받는 쪽에 `null` 로 간다.
> 그리고 아무 이름이나 적을 수 있다. 지금은 그런 상태다.
>
> 되뿌릴 때 `Type` 도 **보낸 쪽이 쓴 값 그대로** 나간다.
> `chat.send` 로 보내면 `chat.send` 로, `chat` 으로 보내면 `chat` 으로 간다.
>
> 지금 유니티 클라이언트는 채팅을 아예 보내지 않는다. 만들 사람이 위 규칙을 지켜야 한다.

### `terrain_excavate` — 칸 하나 파기

| 칸 | 형 | |
|---|---|---|
| `TargetCell` | `{X,Y}` | 팔 칸 번호 |
| `ItemID` | int | 곡괭이 아이템 번호. **서버에 장착된 것과 같아야 한다** |
| `ClientRequestID`, `DamageAmount` | — | **서버 계약에 없는 칸이다.** 클라이언트가 아직 보내지만 서버 클래스에 아예 없어서 조용히 버려진다. 요청과 응답을 잇는 것은 `RequestId` 다 |

```json
{"Type":"terrain_excavate","RequestId":"dig-7","TargetCell":{"X":45,"Y":60},"ItemID":2}
```

서버가 확인하는 것, **이 순서다.**

1. `ItemID` 가 0 보다 큰지
2. 그 칸에 낙하 예약이 걸렸는지
3. 서버에 내 상태가 있는지
4. 곡괭이가 서버에 장착된 것과 같은지
5. 사거리 안인지
6. 그 칸에 지형이 있는지
7. 팔 수 있는 지형인지

**앞에서 걸리면 뒤는 보지 않는다.** 어떤 `Code` 가 오는지는 5장의 표와 이 순서가 같다.
낙하 예약이 곡괭이 검사보다 **먼저**라는 점을 놓치기 쉽다.

되면 **`terrain_batch` 가 방 전체에** 나가고, 부서진 칸에 자원이 있었으면
`world_item.spawned` 도 함께 나간다. 안 되면 보낸 사람에게만 `error` 가 간다.

### `terrain_collapse_start` — 낙하 시작 알리기

| 칸 | 형 | |
|---|---|---|
| `SourceCells` | `[{X,Y}]` | 떨어질 칸들. 비면 안 되고, 서로 겹치면 거절 |

서버는 그 칸들을 **예약**해 둔다. 예약된 칸은 아무도 못 판다.
지형은 아직 바뀌지 않는다. 되면 `terrain_collapse_started` 가 방 전체에 나간다.

### `terrain_collapse_place` — 낙하가 끝난 자리 확정

| 칸 | 형 | |
|---|---|---|
| `CollapseID` | long | `terrain_collapse_started` 로 받은 번호. 0 보다 커야 한다 |
| `SourceCells` | `[{X,Y}]` | 시작할 때 준 것과 **같아야 한다** |
| `Changes` | `[셀변경]` | 떨어진 뒤 자리. 개수가 `SourceCells` 와 같아야 한다 |

> **떨어지는 계산은 클라이언트가 한다.** 서버는 물리를 돌리지 않는다.
> 낙하를 시작한 사람만 이 메시지를 보낸다.
>
> 서버가 확인하는 것 : 그 사람이 맞는지 · `CollapseID` 와 개수가 맞는지 ·
> 좌표가 겹치지 않는지 · 원본 칸이 시작할 때와 같고 아직 서버에 남아 있는지 ·
> 원본이 기반암이 아닌지 · **놓으려는 타일이 빈 칸이나 기반암이 아닌지** ·
> 맵 밖으로 나가지 않는지 · 놓을 자리에 이미 다른 지형이 있지 않은지.
>
> 빈 칸(`TileTypeID` 0)으로는 낙하를 마칠 수 없다. 이것이 걸리기 쉽다.

### `world_item.pickup` — 바닥 아이템 줍기

| 칸 | 형 | |
|---|---|---|
| `DropID` | string | `world_item.spawned` 로 받은 번호 |
| `X`, `Y` | float | 아이템이 지금 있다고 클라이언트가 주장하는 월드 좌표 |

주운 사람에게 `inventory.snapshot`, 방 전체에 `world_item.removed` 가 간다.

> 서버는 그 좌표가 `NaN`/무한이 아닌지, 맵 안인지, **그 사람에게서 3 안인지**만 본다
> (칸 크기가 1이라 3칸과 같다).
>
> **그 좌표가 드롭이 실제로 놓인 자리와 맞는지는 보지 않는다.**
> 오히려 서버가 가진 드롭 좌표를 클라이언트가 보낸 값으로 덮어쓴 뒤 지운다.
> 맵 반대편에 있는 드롭이라도 내 옆 좌표를 적어 보내면 주워진다. 지금은 그런 상태다.

---

## 4. 서버 → 클라 (14종)

### 입장할 때 (나에게만)

**`welcome`**
```json
{"RoomCode":"APIDOC","User":{"Id":"u1","NickName":"가"},"Users":[],"Type":"welcome"}
```
`User` 는 나, `Users` 는 **내가 오기 전부터 있던 사람들**이다. 나는 안 들어 있다.

**`map_session`** — 지형은 이것만 온다
```json
{"Session":{"MapSessionID":"APIDOC-b27b47de…","ProfileID":"Default",
 "Seed":1344477442,"TerrainDataVersion":"481da05fc3be"},"Type":"map_session"}
```

| 칸 | |
|---|---|
| `Seed` | 이 시드로 클라이언트가 지형을 **직접 만든다** |
| `ProfileID` | 지형 표에서 쓸 프로필. 지금은 언제나 `Default` |
| `TerrainDataVersion` | `Data/Terrain` 의 tsv 를 이름순으로 이어 붙여 낸 SHA-256 의 앞 6바이트(16진수 12글자). 지금은 표가 7개다. 표를 하나 더 넣기만 해도 값이 바뀐다. **지금 아무도 대조하지 않는다** |
| `MapSessionID` | `방코드-GUID`. 판을 구별하는 번호 |

**`world_item.snapshot`** — 바닥에 있는 것 전부
```json
{"Drops":[],"Type":"world_item.snapshot"}
```

**`inventory.snapshot`** — 내 가방 (아이템을 주웠을 때도 온다)
```json
{"PlayerID":"u1","Items":[],"Type":"inventory.snapshot"}
```

### 사람이 오갈 때 (그 사람 빼고 전부)

```json
{"User":{"Id":"u2","NickName":"나"},"Type":"join"}
{"Id":"u2","Type":"leave"}
```

### 위치 (기본 초당 10번, 전부)

```json
{"States":[{"Id":"u1","X":0,"Y":0}],"Type":"state"}
```
방에 있는 **모든 사람**의 위치가 매번 통째로 온다. `Type` 은 `state` 다.
횟수는 `appsettings.json` 의 `Room:BroadcastPerSecond` 가 정한다. 기본값이 10 이다.

> 새로 들어온 사람의 `Id` 가 `join` 보다 `state` 에 먼저 실려 올 수 있다.
> 서버가 그 사람을 자리에 앉히는 것과 `join` 을 뿌리는 것 사이에 틈이 있기 때문이다.
> **`state` 에 처음 보는 `Id` 가 나와도 정상이다.** 받는 쪽이 감당해야 한다.

### 지형이 바뀔 때 (전부)

**`terrain_batch`**
```json
{"Batch":{"MapSessionID":"APIDOC-b27b47de…","CollapseID":0,
  "BaseRevision":0,"ResultRevision":1,
  "Changes":[{"Coord":{"X":45,"Y":70},"TileTypeID":0,"Durability":0,
              "ResourceID":0,"LootEntries":[]}]},
 "Type":"terrain_batch","RequestId":"dig0"}
```

| 칸 | |
|---|---|
| `CollapseID` | 굴착이면 `0`, 낙하 확정이면 그 번호 |
| `BaseRevision` → `ResultRevision` | 이 변경 전후의 지형 번호. **언제나 1 차이다** |
| `Changes[].TileTypeID` | `0` 이면 그 칸이 없어졌다는 뜻 |
| `Changes[].Durability` | 부분 채굴이면 남은 내구도 |
| `Changes[].LootEntries` | 그 칸이 품은 드롭. 보통 빈 배열 |

**`terrain_collapse_started`**
```json
{"CollapseID":1,"OwnerPlayerID":"u1","StartedRevision":30,
 "SourceCells":[{"X":43,"Y":60}],"Type":"terrain_collapse_started","RequestId":"cs43"}
```
`OwnerPlayerID` 가 나면 **내가 낙하 계산을 맡는다.** 아니면 보기만 한다.
`StartedRevision` 은 클라이언트가 쓰지 않는다.

> **이것만은 지형을 바꾸지 않는다.** 칸을 예약만 하고 `Revision` 도 올리지 않는다.
> 그래서 `BaseRevision`/`ResultRevision` 짝이 없고 `StartedRevision` 하나만 있다.

**`terrain_collapse_cancelled`** — 낙하가 없던 일이 됐다
```json
{"CollapseIDs":[7],"Type":"terrain_collapse_cancelled",
 "RestoreCells":[{"Coord":{"X":43,"Y":60},"TileTypeID":3,"Durability":3,
                  "ResourceID":0,"LootEntries":[]}]}
```
> 이 문서에서 **이 예시 하나만 실제로 받아 적은 것이 아니다.** 서버 클래스에서 유도했다.

**세 가지 경우에 온다.**

1. 낙하를 잡아 둔 사람이 **확정하지 않은 채 나갔다.** 그 사람이 잡아 둔 것이 한꺼번에 풀린다.
2. **낙하 확정이 거절됐다.** 거절이 곧 그 낙하의 끝이다 — 보낸 쪽은 다시 시도하지 않는다.
   그래서 거절한 사람에게 `error` 를 보내는 것과 **동시에** 방 전체에 이것을 보낸다.
3. **30초가 지나도록 확정도 취소도 오지 않았다.** 서버가 스스로 거둔다.
   클라이언트가 확정 요청을 스스로 버리거나, 덩어리가 만료되거나, 클라이언트가 얼어붙으면
   서버에는 아무것도 오지 않는다. 그런 고장은 시간으로만 알아챌 수 있다.

| 칸 | |
|---|---|
| `CollapseIDs` | 없던 일이 된 낙하 번호들 |
| `RestoreCells` | **되돌려 놓을 칸.** 지금 서버에 있는 그대로다 |

> **`RestoreCells` 를 왜 서버가 보내는가.**
> 클라이언트는 낙하가 시작될 때 원본 칸을 자기 화면에서 지우고, 그 내용을 떨어지는 덩어리가 들고 있다.
> 덩어리가 아직 떠 있으면 그것으로 되돌릴 수 있지만, **착지하면 덩어리는 곧바로 지워진다.**
> 확정 거절이 오는 시점이 바로 그 뒤다. 번호만 보내면 되돌릴 밑천이 없다.
> 서버에는 그 칸이 그대로 남아 있으므로(낙하 시작은 예약만 한다) 서버가 실어 보낸다.
>
> 받는 쪽은 그 번호의 덩어리가 있으면 지우고, `RestoreCells` 를 그대로 격자에 써 넣으면 된다.

> **이 두 가지 말고는 예약을 푸는 길이 없다. 시간 제한도 없다.**
> 시작해 놓고 `terrain_collapse_place` 를 아예 안 보내면 그 칸들은 그 방이 사라질 때까지
> 잠긴 채로 남고, 누가 파도 `terrain.collapse_pending` 이 돌아온다.

### 아이템 (전부)

```json
{"Drop":{"DropID":"d1","ItemID":101,"Quantity":1,"X":0.5,"Y":-52.5},
 "Type":"world_item.spawned","RequestId":"dig23"}

{"DropID":"d1","CollectedByPlayerID":"u1","Type":"world_item.removed","RequestId":"pick1"}
```

### 채팅 (전부)

```json
{"Id":null,"NickName":null,"Text":"안녕하세요","Type":"chat.send"}
```
위 3장의 경고를 보라. **`Id` 와 `NickName` 은 보낸 클라이언트가 채운 값 그대로다.**

### 오류 (보낸 사람에게만)

```json
{"Code":"terrain.empty_cell","Message":"대상 셀에 지형이 없습니다.",
 "Type":"error","RequestId":"dig8"}
```

> 나에게만 오는 것(`error`, 줍기 때의 `inventory.snapshot`)은 방송 큐를 거치지 않고 곧바로 나간다.
> 그래서 2장의 순서 보장은 방송끼리만이다.

---

## 5. 오류 코드

`Message` 는 사람이 읽으라고 있는 것이다. **갈래를 잡는 것은 `Code` 로 하라.**

읽기 전에 두 가지를 알아 두라.

> **① `Code` 하나가 여러 조건을 덮는다.**
> `terrain.collapse_invalid` 에는 일곱 가지, `terrain.collapse_conflict` 에는 세 가지,
> `terrain.invalid_pickaxe` 와 `item.invalid_position` 에는 각각 두 가지 조건이 걸려 있다.
> **어느 조건인지까지 알아야 하면 `Message` 를 봐야 한다.**
>
> **② 아래 표는 요청별로 나눴지만 `Code` 는 요청마다 다르지 않다.**
> `player.not_found` 는 굴착과 줍기 양쪽, `terrain.collapse_pending` 은 굴착과 낙하 시작 양쪽,
> `terrain.collapse_invalid` 과 `terrain.collapse_conflict` 는 낙하 시작과 낙하 확정 양쪽에서
> 똑같은 글자로 온다.
> **어느 요청의 답인지는 `Code` 가 아니라 `RequestId` 로 가려라.**

### 굴착 (`terrain_excavate`) — 검사 순서대로

| Code | 뜻 |
|---|---|
| `terrain.invalid_request` | `ItemID` 가 0 이하 |
| `terrain.collapse_pending` | 그 칸이 낙하 예약에 걸려 있다 |
| `player.not_found` | 서버에 내 상태가 없다 |
| `terrain.invalid_pickaxe` | 보낸 `ItemID` 가 서버에 장착된 곡괭이와 다르다 · **또는** 장착된 번호가 아이템 표에 없다 |
| `terrain.out_of_range` | 곡괭이 사거리 밖 |
| `terrain.empty_cell` | 그 칸에 지형이 없다 |
| `terrain.not_mineable` | 기반암처럼 못 파는 지형 |

### 낙하 시작 (`terrain_collapse_start`)

| Code | 뜻 |
|---|---|
| `terrain.collapse_invalid` | 보낸 `SourceCells` 가 비었다 · 좌표가 서로 겹친다 · 기반암이 섞였다 |
| `terrain.collapse_pending` | 이미 낙하 중인 칸이 섞였다 |
| `terrain.collapse_conflict` | 원본 칸이 서버에 없다 |

### 낙하 확정 (`terrain_collapse_place`)

| Code | 뜻 |
|---|---|
| `terrain.collapse_not_found` | 그 `CollapseID` 로 잡아 둔 것이 없다 |
| `terrain.collapse_not_owner` | 그 낙하를 시작한 사람이 아니다 |
| `terrain.collapse_invalid` | `CollapseID` 가 0 이하 · `SourceCells` 가 비었거나 `Changes` 와 개수가 다르다 · 좌표가 겹친다 · 원본 칸이 시작할 때와 다르다 · 원본 칸이 기반암이다 · 놓으려는 타일이 빈 칸이거나 기반암이다 |
| `terrain.collapse_conflict` | 원본 칸이 서버에서 사라졌다 · **놓으려는 자리에 이미 다른 지형이 있다** |
| `terrain.collapse_out_of_bounds` | 놓으려는 자리가 맵 밖이다 |

> 클라이언트는 `terrain.collapse_conflict` 하나만 특별히 다뤄서 다시 시도한다.
> 겹침 거절도 여기로 온다.

> **거절되면 그 낙하는 거기서 끝난다.** 서버가 예약을 풀고
> 방 전체에 `terrain_collapse_cancelled` 를 보낸다(4장). 오류는 보낸 사람에게만,
> 취소는 모두에게 간다 — 지운 화면을 되돌려야 하는 것은 모두이기 때문이다.
> 단 `collapse_not_found` 와 `collapse_not_owner` 는 예외다.
> 풀 예약이 없거나, 남의 예약이라 건드리면 안 된다.

### 아이템 (`world_item.pickup`) — 검사 순서대로

| Code | 뜻 |
|---|---|
| `item.invalid_request` | `DropID` 가 비었다 |
| `player.not_found` | 서버에 내 상태가 없다 |
| `item.not_found` | 이미 누가 주웠거나 없는 번호 |
| `item.invalid_position` | 좌표가 `NaN`/무한이다 · **또는** 맵 밖이다 |
| `item.out_of_range` | 내가 말한 좌표가 나에게서 3 밖이다 |
| `inventory.not_found` | 서버에 내 가방이 없다 |
| `inventory.overflow` | 수량이 `int` 한계를 넘는다 |

---

## 6. 지형은 서버가 보내지 않는다

서버는 `map_session` 에 **시드**만 보낸다. 클라이언트가 그 시드로 지형을 만든다.

예전에는 지형 전체를 보냈다 — 시드에 따라 470~490KB 쯤 된다(단단한 칸이 5,400~5,600개).
지금 `map_session` 은 **170바이트 안팎**이고, 방코드 길이와 시드 자릿수에 따라 몇 바이트 오르내린다.

되는 이유는 **양쪽이 같은 표를 같은 순서로 쓰는 같은 알고리즘**이기 때문이다.

```
서버 : HelloServer/Game/Terrain/ServerTerrainGenerator*.cs   (7개 전부)
클라 : Assets/02. Scripts/TerrainCollapse/Generation/
```

> **두 생성기는 한 몸이다. 한쪽만 고치면 그 방의 지형이 어긋난다.**
> 난수를 언제 몇 번 뽑는지, 격자를 어느 순서로 도는지, 조건을 어느 순서로 보는지가
> 전부 결과를 정한다. 보기 좋게 다듬는 것도 결과를 바꾼다.
>
> **표도 두 벌이다.** 같은 tsv 7개가 서버의 `HelloServer/Data/Terrain/` 과
> 클라이언트의 `Assets/StreamingAssets/Table/Terrain/` 에 따로 있다.
> 한쪽만 고치면 생성기를 안 건드려도 지형이 어긋난다.
> `TerrainDataVersion` 이 이것을 잡으라고 있는 값인데 지금 아무도 대조하지 않는다.
>
> 고쳤다면 지형 일치 검사를 돌려라. 검사 도구(`tools/terrain-parity/`)는 **리포에 올라가지 않는다**
> (`.gitignore` 의 `/tools/`). 선생님에게 받아서 `check-parity.bat` 안의 경로 세 줄을
> 네 컴퓨터 경로로 고친 뒤 실행하라. 두 생성기를 유니티와 ASP.NET 없이 떼어내
> 같은 시드로 돌리고 뽑힌 지형을 통째로 대조한다. 표도 함께 본다.

### 좌표 — **두 가지가 섞여 있으니 주의**

맵은 90 × 107 칸, 칸 크기 1.

| | |
|---|---|
| 원점 (−45, −100) | **월드 좌표**(float). 맵은 x −45~45, y −100~7 을 덮는다 |
| 스폰 구역 (35, 100) 에서 20 × 7 | **칸 번호**(int). 월드로는 (−10, 0) 에서 (10, 7) 이다 |

```
월드 = 원점 + (칸번호 + 0.5) × 칸크기
```

`player.move` 와 `world_item.pickup` 의 `X`/`Y` 는 월드 좌표,
`terrain_excavate` 의 `TargetCell` 과 낙하의 `SourceCells`/`Coord` 는 칸 번호다.

---

## 7. 알아 둘 것

**서버가 안 지키는 것.** 움직임은 그대로 받아 적는다 — 속도도 벽도 안 본다.
채팅의 보낸 사람도 안 채운다. 낙하 물리도 클라이언트가 계산한 것을 검사만 하고 받는다.
줍기는 드롭이 실제로 그 자리에 있는지 안 본다. 지금은 수업용이라 이렇게 되어 있다.

**다시 붙기가 없다.** 끊기면 그 사람의 상태는 지워진다. 같은 방에 다시 들어오면 새 사람이다.

**내구도가 지금 무시되고 있다.** 기본 장착이 `DebugPickAxe`(ID 2)이고 이 곡괭이의 위력이 100이다.
가장 단단한 심층암이 4이므로 **팔 수 있는 지형은** 무엇이든 한 방에 부서진다
(기반암은 애초에 못 판다).

> 부분 채굴을 보려면 `Data/Item/Items.tsv` 에서 `DebugPickAxe`(ID 2)의 `DigPower` 를 낮춰라.
> **ID 0 짜리 기본 곡괭이로 바꾸는 길은 막혀 있다** — `ItemID` 가 0 이하면
> `terrain.invalid_request` 로 먼저 튕기기 때문이다(`TerrainExcavationRequest.IsValid`).
> ID 가 0 보다 큰 곡괭이를 새로 넣는 것은 된다.
>
> **장착을 바꾸는 메시지도 없다.** `EquippedPickaxeItemID` 는 `Game/RoomState.cs` 의
> 기본값 2 가 전부이고 서버 어디에서도 대입하지 않는다. 코드를 고쳐 다시 빌드하는 수밖에 없다.

**사거리도 사실상 무제한이다.** 사거리는 곡괭이 표의 `PickaxeRange` 인데
지금 장착된 `DebugPickAxe` 는 100 이고 맵이 90 × 107 이다.
**`terrain.out_of_range` 는 지금 설정에서는 나오지 않는다.** 내구도와 같은 이유다.

**한 사람이 느리면 그 방 전체가 기다린다.** 방송은 방 사람 전부에게 보내고 나서 다음으로 넘어간다.
보내기 하나의 제한 시간이 3초라 대체로 3초까지 밀린다
(그 사람에게 나가던 개인 메시지 뒤에 줄을 서면 조금 더 걸릴 수 있다).

> **3초가 지나면 그 사람의 소켓을 서버가 끊어 버린다.** close 프레임도 오류도 안 간다.
> 에디터에서 중단점에 3초 넘게 서 있으면 그것만으로 튕긴다.
> 대신 끊긴 뒤로는 그 사람 때문에 더 밀리지 않는다.

**계측을 켜면 콘솔이 바쁘다.** 개발 설정에서는 방 하나가 가만히 있어도
`[Perf]` 가 초당 20줄쯤 찍힌다(방송 한 번에 2줄 × 초당 10번).
방이 늘거나 굴착이 잦으면 금세 초당 100줄을 넘는다. 운영 설정에서는 꺼져 있다.

---

## 8. 새 메시지 종류 추가하기

**네 곳을 고쳐야 한다. 하나라도 빠지면 동작하지 않는다.**

1. `Packets/PacketTypes.cs` 에 글자 상수
2. `Packets/Messages/` 의 알맞은 파일에 메시지 클래스 (`PacketHeader` 를 상속).
   **생성자에서 `Type` 을 채워라** — 안 채우면 `Type` 이 `null` 로 나가서
   클라이언트가 갈래를 못 찾는다.
3. `Packets/Handlers/` 에 `IPacketHandler` 구현.
   **핸들러는 JSON 문자열을 그대로 받는다.** 안에서 `JsonSerializer.Deserialize<내메시지>(json)`
   을 직접 불러야 하고, 여기서 예외가 나면 그 사람의 연결이 끊긴다(2장 참고).
4. **`Packets/Handlers/PacketHandlerRegistry.cs` 목록에 등록**

4번을 빠뜨리면 서버가 받고도 아무 일도 하지 않는다. 콘솔에 한 번 찍히니 그것으로 안다.
같은 `Type` 을 두 핸들러가 가져가면 방을 만들 때 예외가 난다.

> **`hello` 는 예외다.** 이 규칙을 따르지 않고 `Room.JoinAsync` 안에서 직접 처리한다(3장 참고).

클라이언트 쪽 짝도 필요하다 — `Assets/02. Scripts/Networking/` 의
`Contracts/`(메시지 모양)와 `RoomClient.RouteIncoming`(받는 갈래).

**글자 값은 클라이언트와 맞춰야 한다.** 지금 이름이 세 갈래로 섞여 있는데
(`hello`, `player.move`, `map_session`) 규칙이 아니라 자라난 흔적이다. 기존 것은 바꾸지 마라.
