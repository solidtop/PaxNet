using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LogicalPacket.Core;

namespace LogicalPacket;

public sealed class Server : IDisposable
{
    private const int MaxPacketSize = 1024;

    private readonly Socket _socket;
    private readonly MemoryPool<byte> _bufferPool;
    private readonly IPEndPoint _endPointFactory;

    private readonly ConcurrentDictionary<IPEndPoint, SocketAddress> _addressCache;
    private readonly ConcurrentDictionary<SocketAddress, IPEndPoint> _endPointCache;
    private readonly ConcurrentDictionary<IPEndPoint, Client> _clients;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public Server()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _bufferPool = MemoryPool<byte>.Shared;
        _endPointFactory = new IPEndPoint(IPAddress.Any, 0);

        _clients = [];
        _endPointCache = [];
        _addressCache = [];
    }

    public void Dispose()
    {
        Stop();
        _socket.Dispose();
    }

    public void Start(int port)
    {
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        _cts = new CancellationTokenSource();

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        Console.WriteLine($"Server started on port {port}...");
    }

    public void Stop()
    {
        Console.WriteLine("Server shutting down...");

        _cts?.Cancel();
        _receiveTask?.Wait();
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
    }

    internal int Send(Packet packet, IPEndPoint remoteEndPoint)
    {
        try
        {
            var address = _addressCache.GetOrAdd(remoteEndPoint, ep => ep.Serialize());
            return _socket.SendTo(packet.Data.Span, SocketFlags.None, address);
        }
        catch (SocketException ex)
        {
            Console.WriteLine(ex.Message);
            return 0;
        }
        finally
        {
            packet.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        // Preallocate SocketAddress for reuse
        var receivedAddress = new SocketAddress(_socket.AddressFamily);

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var bufferOwner = _bufferPool.Rent(MaxPacketSize);
                var buffer = bufferOwner.Memory;

                var bytesReceived =
                    await _socket.ReceiveFromAsync(buffer, SocketFlags.None, receivedAddress, cancellationToken);

                if (bytesReceived == 0)
                {
                    bufferOwner.Dispose();
                    continue;
                }

                var remoteEndPoint = GetEndPoint(receivedAddress);
                var packet = new Packet(bufferOwner, bytesReceived);
                HandlePacket(packet, remoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                Console.WriteLine(ex.Message);
            }
    }

    private void HandlePacket(Packet packet, IPEndPoint remoteEndPoint)
    {
        var clientFound = _clients.TryGetValue(remoteEndPoint, out var client);

        switch (packet.Type)
        {
            case PacketType.Connect:
                ProcessConnect(packet, remoteEndPoint, client);
                packet.Dispose();
                break;
            default:
                if (clientFound)
                    client!.ProcessPacket(packet);
                break;
        }
    }

    private void ProcessConnect(Packet packet, IPEndPoint remoteEndPoint, Client? client)
    {
        // TODO: Implement a more robust connection handshake
        if (client != null) return;

        client = new Client(this, remoteEndPoint);
        _clients.TryAdd(remoteEndPoint, client);

        var connectAcceptPacket = new Packet(PacketType.ConnectAccept);
        Send(connectAcceptPacket, remoteEndPoint);
    }

    private IPEndPoint GetEndPoint(SocketAddress address)
    {
        if (_endPointCache.TryGetValue(address, out var endPoint)) return endPoint;

        endPoint = (IPEndPoint)_endPointFactory.Create(address);

        var addressCopy = new SocketAddress(address.Family, address.Size);
        address.Buffer.CopyTo(addressCopy.Buffer);
        _endPointCache.TryAdd(addressCopy, endPoint);

        return endPoint;
    }
}