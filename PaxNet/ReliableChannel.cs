using System.Collections.Concurrent;

namespace PaxNet;

internal sealed class ReliableChannel(byte id) : Channel(id)
{
    private const int WindowSize = 32;

    private readonly ConcurrentQueue<Packet> _sendQueue = [];
    private readonly ConcurrentQueue<Packet> _ackQueue = [];
    private readonly ConcurrentDictionary<ushort, Packet> _reorderBuffer = [];
    private readonly ConcurrentDictionary<ushort, InFlight> _inFlight = [];

    private ushort _nextSequence;
    private ushort _expectedSequence;

    public override void Send(ReadOnlySpan<byte> payload)
    {
        if (_inFlight.Count >= WindowSize) Console.WriteLine("window full");

        var packet = Packet.CreateData(PacketFlags.Reliable, payload, Id, _nextSequence++);
        _sendQueue.Enqueue(packet);
    }

    public override void Process(DateTime now, Action<Packet> deliver)
    {
        while (_ackQueue.TryDequeue(out var packet)) deliver(packet);

        while (_sendQueue.TryPeek(out var packet) && _inFlight.Count < WindowSize)
        {
            _sendQueue.TryDequeue(out packet);
            var inFlight = new InFlight(packet, now, TimeSpan.FromMilliseconds(250));
            _inFlight.TryAdd(packet.Sequence, inFlight);
            deliver(packet);
        }

        foreach (var pending in _inFlight.Values)
            if (now >= pending.RetransmitAt)
            {
                Console.WriteLine($"[CHANNEL] Retransmitting {pending.Packet.Sequence}");
                pending.LastSent = now;
                pending.Rto = TimeSpan.FromMilliseconds(Math.Min(pending.Rto.TotalMilliseconds * 2, 5000));
                pending.RetransmitAt = now + pending.Rto;
                deliver(pending.Packet);
            }
    }

    public override void Receive(Packet packet, Action<Packet> deliver)
    {
        var sequence = packet.Sequence;
        var ackPacket = Packet.CreateAck(Id, sequence);
        _ackQueue.Enqueue(ackPacket);

        // If it matches
        if (sequence == _expectedSequence)
        {
            _expectedSequence++; // Advance expectedSequence
            deliver(packet);
            DrainReorderBuffer(deliver);
        }
        else if (IsNewer(sequence, _expectedSequence))
        {
            if (!_reorderBuffer.TryAdd(sequence, packet))
                packet.Dispose();
        }
        else // Duplicate or old packet, drop it
        {
            packet.Dispose();
        }
    }

    public override void ReceiveAck(ushort sequence)
    {
        if (_inFlight.TryRemove(sequence, out var pending))
            pending.Packet.Dispose();
    }

    private void DrainReorderBuffer(Action<Packet> deliver)
    {
        while (_reorderBuffer.TryRemove(_nextSequence, out var packet))
        {
            _expectedSequence++;
            deliver(packet);
        }
    }

    private static bool IsNewer(ushort sequence, ushort expectedSequence)
    {
        return (ushort)(sequence - expectedSequence) < 0x8000;
    }
}

internal sealed class InFlight(Packet packet, DateTime lastSent, TimeSpan rto)
{
    public Packet Packet { get; set; } = packet;
    public DateTime LastSent { get; set; } = lastSent;
    public TimeSpan Rto { get; set; } = rto;
    public DateTime RetransmitAt { get; set; } = lastSent + rto;
}