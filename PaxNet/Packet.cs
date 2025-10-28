using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace PaxNet;

internal readonly struct Packet(IMemoryOwner<byte> bufferOwner, int size) : IDisposable
{
    public int Size => size;
    public Span<byte> Data => bufferOwner.Memory.Span[..Size];
    public PacketType Type => (PacketType)Data[0];
    public PacketFlags Flags => (PacketFlags)Data[1];
    public byte ChannelId => Data[2];
    public ushort Sequence => BinaryPrimitives.ReadUInt16LittleEndian(Data.Slice(3, 2));
    public Span<byte> Payload => Data[GetHeaderSize(Type)..];

    public PacketReader Reader => new(Payload);
    public PacketWriter Writer => new(Payload);

    public void Dispose()
    {
        bufferOwner.Dispose();
    }

    public static Packet CreateData(PacketFlags flags, ReadOnlySpan<byte> payload, byte channelId = 0,
        ushort sequence = 0)
    {
        var headerSize = GetHeaderSize(PacketType.Data);
        var totalSize = headerSize + payload.Length;
        var bufferOwner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = bufferOwner.Memory.Span;

        span[0] = (byte)PacketType.Data;
        span[1] = (byte)flags;
        span[2] = channelId;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(3, 2), sequence);

        payload.CopyTo(span[headerSize..]);

        return new Packet(bufferOwner, totalSize);
    }

    public static Packet Create(PacketType type, int payloadSize = 0)
    {
        var headerSize = GetHeaderSize(type);
        var totalSize = headerSize + payloadSize;
        var bufferOwner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = bufferOwner.Memory.Span;

        span[0] = (byte)type;

        return new Packet(bufferOwner, totalSize);
    }

    public static Packet CreateConnectRequest(string key)
    {
        var payloadSize = Encoding.UTF8.GetByteCount(key) + 2;
        var packet = Create(PacketType.ConnectRequest, payloadSize);
        packet.Writer.WriteString(key);

        return packet;
    }

    public static Packet CreateAck(byte channelId, ushort sequence)
    {
        var headerSize = GetHeaderSize(PacketType.Ack);
        var bufferOwner = MemoryPool<byte>.Shared.Rent(headerSize);
        var span = bufferOwner.Memory.Span;

        span[0] = (byte)PacketType.Ack;
        span[1] = (byte)PacketFlags.None;
        span[2] = channelId;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(3, 2), sequence);

        return new Packet(bufferOwner, headerSize);
    }

    private static int GetHeaderSize(PacketType type)
    {
        return type switch
        {
            PacketType.Data => 6,
            PacketType.Ack => 6,
            _ => 1
        };
    }
}

internal enum PacketType : byte
{
    ConnectRequest,
    ConnectAccept,
    ConnectReject,
    Disconnect,
    Ping,
    Pong,
    Data,
    Ack
}

[Flags]
internal enum PacketFlags : byte
{
    None = 0,
    Reliable = 1 >> 0,
    Ordered = 1 << 1,
    Sequenced = 1 << 2
}