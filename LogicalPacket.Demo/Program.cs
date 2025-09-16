using System.Net;
using LogicalPacket;
using LogicalPacket.Demo;

var netListener = new NetEventListener();
var server = new Server(netListener);
server.Start(8000);

var client = new TestClient(8000);
client.Connect(8001);

while (!Console.KeyAvailable)
{
    server.PollEvents();
    await Task.Delay(16);
}

server.Stop();