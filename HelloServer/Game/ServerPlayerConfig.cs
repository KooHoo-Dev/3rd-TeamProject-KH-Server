using System.Text.Json;

namespace HelloServer;

/// <summary>모든 플레이어의 서버 권위 초기 체력 설정입니다.</summary>
public sealed class ServerPlayerConfig
{
    public int InitialHealth { get; set; }
    public int MaxHealth { get; set; }

    public static ServerPlayerConfig Load(string path)
    {
        ServerPlayerConfig config = JsonSerializer.Deserialize<ServerPlayerConfig>(
            File.ReadAllText(path));

        if (config == null || config.MaxHealth <= 0 ||
            config.InitialHealth < 0 || config.InitialHealth > config.MaxHealth)
            throw new InvalidDataException($"플레이어 체력 설정이 올바르지 않습니다: {path}");

        return config;
    }
}
