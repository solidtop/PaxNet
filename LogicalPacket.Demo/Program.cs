using LogicalPacket.Core;

var options = new ServerOptions
{
    Port = 8000,
};

var server = new Server(options);
server.Start();
