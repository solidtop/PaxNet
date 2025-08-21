namespace LogicalPacket.Core;

public readonly struct Packet(PacketHeader header, ReadOnlyMemory<byte> payload)
{
    public PacketHeader Header { get; } = header;
    public ReadOnlyMemory<byte> Payload { get; } = payload;
}

public readonly struct PacketHeader(PacketType type, PacketFlags flags, uint sequence)
{
    public PacketType Type { get; } = type;
    public PacketFlags Flags { get; } = flags;
    public uint Sequence { get; } = sequence;
}

public enum PacketType : byte
{
    Connect,
    ConnectAccept,
    Disconnect,
}

[Flags]
public enum PacketFlags : byte
{
    None,
    Reliable,
    Sequenced,
}