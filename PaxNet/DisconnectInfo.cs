using System.Net.Sockets;

namespace PaxNet;

public enum DisconnectReason
{
    LocalClose,
    RemoteClose,
    Timeout,
    TransportError
}

public record DisconnectInfo(DisconnectReason Reason, SocketError? SocketError)
{
    public static DisconnectInfo LocalClose => new(DisconnectReason.LocalClose, null);
    public static DisconnectInfo RemoteClose => new(DisconnectReason.RemoteClose, null);
    public static DisconnectInfo Timeout => new(DisconnectReason.Timeout, null);

    public static DisconnectInfo TransportError(SocketError error)
    {
        return new DisconnectInfo(DisconnectReason.TransportError, error);
    }
}