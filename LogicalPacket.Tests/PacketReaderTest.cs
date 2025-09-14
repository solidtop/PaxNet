using System.Buffers.Binary;
using System.Text;
using LogicalPacket.Core;

namespace LogicalPacket.Tests;

public class PacketReaderTest
{
    [Fact]
    public void ReadByte_ShouldReturnCorrectValue()
    {
        const byte expected = 0x42;
        var buffer = new[] { expected };
        var reader = new PacketReader(buffer);

        var actual = reader.ReadByte();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadInt16_ShouldReturnCorrectValue_LittleEndian()
    {
        const short expected = 12345;
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, expected);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadInt16();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadInt32_ShouldReturnCorrectValue_LittleEndian()
    {
        const int expected = 0x12345678;
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, expected);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadInt32();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadInt64_ShouldReturnCorrectValue_LittleEndian()
    {
        const long expected = 12345678L;
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, expected);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadInt64();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadUInt64_ShouldReturnCorrectValue_LittleEndian()
    {
        const ulong expected = 12345678UL;
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, expected);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadUInt64();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadFloat_ShouldReturnCorrectValue()
    {
        const float expected = 12345.6789f;
        var buffer = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, expected);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadFloat();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadDouble_ShouldReturnCorrectValue()
    {
        const double expected = 12345.6789d;
        var buffer = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, expected);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadDouble();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadString_ShouldReturnCorrectUtf8String()
    {
        const string expected = "Hello";
        var stringBytes = Encoding.UTF8.GetBytes(expected);
        var buffer = new byte[stringBytes.Length + 2];

        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)stringBytes.Length);
        stringBytes.CopyTo(buffer, 2);

        var reader = new PacketReader(buffer);
        var actual = reader.ReadString();

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void SequentialReads_ShouldAdvancePositionCorrectly()
    {
        var buffer = new byte[7];
        buffer[0] = 0x01;
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(1), 0x0203);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(3), 0x04050607);

        var reader = new PacketReader(buffer);

        Assert.Equal(0x01, reader.ReadByte());
        Assert.Equal(0x0203, reader.ReadInt16());
        Assert.Equal(0x04050607, reader.ReadInt32());
        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void EmptyPayload_ShouldHaveEndOfBufferTrue()
    {
        var buffer = Array.Empty<byte>();
        var reader = new PacketReader(buffer);

        Assert.True(reader.EndOfBuffer);
    }

    [Fact]
    public void ReadingBeyondBuffer_ShouldThrow()
    {
        Assert.Throws<IndexOutOfRangeException>(() => ReadPastEnd([1]));
        return;

        void ReadPastEnd(byte[] buffer)
        {
            var reader = new PacketReader(buffer);
            reader.ReadByte();
            reader.ReadByte();
        }
    }
}