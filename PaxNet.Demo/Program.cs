using PaxNet;

using var server = new Server();
using var client = new Client();

server.ConnectionRequested += request => { request.AcceptIfKey("MyKey"); };

server.PeerConnected += peer => { Console.WriteLine($"Peer {peer} connected"); };

server.PeerDisconnected += (peer, info) =>
{
    Console.WriteLine($"Peer {peer} disconnected with reason: {info.Reason}");
};

server.PacketReceived += (peer, packet, deliveryMethod) =>
{
    Console.WriteLine($"Packet received from {peer} with method {deliveryMethod}");
};

server.ErrorOccured += (endPoint, error) =>
{
    Console.WriteLine($"Error received from {endPoint} with error {error}");
};

server.RttUpdated += (peer, rtt) => { Console.WriteLine($"RTT for peer {peer} is {rtt.TotalMilliseconds:F1} ms"); };

server.Start("127.0.0.1", 8000);
client.Connect("127.0.0.1", 8000);

while (!Console.KeyAvailable)
{
    server.PollEvents();
    await Task.Delay(16);
}