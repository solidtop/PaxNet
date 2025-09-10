using LogicalPacket;
using LogicalPacket.Demo;

var netListener = new NetEventListener();
var server = new Server(netListener);
server.Start(8000);

var cts = new CancellationTokenSource();

_ = Task.Run(() =>
{
    Console.ReadLine();
    cts.Cancel();
});

while (!cts.Token.IsCancellationRequested)
{
    server.PollEvents();
    await Task.Delay(16);
}

server.Stop();