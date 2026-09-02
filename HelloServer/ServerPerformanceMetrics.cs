using System.Diagnostics;

namespace HelloServer;

// 개발 환경 설정 기반 선택적 경량 계측 기능
public static class ServerPerformanceMetrics
{
    public static bool Enabled { get; set; }

    public static long Timestamp() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void Write(string name, long startedAt, string suffix = "")
    {
        if (!Enabled) return;
        double milliseconds = (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
        Console.WriteLine($"[Perf] {name}Ms={milliseconds:F2}{suffix}");
    }
}
