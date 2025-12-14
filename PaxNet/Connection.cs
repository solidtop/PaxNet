using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PaxNet;

public class Connection
{
    private readonly Host _host;
    private readonly ConcurrentDictionary<byte, Channel> _channels;
    private readonly Packet _pingPacket;
    private readonly Packet _pongPacket;

    internal Connection(Host host, IPEndPoint remoteEndPoint)
    {
        _host = host;
        _channels = [];
        _pingPacket = Packet.CreateAlloc(PacketType.Ping, sizeof(long));
        _pongPacket = Packet.CreateAlloc(PacketType.Pong, sizeof(long));

        RemoteEndPoint = remoteEndPoint;
    }

    public IPEndPoint RemoteEndPoint { get; }
    public ConnectionState State { get; private set; }

    public TimeSpan KeepAliveInterval { get; set; }
    public TimeSpan TimeoutInterval { get; set; }
    public TimeSpan Rtt { get; private set; } = TimeSpan.Zero;
    public TimeSpan RttVar { get; private set; } = TimeSpan.Zero;
    public TimeSpan LastSend { get; private set; }
    public TimeSpan LastReceive { get; private set; }

    public void Send(ReadOnlySpan<byte> data, DeliveryMethod deliveryMethod)
    {
        if (State != ConnectionState.Connected)
            return;

        var channelId = (byte)deliveryMethod;

        if (!_channels.TryGetValue(channelId, out var channel))
        {
            var options = new ChannelOptions(); // TODO: This should be handled better
            channel = Channel.Create(this, channelId, options);
            _channels[channelId] = channel;
        }

        channel.Send(data);
    }

    public void Disconnect()
    {
        if (State != ConnectionState.Connected)
            return;

        using var closePacket = Packet.Create(PacketType.Close);
        SendCore(closePacket.Data);

        State = ConnectionState.Disconnecting;
        Close(DisconnectReason.LocalClose);
    }

    public void Close(DisconnectReason reason)
    {
        if (State == ConnectionState.Disconnected)
            return;

        State = ConnectionState.Disconnected;
        _host.OnConnectionClosed(this, reason);
    }

    internal void Accept()
    {
        if (State != ConnectionState.Connecting)
            return;

        State = ConnectionState.Connected;
        _host.OnConnectionAccepted(this);
    }

    internal void Reject()
    {
        if (State == ConnectionState.Rejected)
            return;

        State = ConnectionState.Rejected;
        _host.OnConnectionRejected(this);
    }

    internal void SendCore(ReadOnlySpan<byte> data)
    {
        if (State != ConnectionState.Connected)
            return;

        try
        {
            _host.SendTo(data, RemoteEndPoint);
        }
        catch (SocketException ex)
        {
            _host.OnErrorOccured(this, ex.SocketErrorCode);

            // Close only on fatal error
            if (ex.SocketErrorCode is SocketError.HostUnreachable or SocketError.NetworkUnreachable)
                Close(DisconnectReason.TransportError);
        }
    }

    internal void Update(TimeSpan elapsed)
    {
        KeepAlive(elapsed);

        foreach (var channel in _channels.Values)
            channel.Update(elapsed);
    }

    internal void OnInboundPacketReady(Packet packet)
    {
        _host.OnDataReceived(this, packet, (DeliveryMethod)packet.ChannelId);
    }

    internal void OnOutboundPacketReady(Packet packet)
    {
        SendCore(packet.Data);
        packet.Dispose();
    }

    internal void HandlePacket(Packet packet, TimeSpan elapsed)
    {
        LastReceive = elapsed;

        switch (packet.Type)
        {
            case PacketType.ConnectionRequest:
                HandleRequest(packet);
                break;
            case PacketType.ConnectionAccept:
                Accept();
                break;
            case PacketType.ConnectionReject:
                Reject();
                break;
            case PacketType.Channel:
                HandleChannel(packet);
                return;
            case PacketType.Ack:
                HandleAck(packet);
                break;
            case PacketType.Ping:
                SendPong(packet);
                break;
            case PacketType.Pong:
                UpdateRtt(packet);
                break;
            case PacketType.Close:
                Close(DisconnectReason.RemoteClose);
                break;
            default:
                throw new ArgumentOutOfRangeException($"Invalid packet type: {packet.Type}");
        }

        packet.Dispose();
    }

