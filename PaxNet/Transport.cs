using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace PaxNet;

internal class Transport(int maxPacketSize) : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly MemoryPool<byte> _bufferPool = MemoryPool<byte>.Shared;

    public AddressFamily AddressFamily => _socket.AddressFamily;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _socket.Dispose();
        _bufferPool.Dispose();
    }

    public void Bind(IPEndPoint localEndPoint)
    {
        _socket.Bind(localEndPoint);
    }

    public int Send(ReadOnlySpan<byte> data, IPEndPoint remoteEndPoint)
    {
        return _socket.SendTo(data, SocketFlags.None, remoteEndPoint);
    }

    public Task<int> SendAsync(ReadOnlyMemory<byte> data, IPEndPoint remoteEndPoint)
    {
        throw new NotImplementedException();
    }

    public async Task<Packet> ReceiveAsync(SocketAddress receivedAddress, CancellationToken cancellationToken)
    {
        var bufferOwner = _bufferPool.Rent(maxPacketSize);
        var buffer = bufferOwner.Memory;
        var bytesReceived =
            await _socket.ReceiveFromAsync(buffer, SocketFlags.None, receivedAddress, cancellationToken);

        return new Packet(bufferOwner, bytesReceived);
    }
}