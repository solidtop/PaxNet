using System.Net.Sockets;

namespace PaxNet;

public enum DisconnectReason
{
    LocalClose,
    RemoteClose,
    Timeout,
    Rejected,
    ConnectionFailed
}

public record DisconnectInfo(DisconnectReason Reason, SocketError? SocketError)
{
    public static DisconnectInfo LocalClose => new(DisconnectReason.LocalClose, null);
    public static DisconnectInfo RemoteClose => new(DisconnectReason.RemoteClose, null);
    public static DisconnectInfo Timeout => new(DisconnectReason.Timeout, null);
}