using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace PaxNet.Core;

public ref struct PacketWriter(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;
    private int _position = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        _buffer[_position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(ReadOnlySpan<byte> values)
    {
        values.CopyTo(_buffer.Slice(_position, values.Length));
        _position += values.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.Slice(_position, 2), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_position, 2), value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.Slice(_position, 8), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.Slice(_position, 8), value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt16((ushort)byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.Slice(_position, byteCount));
        _position += byteCount;
    }

    public Span<byte> Data => _buffer[.._position];
}