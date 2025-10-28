namespace PaxNet;

internal abstract class Channel(byte id)
{
    public byte Id => id;

    public abstract void Send(ReadOnlySpan<byte> payload);
    public abstract void Receive(Packet packet, Action<Packet> deliver);
    public abstract void ReceiveAck(ushort sequence);
    public abstract void Process(DateTime now, Action<Packet> callback);

    public static Channel Create(byte channelId, Delivery delivery)
    {
        return delivery switch
        {
            Delivery.Reliable => new ReliableChannel(channelId),
            _ => new UnreliableChannel(channelId)
        };
    }

    public static Channel Create(byte channelId, PacketFlags flags)
    {
        return flags switch
        {
            PacketFlags.Reliable => new ReliableChannel(channelId),
            _ => new UnreliableChannel(channelId)
        };
    }
}