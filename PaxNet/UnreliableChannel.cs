using System.Collections.Concurrent;

namespace PaxNet;

internal sealed class UnreliableChannel(byte id) : Channel(id)
{
    private readonly ConcurrentQueue<Packet> _sendQueue = [];

    public override void Send(ReadOnlySpan<byte> payload)
    {
        var packet = Packet.CreateData(PacketFlags.None, payload, Id);
        _sendQueue.Enqueue(packet);
    }

    public override void Receive(Packet packet, Action<Packet> deliver)
    {
        deliver(packet);
    }

    public override void ReceiveAck(ushort sequence)
    {
        throw new NotImplementedException();
    }

    public override void Process(DateTime now, Action<Packet> callback)
    {
        while (_sendQueue.TryDequeue(out var packet)) callback(packet);
    }
}