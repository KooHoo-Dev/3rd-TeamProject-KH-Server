using System.Text.Json;

namespace HelloServer;

public sealed class PacketDispatcher
{
    private readonly Dictionary<string, IPacketHandler> handlers =
        new Dictionary<string, IPacketHandler>(StringComparer.Ordinal);

    public PacketDispatcher(IEnumerable<IPacketHandler> packetHandlers)
    {
        foreach (IPacketHandler handler in packetHandlers)
        {
            foreach (string type in handler.Types)
            {
                if (handlers.TryAdd(type, handler) == false)
                    throw new InvalidOperationException($"패킷 Type이 중복 등록됨: {type}");
            }
        }
    }

    public Task DispatchAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        PacketHeader header = JsonSerializer.Deserialize<PacketHeader>(json);
        if (string.IsNullOrWhiteSpace(header?.Type))
            return Task.CompletedTask;

        if (handlers.TryGetValue(header.Type, out IPacketHandler handler) == false)
            return Task.CompletedTask;

        return handler.HandleAsync(context, json, token);
    }
}
