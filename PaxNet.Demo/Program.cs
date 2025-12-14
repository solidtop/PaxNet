using System.Net;
using PaxNet.Demo;

var localEndPoint = new IPEndPoint(IPAddress.Loopback, 5555);

var server = new Server();
server.Start(localEndPoint);

List<Client> clients = [];

for (var i = 0; i < 10; i++)
{
    var client = new Client();
    client.Connect(localEndPoint);
    clients.Add(client);
}

while (!Console.KeyAvailable)
{
    server.Update();

    foreach (var client in clients)
        client.Update();

    Thread.Sleep(15);
}