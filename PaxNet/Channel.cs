using System.Collections.Concurrent;

namespace PaxNet;

internal abstract class Channel(byte id)
{
    protected readonly ConcurrentQueue<Packet> InboundQueue = [];
    protected readonly ConcurrentQueue<Packet> OutboundQueue = [];

    public byte Id => id;

    public void EnqueueInbound(Packet packet)
    {
        InboundQueue.Enqueue(packet);
    }

    public void EnqueueOutbound(ReadOnlySpan<byte> data, Delivery delivery)
    {
        var flags = delivery switch
        {
            _ => PacketFlags.None
        };

        var packet = Packet.CreateData(flags, data);
        OutboundQueue.Enqueue(packet);
    }

    public static Channel Create(byte channelId, Delivery delivery)
    {
        return delivery switch
        {
            _ => new UnreliableChannel(channelId)
        };
    }
}