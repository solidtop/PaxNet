using System.Threading.Channels;

namespace PaxNet.Core;

internal sealed class UnreliableChannel(Server server, Peer peer, int capacity)
{
    private readonly Channel<Packet> _channel = Channel.CreateBounded<Packet>(new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    public bool TryWrite(Packet packet)
    {
        if (_channel.Writer.TryWrite(packet)) return true;

        if (_channel.Reader.TryRead(out var evicted))
        {
            evicted.Dispose();

            if (_channel.Writer.TryWrite(packet)) return true;
        }

        packet.Dispose();
        return false;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in _channel.Reader.ReadAllAsync(cancellationToken))
                await server.SendAsync(packet, peer, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (ChannelClosedException)
        {
            // Normal shutdown
        }
    }

    public void Complete()
    {
        _channel.Writer.Complete();
    }
}