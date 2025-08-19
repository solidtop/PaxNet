using System.Net.Sockets;

namespace LogicalPacket.Core;

public sealed class Server(ServerOptions options) : IDisposable
{
    private readonly ServerOptions _options = options;
    private readonly UdpSocket _socket = new(options.Port);
    private readonly CancellationTokenSource _cts = new();

    private bool _isRunning;
    private Task? _receiveTask;

    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;

        _receiveTask = Task.Run(() => ReceiveAsync(_cts.Token), _cts.Token);

        Console.WriteLine($"Server started on port {_options.Port}... Press enter to stop.");

        Console.ReadLine();

        Stop();
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        Console.WriteLine("Server shutting down...");

        _isRunning = false;

        _cts.Cancel();

        _receiveTask?.GetAwaiter().GetResult();
        _cts.Dispose();
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var result = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var remoteEndpoint = result.RemoteEndPoint;
                var buffer = result.Buffer;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket error while receiving UDP packet: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _socket.Dispose();
        _cts.Dispose();
    }
}
