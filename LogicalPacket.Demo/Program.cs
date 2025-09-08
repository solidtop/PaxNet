using LogicalPacket;

var server = new Server();
server.Start(8000);
Console.ReadLine();
server.Stop();