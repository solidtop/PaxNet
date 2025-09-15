using System.Buffers;
using System.Net;
using System.Net.Sockets;
using LogicalPacket.Core;

namespace LogicalPacket.Demo;

public class TestClient(string serverAddress, int serverPort)
{
    private const int MaxPacketSize = 1024;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly IPEndPoint _serverEndPoint = new(IPAddress.Parse(serverAddress), serverPort);
    private readonly MemoryPool<byte> _bufferPool = MemoryPool<byte>.Shared;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public async Task StartAsync()
    {
        _socket.Bind(new IPEndPoint(IPAddress.Any, 8001));
        _cts = new CancellationTokenSource();

        await ConnectAsync();

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    internal ValueTask<int> SendAsync(Packet packet, CancellationToken cancellationToken)
    {
        try
        {
            return _socket.SendToAsync(packet.Data, SocketFlags.None, _serverEndPoint, cancellationToken);
        }
        catch (SocketException)
        {
            return ValueTask.FromResult(0);
        }
        finally
        {
            packet.Dispose();
        }
    }

    private async Task ConnectAsync()
    {
        const uint connectionNumber = 1;
        var connectionTime = DateTime.Now.Ticks;

        Memory<byte> buffer = new byte[60];
        var span = buffer.Span;
        var writer = new PacketWriter(span);

        writer.WriteByte((byte)PacketType.ConnectRequest);
        writer.WriteUInt32(connectionNumber);
        writer.WriteInt64(connectionTime);
        writer.WriteString("MyKey");

        await _socket.SendToAsync(buffer, SocketFlags.None, _serverEndPoint, _cts!.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var bufferOwner = _bufferPool.Rent(MaxPacketSize);
                var buffer = bufferOwner.Memory;

                var result =
                    await _socket.ReceiveFromAsync(buffer, SocketFlags.None, _serverEndPoint, cancellationToken);

                var packet = new Packet(bufferOwner, result.ReceivedBytes);
                HandlePacket(packet);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
            }
    }

    private void HandlePacket(Packet packet)
    {
        switch (packet.Type)
        {
            case PacketType.ConnectAccept:
                Console.WriteLine("Connected to server!");
                break;
            case PacketType.Shutdown:
                Console.WriteLine("Disconnected from server!");
                break;
            default:
                Console.WriteLine($"Received packet type: {packet.Type}");
                break;
        }
    }
}