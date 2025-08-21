using LogicalPacket.Core;

var server = new Server();
server.Start(port: 8000);

Console.ReadLine();

server.Stop();
