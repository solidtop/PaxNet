using System.Buffers;
using System.Text;

namespace PaxNet;

internal readonly struct Packet(IMemoryOwner<byte> bufferOwner, int size) : IDisposable
{
    public int Size => size;
    public Span<byte> Data => bufferOwner.Memory.Span[..Size];
    public PacketType Type => (PacketType)Data[0];
    public PacketFlags Flags => (PacketFlags)Data[1];
    public Span<byte> Payload => Data[HeaderSize..];

    public PacketReader Reader => new(Payload);
    public PacketWriter Writer => new(Payload);

    public void Dispose()
    {
        bufferOwner.Dispose();
    }

    public static Packet CreateData(PacketFlags flags, ReadOnlySpan<byte> payload)
    {
        const int headerSize = 2;
        var totalSize = headerSize + payload.Length;
        var bufferOwner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = bufferOwner.Memory.Span;

        span[0] = (byte)PacketType.Data;
        span[1] = (byte)flags;

        payload.CopyTo(span[headerSize..]);

        return new Packet(bufferOwner, totalSize);
    }

    public static Packet Create(PacketType type, int payloadSize = 0)
    {
        const int headerSize = 1;
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

    private int HeaderSize => Type switch
    {
        PacketType.Data => 2,
        _ => 1
    };
}

internal enum PacketType : byte
{
    ConnectRequest,
    ConnectAccept,
    ConnectReject,
    Disconnect,
    Ping,
    Pong,
    Data
}

[Flags]
internal enum PacketFlags : byte
{
    None = 0,
    Reliable = 1 >> 0,
    Ordered = 1 << 1,
    Sequenced = 1 << 2
}