    private void HandleRequest(Packet requestPacket)
    {
        if (State != ConnectionState.Connecting)
            return;

        var request = new ConnectionRequest(this, requestPacket);
        _host.OnConnectionRequested(request);
    }

    private void HandleChannel(Packet channelPacket)
    {
        var channelId = channelPacket.ChannelId;

        if (!_channels.TryGetValue(channelId, out var channel))
        {
            var options = new ChannelOptions();
            channel = Channel.Create(this, channelId, options);
            _channels[channelId] = channel;
        }

        channel.ProcessData(channelPacket);
    }

    private void HandleAck(Packet ackPacket)
    {
        if (!_channels.TryGetValue(ackPacket.ChannelId, out var channel))
            return;

        channel.ProcessAck(ackPacket.Sequence);
    }

    private void SendPing()
    {
        var timestamp = Stopwatch.GetTimestamp();
        _pingPacket.Writer.WriteInt64(timestamp);
        SendCore(_pingPacket.Data);
    }

    private void SendPong(Packet pingPacket)
    {
        var timestamp = pingPacket.Reader.ReadInt64();
        _pongPacket.Writer.WriteInt64(timestamp);
        SendCore(_pongPacket.Data);
    }

    private void UpdateRtt(Packet pongPacket)
    {
        var timestamp = pongPacket.Reader.ReadInt64();
        var sample = Stopwatch.GetElapsedTime(timestamp);

        if (Rtt == TimeSpan.Zero)
        {
            Rtt = sample;
            RttVar = sample * 0.5;
        }
        else
        {
            const double alpha = 1.0 / 8.0;
            const double beta = 1.0 / 4.0;

            var srRtMs = Rtt.TotalMilliseconds;
            var rttVarMs = RttVar.TotalMilliseconds;
            var sampleMs = sample.TotalMilliseconds;

            rttVarMs = (1 - beta) * rttVarMs + beta * Math.Abs(srRtMs - sampleMs);
            srRtMs = (1 - alpha) * srRtMs + alpha * sampleMs;

            Rtt = TimeSpan.FromMilliseconds(srRtMs);
            RttVar = TimeSpan.FromMilliseconds(rttVarMs);
        }

        _host.OnRttUpdated(this, Rtt);
    }

    private void KeepAlive(TimeSpan elapsed)
    {
        if (State != ConnectionState.Connected)
            return;

        if (elapsed - LastSend > KeepAliveInterval)
        {
            SendPing();
            LastSend = elapsed;
        }

        if (elapsed - LastReceive > TimeoutInterval)
            Close(DisconnectReason.Timeout);
    }
}

public enum ConnectionState
{
    Connecting,
    Disconnecting,
    Connected,
    Disconnected,
    Rejected
}

public enum DisconnectReason
{
    LocalClose,
    RemoteClose,
    Timeout,
    Reject,
    TransportError
}

public enum DeliveryMethod
{
    Unreliable,
    UnreliableSequenced,
    Reliable,
    ReliableSequenced,
    ReliableOrdered
}

public interface IConnectionListener
{
    void OnConnectionRequested(ConnectionRequest request);
    void OnConnected(Connection connection);
    void OnDisconnected(Connection connection, DisconnectReason reason);
    void OnDataReceived(Connection connection, PacketReader reader, DeliveryMethod deliveryMethod);
    void OnRttUpdated(Connection connection, TimeSpan rtt);
    void OnErrorOccured(IPEndPoint remoteEndPoint, SocketError error);
}