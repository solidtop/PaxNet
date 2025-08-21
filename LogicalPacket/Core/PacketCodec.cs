using System.Buffers.Binary;

namespace LogicalPacket.Core;

public static class PacketCodec
{
    private const int HeaderSize = 6;

    public static bool TryDecode(ReadOnlyMemory<byte> buffer, out Packet packet)
    {
        var span = buffer.Span;
        var type = (PacketType)span[0];
        var flags = (PacketFlags)span[1];
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2, 4));

        var header = new PacketHeader(type, flags, seq);
        var payload = buffer[HeaderSize..];
        packet = new Packet(header, payload);

        return true;
    }

    public static bool TryEncodeHeader(in PacketHeader header, Span<byte> destination, out int bytesWritten)
    {
        destination[0] = (byte)header.Type;
        destination[1] = (byte)header.Flags;
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(2, 4), header.Sequence);

        bytesWritten = HeaderSize;

        return true;
    }
}