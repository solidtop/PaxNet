using System.Buffers;
using System.Buffers.Binary;

namespace LogicalPacket.Core;

internal readonly struct Packet : IDisposable
{
    private readonly IMemoryOwner<byte>? _bufferOwner;
    private readonly Memory<byte> _data;

    public Packet(IMemoryOwner<byte> bufferOwner, int size)
    {
        _bufferOwner = bufferOwner;
        _data = bufferOwner.Memory[..size];
    }

    public Packet(PacketType type)
    {
        _data = new byte[GetHeaderSize(type)];
        Type = type;
    }

    public ReadOnlyMemory<byte> Data => _data;

    public PacketType Type
    {
        get => (PacketType)_data.Span[0];
        set => _data.Span[0] = (byte)value;
    }

    public ushort Sequence
    {
        get => BinaryPrimitives.ReadUInt16LittleEndian(_data.Span.Slice(1, 2));
        set => BinaryPrimitives.WriteUInt16LittleEndian(_data.Span.Slice(1, 2), value);
    }

    public ReadOnlyMemory<byte> Payload
    {
        get => _data[GetHeaderSize(Type)..];
        set => value.CopyTo(_data[GetHeaderSize(Type)..]);
    }

    public static int GetHeaderSize(PacketType type)
    {
        return type switch
        {
            _ => 1
        };
    }

    public void Dispose()
    {
        _bufferOwner?.Dispose();
    }
}

public enum PacketType : byte
{
    ConnectRequest,
    ConnectAccept,
    ConnectReject,
    Disconnect,
    Shutdown,
    Unreliable,
    Ping,
    Pong
}

internal struct ConnectionRequestPacket
{
    public const int HeaderSize = 12;

    public uint ConnectionNumber { get; set; }
    public long ConnectionTime { get; set; }
    public ReadOnlyMemory<byte> Payload { get; set; }

    public void Write(PacketWriter writer)
    {
        writer.WriteUInt32(ConnectionNumber);
        writer.WriteInt64(ConnectionTime);
    }

    public static ConnectionRequestPacket Parse(PacketReader reader)
    {
        return new ConnectionRequestPacket
        {
            ConnectionNumber = reader.ReadUInt32(),
            ConnectionTime = reader.ReadInt64()
        };
    }
}