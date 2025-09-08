using System.Buffers;
using System.Buffers.Binary;

namespace LogicalPacket.Core;

public readonly struct Packet : IDisposable
{
    private readonly IMemoryOwner<byte>? _bufferOwner;

    public readonly int Size;
    public readonly Memory<byte> Data;

    public Packet(IMemoryOwner<byte> bufferOwner, int size)
    {
        _bufferOwner = bufferOwner;

        Size = size;
        Data = bufferOwner.Memory[..size];
    }

    public Packet(PacketType type)
    {
        Size = GetHeaderSize(type);
        Data = new byte[Size];
        Type = type;
    }

    public PacketType Type
    {
        get => (PacketType)Data.Span[0];
        set => Data.Span[0] = (byte)value;
    }

    public ushort Sequence
    {
        get => BinaryPrimitives.ReadUInt16LittleEndian(Data.Span.Slice(1, 2));
        set => BinaryPrimitives.WriteUInt16LittleEndian(Data.Span.Slice(1, 2), value);
    }

    public static int GetHeaderSize(PacketType type)
    {
        return type switch
        {
            PacketType.Ping or PacketType.Pong => 1,
            _ => 3
        };
    }

    public void Dispose()
    {
        _bufferOwner?.Dispose();
    }
}

public enum PacketType : byte
{
    Connect,
    ConnectAccept,
    Disconnect,
    Data,
    Ping,
    Pong
}