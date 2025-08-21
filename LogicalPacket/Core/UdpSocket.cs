using System.Net;
using System.Net.Sockets;

namespace LogicalPacket.Core;

public sealed class UdpSocket : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly IPEndPoint _anyRemoteEndPoint = new(IPAddress.Any, 0);
    private bool _disposed;

    public void Bind(IPEndPoint endpoint)
    {
        ThrowIfDisposed();
        _socket.Bind(endpoint);
    }

    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return _socket.SendToAsync(buffer, SocketFlags.None, endPoint, cancellationToken);
    }

    public ValueTask<SocketReceiveFromResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return _socket.ReceiveFromAsync(buffer, _anyRemoteEndPoint, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _socket.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

