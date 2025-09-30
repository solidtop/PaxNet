using System.Buffers;
using System.Net;
using System.Net.Sockets;
using PaxNet.Core;

namespace PaxNet;

public class Client : IDisposable
{
    private const int MaxPacketSize = 1024;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly MemoryPool<byte> _bufferPool = MemoryPool<byte>.Shared;

    private IPEndPoint? _serverEndPoint;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public void Dispose()
    {
        Shutdown();
        _socket.Dispose();
    }

    public void Connect(string address, int port)
    {
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        _serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
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

        _socket.SendTo(writer.Data, SocketFlags.None, _serverEndPoint);
    }

    public void Disconnect()
    {
        var buffer = new byte[1];
        buffer[0] = (byte)PacketType.Disconnect;
        _socket.SendTo(buffer, SocketFlags.None, _serverEndPoint);
    }

    internal int Send(Packet packet)
    {
        if (_serverEndPoint == null) return 0;

        try
        {
            return _socket.SendTo(packet.Data.Span, SocketFlags.None, _serverEndPoint);
        }
        catch (SocketException ex)
        {
            return 0;
        }
        finally
        {
            packet.Dispose();
        }
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

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_serverEndPoint == null)
            return;

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

    private void Shutdown()
    {
        _cts?.Cancel();
        _socket.Close();
        _cts = null;
        _receiveTask = null;
    }

    private void HandlePacket(Packet packet)
    {
        switch (packet.Type)
        {
            case PacketType.ConnectAccept:
                Console.WriteLine("Connected to server");
                break;
            case PacketType.ConnectReject:
            case PacketType.Shutdown:
                Shutdown();
                break;
            case PacketType.Ping:
                SendPongEcho(packet);
                break;
            case PacketType.Pong:
                break;
            default:
                Console.WriteLine($"Received packet type: {packet.Type}");
                break;
        }

        packet.Dispose();
    }

    private void SendPongEcho(Packet pingPacket)
    {
        var headerSize = Packet.GetHeaderSize(PacketType.Pong);
        var payloadSpan = pingPacket.Payload.Span;
        var packetSize = headerSize + payloadSpan.Length;

        var bufferOwner = _bufferPool.Rent(packetSize);
        var span = bufferOwner.Memory.Span;

        payloadSpan.CopyTo(span[headerSize..]);

        var pongPacket = new Packet(bufferOwner, packetSize) { Type = PacketType.Pong };
        Send(pongPacket);
    }
}