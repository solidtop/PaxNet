using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace LogicalPacket.Core;

public sealed class Server : IDisposable
{
    private const int MaxUdpSize = 1500;

    private readonly UdpSocket _socket = new();
    private bool _isRunning;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public void Start(int port)
    {
        if (_isRunning)
            return;

        _isRunning = true;

        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        Console.WriteLine($"Server started on port {port}...");
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        Console.WriteLine("Server shutting down...");

        _cts?.Cancel();
        _receiveTask?.Wait();
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(MaxUdpSize);
        var memory = rented.AsMemory();

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _socket.ReceiveAsync(memory, cancellationToken).ConfigureAwait(false);
                var remote = (IPEndPoint)result.RemoteEndPoint;
                var buffer = memory[..result.ReceivedBytes];

                if (PacketCodec.TryDecode(buffer, out var packet))
                {
                    Console.WriteLine($"Packet received of type: {packet.Header.Type}");
                }
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
                Console.WriteLine($"Socket error while receiving UDP packet: {ex.Message}");
            }
        }

        ArrayPool<byte>.Shared.Return(rented);
    }

    public void Dispose()
    {
        Stop();
        _socket.Dispose();
        _cts?.Dispose();
    }
}