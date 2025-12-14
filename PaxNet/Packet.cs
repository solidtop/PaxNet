using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace PaxNet;

internal class Packet(byte[] buffer, int length) : IDisposable
{
    public const int HeaderLength = 4;

    public byte[] Buffer => buffer;
    public int Length => length;
    public bool IsEmpty => Length == 0;
    public ReadOnlySpan<byte> Data => Buffer.AsSpan(0, Length);
    public ReadOnlySpan<byte> Payload => Data[HeaderLength..];

    public PacketReader Reader => new(Payload);
    public PacketWriter Writer => new(Buffer, HeaderLength);

    public PacketType Type
    {
        get => (PacketType)Buffer[0];
        set => Buffer[0] = (byte)value;
    }

    public byte ChannelId
    {
        get => Buffer[1];
        set => Buffer[1] = value;
    }

    public ushort Sequence
    {
        get => BinaryPrimitives.ReadUInt16LittleEndian(Buffer.AsSpan(2, 2));
        set => BinaryPrimitives.WriteUInt16LittleEndian(Buffer.AsSpan(2, 2), value);
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(Buffer);
    }

    public static Packet Create(PacketType type)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(HeaderLength);

        return new Packet(buffer, HeaderLength)
        {
            Type = type
        };
    }

    public static Packet CreateAlloc(PacketType type, int length)
    {
        var totalLength = HeaderLength + length;
        var buffer = new byte[totalLength];

        return new Packet(buffer, totalLength)
        {
            Type = type
        };
    }

    public static Packet CreateConnectionRequest(string key)
    {
        var keyLength = Encoding.UTF8.GetByteCount(key) + 2;
        var length = HeaderLength + keyLength;
        var buffer = ArrayPool<byte>.Shared.Rent(length);

        var packet = new Packet(buffer, length)
        {
            Type = PacketType.ConnectionRequest
        };

        packet.Writer.WriteString(key);

        return packet;
    }

    public static Packet CreateChannel(byte channelId, ushort sequence, ReadOnlySpan<byte> payload)
    {
        var length = HeaderLength + payload.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(length);

        payload.CopyTo(buffer.AsSpan(HeaderLength));

        return new Packet(buffer, length)
        {
            Type = PacketType.Channel,
            ChannelId = channelId,
            Sequence = sequence
        };
    }

    public static Packet CreateAck(byte channelId, ushort sequence)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(HeaderLength);

        return new Packet(buffer, HeaderLength)
        {
            Type = PacketType.Ack,
            ChannelId = channelId,
            Sequence = sequence
        };
    }
}

public enum PacketType : byte
{
    ConnectionRequest,
    ConnectionAccept,
    ConnectionReject,
    Channel,
    Ack,
    Ping,
    Pong,
    Close
}