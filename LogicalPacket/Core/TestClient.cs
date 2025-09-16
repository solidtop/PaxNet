using System.Buffers;
using System.Net;
using System.Net.Sockets;
using LogicalPacket.Core;

namespace LogicalPacket.Demo;

public class TestClient(int serverPort)
{
    private const int MaxPacketSize = 1024;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly IPEndPoint _serverEndPoint = new(IPAddress.Loopback, serverPort);
    private readonly MemoryPool<byte> _bufferPool = MemoryPool<byte>.Shared;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _sendTask;

    public void Connect(int port)
    {
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _cts = new CancellationTokenSource();
        
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        
        // Attempt connection
        const uint connectionNumber = 1;
        var connectionTime = DateTime.Now.Ticks;
        const string key = "MyKey";

        Memory<byte> buffer = new byte[60];
        var writer = new PacketWriter(buffer.Span);

        writer.WriteByte((byte)PacketType.ConnectRequest);
        writer.WriteUInt32(connectionNumber);
        writer.WriteInt64(connectionTime);
        writer.WriteString(key);

        _socket.SendTo(buffer.Span, SocketFlags.None, _serverEndPoint);
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
    
    private void Shutdown()
    {
        _cts?.Cancel();
        _receiveTask?.Wait();
        _sendTask?.Wait();
        _socket.Close();
        _cts =  null;
        _receiveTask = null;
        
        Console.WriteLine("Disconnected from server!");
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

    private async Task SendLoopAsync(CancellationToken cancellationToken, int delay)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var packet = new Packet(PacketType.Unreliable);
            
            await SendAsync(packet, cancellationToken);
            await Task.Delay(delay, cancellationToken);
        }
    }

    private void HandlePacket(Packet packet)
    {
        switch (packet.Type)
        {
            case PacketType.ConnectAccept:
                Console.WriteLine("Connected to server");
                _sendTask = Task.Run(() => SendLoopAsync(_cts.Token, 100));
                break;
            case PacketType.ConnectReject:
                Shutdown();
                break;
            case PacketType.Shutdown:
                Shutdown();
                break;
            default:
                Console.WriteLine($"Received packet type: {packet.Type}");
                break;
        }
    }
}