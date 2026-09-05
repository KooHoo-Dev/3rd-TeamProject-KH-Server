using System.Text.Json;

namespace HelloServer;

/// <summary>한 게임 세션에 공통으로 적용할 진행 및 종료 설정입니다.</summary>
public sealed class ServerGameConfig
{
    public int GameDurationSeconds { get; set; }
    public int VictoryGold { get; set; }

    public static ServerGameConfig Load(string path)
    {
        ServerGameConfig config = JsonSerializer.Deserialize<ServerGameConfig>(
            File.ReadAllText(path));

        if (config == null || config.GameDurationSeconds <= 0 || config.VictoryGold <= 0)
            throw new InvalidDataException($"게임 진행 설정이 올바르지 않습니다: {path}");

        return config;
    }
}
