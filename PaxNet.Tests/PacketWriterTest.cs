using PaxNet.Core;

namespace PaxNet.Tests;

public class PacketWriterTest
{
    [Fact]
    public void WriteByte_WritesSingleByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new PacketWriter(buffer);

        writer.WriteByte(0x42);

        var expected = writer.Data;
        var actual = new byte[] { 0x42 };

        Assert.Equal(actual, expected);
    }

    [Fact]
    public void WriteBytes_WritesMultipleBytes()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new PacketWriter(buffer);

        var expected = new byte[] { 1, 2, 3, 4 };

        writer.WriteBytes(expected);

        Assert.Equal(expected, writer.Data);
    }

    [Fact]
    public void WriteInt16_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new PacketWriter(buffer);

        writer.WriteInt16(0x1234);

        Assert.Equal(new byte[] { 0x34, 0x12 }, writer.Data);
    }

    [Fact]
    public void WriteUInt16_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[2];
        var writer = new PacketWriter(buffer);

        writer.WriteUInt16(0xABCD);

        Assert.Equal(new byte[] { 0xCD, 0xAB }, writer.Data);
    }

    [Fact]
    public void WriteInt32_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new PacketWriter(buffer);

        writer.WriteInt32(0x12345678);

        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, writer.Data);
    }

    [Fact]
    public void WriteUInt32_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new PacketWriter(buffer);

        writer.WriteUInt32(0x89ABCDEF);

        Assert.Equal(new byte[] { 0xEF, 0xCD, 0xAB, 0x89 }, writer.Data);
    }

    [Fact]
    public void WriteInt64_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[8];
        var writer = new PacketWriter(buffer);

        writer.WriteInt64(0x1122334455667788);

        Assert.Equal(new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 }, writer.Data);
    }

    [Fact]
    public void WriteUInt64_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[8];
        var writer = new PacketWriter(buffer);

        writer.WriteUInt64(0x8877665544332211);

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, writer.Data);
    }

    [Fact]
    public void WriteFloat_WritesIEEE754LittleEndian()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new PacketWriter(buffer);

        writer.WriteFloat(1.5f);

        // 1.5f in IEEE 754 little-endian: 00 00 C0 3F
        Assert.Equal(new byte[] { 0x00, 0x00, 0xC0, 0x3F }, writer.Data);
    }

    [Fact]
    public void WriteString_WritesLengthAndUtf8Bytes()
    {
        Span<byte> buffer = stackalloc byte[10];
        var writer = new PacketWriter(buffer);

        writer.WriteString("Hi");

        // Expected: length (2 bytes LE) + UTF-8 bytes for "Hi"
        Assert.Equal(new byte[] { 0x02, 0x00, (byte)'H', (byte)'i' }, writer.Data);
    }

    [Fact]
    public void MultipleWrites_AppendSequentially()
    {
        Span<byte> buffer = stackalloc byte[10];
        var writer = new PacketWriter(buffer);

        writer.WriteByte(0xAA);
        writer.WriteInt16(0x1234);
        writer.WriteByte(0xBB);

        Assert.Equal(new byte[] { 0xAA, 0x34, 0x12, 0xBB }, writer.Data);
    }
}