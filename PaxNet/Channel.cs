using System.Collections.Concurrent;

namespace PaxNet;

public class ChannelOptions
{
    public int WindowSize { get; set; } = 32;
    public TimeSpan MinRetransmitTimeout { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxRetransmitTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

internal abstract class Channel(Connection connection, byte id, ChannelOptions options)
{
    protected readonly Connection Connection = connection;
    protected readonly byte Id = id;
    protected readonly ChannelOptions Options = options;
    protected readonly ConcurrentQueue<Packet> SendBuffer = [];

    public abstract void Send(ReadOnlySpan<byte> payload);
    public abstract void ProcessData(Packet packet);
    public abstract void ProcessAck(ushort sequence);
    public abstract void Update(TimeSpan elapsed);

    protected static bool IsNewer(ushort sequence, ushort expectedSequence)
    {
        return (ushort)(sequence - expectedSequence) < 0x8000;
    }

    public static Channel Create(Connection connection, byte id, ChannelOptions options)
    {
        return (DeliveryMethod)id switch
        {
            DeliveryMethod.Unreliable => new UnreliableChannel(connection, id, options),
            DeliveryMethod.UnreliableSequenced => new UnreliableSequencedChannel(connection, id, options),
            DeliveryMethod.Reliable => new ReliableChannel(connection, id, options),
            DeliveryMethod.ReliableSequenced => new ReliableSequencedChannel(connection, id, options),
            DeliveryMethod.ReliableOrdered => new ReliableOrderedChannel(connection, id, options),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };
    }
}

internal class UnreliableChannel(Connection connection, byte id, ChannelOptions options)
    : Channel(connection, id, options)
{
    public override void Send(ReadOnlySpan<byte> payload)
    {
        var packet = Packet.CreateChannel(Id, 0, payload);
        SendBuffer.Enqueue(packet);
    }

    public override void ProcessData(Packet packet)
    {
        Connection.OnInboundPacketReady(packet);
    }

    public override void ProcessAck(ushort sequence)
    {
        throw new NotImplementedException();
    }

    public override void Update(TimeSpan elapsed)
    {
        while (SendBuffer.TryDequeue(out var packet))
            Connection.OnOutboundPacketReady(packet);
    }
}

internal class UnreliableSequencedChannel(Connection connection, byte id, ChannelOptions options)
    : UnreliableChannel(connection, id, options)
{
    private ushort _nextSequence;
    private ushort _expectedSequence;

    public override void Send(ReadOnlySpan<byte> payload)
    {
        var packet = Packet.CreateChannel(Id, _nextSequence++, payload);
        SendBuffer.Enqueue(packet);
    }

    public override void ProcessData(Packet packet)
    {
        var sequence = packet.Sequence;

        if (IsNewer(sequence, _expectedSequence))
        {
            _expectedSequence = sequence;
            Connection.OnInboundPacketReady(packet);
        }
        else
        {
            packet.Dispose(); // Drop stale
        }
    }
}

internal class ReliableChannel(Connection connection, byte id, ChannelOptions options)
    : Channel(connection, id, options)
{
    private readonly ConcurrentDictionary<ushort, InFlight> _inFlightBuffer = [];

    protected readonly ConcurrentQueue<Packet> AckBuffer = [];
    private ushort _nextSequence;
    protected ushort ExpectedSequence;

    public override void Send(ReadOnlySpan<byte> payload)
    {
        if (_inFlightBuffer.Count >= Options.WindowSize)
            return;

        var packet = Packet.CreateChannel(Id, _nextSequence++, payload);
        SendBuffer.Enqueue(packet);
    }

    public override void ProcessData(Packet packet)
    {
        var ackPacket = Packet.CreateAck(packet.ChannelId, packet.Sequence);
        AckBuffer.Enqueue(ackPacket);
        Connection.OnInboundPacketReady(packet);
    }

    public override void ProcessAck(ushort sequence)
    {
        if (_inFlightBuffer.Remove(sequence, out var inFlight))
            inFlight.Packet.Dispose();
    }

    public override void Update(TimeSpan elapsed)
    {
        FlushAcks();
        FlushTransmits(elapsed);
        FlushRetransmits(elapsed);
    }

    private void FlushAcks()
    {
        while (AckBuffer.TryDequeue(out var packet))
            Connection.OnOutboundPacketReady(packet);
    }

    private void FlushTransmits(TimeSpan elapsed)
    {
        var windowSize = Options.WindowSize;
        var minRto = Options.MinRetransmitTimeout;

        while (SendBuffer.TryPeek(out var packet) && _inFlightBuffer.Count < windowSize)
        {
            if (!SendBuffer.TryDequeue(out packet))
                continue;

            var inFlight = new InFlight(packet)
            {
                LastSent = elapsed,
                RetransmitAt = elapsed + minRto
            };

            _inFlightBuffer.TryAdd(packet.Sequence, inFlight);
            Connection.OnOutboundPacketReady(packet);
        }
    }

    private void FlushRetransmits(TimeSpan elapsed)
    {
        var minRto = Options.MinRetransmitTimeout;
        var maxRto = Options.MaxRetransmitTimeout;

        foreach (var inFlight in _inFlightBuffer.Values.Where(inFlight => elapsed >= inFlight.RetransmitAt))
        {
            inFlight.LastSent = elapsed;
            inFlight.RetransmitCount++;

            var rto = minRto * (1 << inFlight.RetransmitCount);

            if (rto > maxRto)
                rto = maxRto;

            inFlight.RetransmitAt = elapsed + rto;
            Connection.OnOutboundPacketReady(inFlight.Packet);
        }
    }
}

internal class ReliableSequencedChannel(Connection connection, byte id, ChannelOptions options)
    : ReliableChannel(connection, id, options)
{
    public override void ProcessData(Packet packet)
    {
        var sequence = packet.Sequence;

        if (IsNewer(sequence, ExpectedSequence))
        {
            ExpectedSequence = sequence;
            Connection.OnInboundPacketReady(packet);
        }
        else
        {
            packet.Dispose();
        }
    }
}

internal class ReliableOrderedChannel(Connection connection, byte id, ChannelOptions options)
    : ReliableChannel(connection, id, options)
{
    private readonly ConcurrentDictionary<ushort, Packet> _reorderBuffer = [];

    public override void ProcessData(Packet packet)
    {
        var sequence = packet.Sequence;
        AckBuffer.Enqueue(Packet.CreateAck(Id, sequence));

        if (sequence == ExpectedSequence)
        {
            ExpectedSequence++;
            Connection.OnInboundPacketReady(packet);
            FlushReorders();
        }
        else if (IsNewer(sequence, ExpectedSequence))
        {
            if (!_reorderBuffer.TryAdd(sequence, packet))
                packet.Dispose();
        }
        else // Duplicate or old packet, drop it 
        {
            packet.Dispose();
        }
    }

    private void FlushReorders()
    {
        while (_reorderBuffer.TryRemove(ExpectedSequence, out var packet))
        {
            ExpectedSequence++;
            Connection.OnInboundPacketReady(packet);
        }
    }
}

internal class InFlight(Packet packet)
{
    public Packet Packet => packet;
    public TimeSpan LastSent { get; set; }
    public TimeSpan RetransmitAt { get; set; }
    public int RetransmitCount { get; set; }
}