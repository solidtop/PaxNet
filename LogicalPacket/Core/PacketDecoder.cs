using System.Buffers.Binary;

namespace LogicalPacket.Core;

public static class PacketDecoder
{
    private const int HeaderSize = 6;// [type:1][flags:1][seq:4 BE]

    public static bool TryDecode(ReadOnlyMemory<byte> buffer, out Packet packet)
    {
        var span = buffer.Span;
        var type = (PacketType)span[0];
        var flags = (PacketFlags)span[1];
        var seq = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2, 4));

        var header = new PacketHeader(type, flags, seq);
        var payload = buffer[HeaderSize..];
        packet = new Packet(header, payload);

        return true;
    }
}
