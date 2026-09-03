using System.Text.Json.Serialization;

namespace HelloServer;

// 모든 메시지에서 Type을 먼저 읽고 선택적으로 요청을 연결할 수 있는 공통 헤더입니다.
public class PacketHeader
{
    public string Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string RequestId { get; set; }
}

public sealed class ErrorMessage : PacketHeader
{
    public ErrorMessage()
    {
        Type = PacketTypes.Error;
    }

    public string Code { get; set; }
    public string Message { get; set; }
}
