namespace HelloServer;

public interface IPacketHandler
{
    IReadOnlyCollection<string> Types { get; }

    Task HandleAsync(PacketContext context, string json, CancellationToken token);
}
