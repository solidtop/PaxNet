using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace LogicalPacket.Core;

public sealed class UdpSocket : IDisposable
{
    private const int MaxUdpSize = 1500;

    private readonly Socket _socket;
    private bool _disposed;

    public UdpSocket(int port)
    {
        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp)
        {
            DualMode = true,
        };

        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
    }

    public async ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var owner = MemoryPool<byte>.Shared.Rent(MaxUdpSize);

        var any = new IPEndPoint(IPAddress.IPv6Any, 0);
        var result = await _socket.ReceiveFromAsync(owner.Memory, SocketFlags.None, any, cancellationToken).ConfigureAwait(false);

        var remote = (IPEndPoint)result.RemoteEndPoint;
        var buffer = owner.Memory[..result.ReceivedBytes];

        return new UdpReceiveResult(remote, owner, buffer);
    }

    public void Dispose()
    {
        Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _socket.Dispose();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public readonly struct UdpReceiveResult(IPEndPoint remoteEndPoint, IMemoryOwner<byte> owner, ReadOnlyMemory<byte> buffer) : IDisposable
{
    public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint;
    public IMemoryOwner<byte> Owner { get; } = owner;
    public ReadOnlyMemory<byte> Buffer { get; } = buffer;
    public void Dispose() => Owner.Dispose();
}
