using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PaxNet;

public sealed class Connection
{
    private readonly Dictionary<int, Channel> _channels = [];

    internal Connection(Transport transport, IPEndPoint remoteEndPoint)
    {
        Transport = transport;
        RemoteEndPoint = remoteEndPoint;
    }

    internal Transport Transport { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Connecting;

    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan TimeoutInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan Rtt { get; private set; } = TimeSpan.Zero;
    public DateTime LastSend { get; private set; }
    public DateTime LastReceive { get; private set; }

    internal event Action<ConnectionRequest>? Requested;
    internal event Action<Connection>? Accepted;
    internal event Action<Connection>? Rejected;
    internal event Action<Connection, DisconnectInfo>? Disconnected;
    internal event Action<Connection, Packet>? DataReceived;
    internal event Action<Connection, TimeSpan>? RttUpdated;
    internal event Action<Connection, SocketError>? ErrorOccurred;

    public void Disconnect()
    {
        if (State != ConnectionState.Connected)
            return;

        State = ConnectionState.Disconnecting;
        using var disconnectPacket = Packet.Create(PacketType.Disconnect);
        Transport.Send(disconnectPacket.Data, RemoteEndPoint);

        Close(DisconnectInfo.LocalClose);
    }

    public void Close(DisconnectInfo info)
    {
        if (State == ConnectionState.Disconnected)
            return;

        State = ConnectionState.Disconnected;
        Disconnected?.Invoke(this, info);
        // Free resources etc..
    }

    public void Send(ReadOnlySpan<byte> data, Delivery delivery, byte channelId = 0)
    {
        if (!_channels.TryGetValue(channelId, out var channel))
        {
            channel = Channel.Create(channelId, delivery);
            _channels.TryAdd(channelId, channel);
        }

        channel.Send(data);
    }

    internal void Send(ReadOnlySpan<byte> data)
    {
        if (State != ConnectionState.Connected)
            return;

        try
        {
            Transport.Send(data, RemoteEndPoint);
        }
        catch (SocketException ex)
        {
            ErrorOccurred?.Invoke(this, ex.SocketErrorCode);

            // Close only on fatal error
            if (ex.SocketErrorCode is SocketError.HostUnreachable or SocketError.NetworkUnreachable)
                Close(DisconnectInfo.TransportError(ex.SocketErrorCode));
        }
    }

    internal void HandlePacket(Packet packet)
    {
        LastReceive = DateTime.UtcNow;

        switch (packet.Type)
        {
            case PacketType.ConnectRequest:
                HandleRequest(packet);
                return;
            case PacketType.ConnectAccept:
                Accept();
                break;
            case PacketType.ConnectReject:
                Reject();
                break;
            case PacketType.Disconnect:
                Close(DisconnectInfo.RemoteClose);
                break;
            case PacketType.Ping:
                SendPong(packet);
                break;
            case PacketType.Pong:
                UpdateRtt(packet);
                break;
            case PacketType.Data:
                HandleData(packet);
                return;
            case PacketType.Ack:
                HandleAck(packet);
                break;
            default:
                throw new ArgumentOutOfRangeException($"Unknown packet type {packet.Type}");
        }

        packet.Dispose();
    }

    internal void Accept()
    {
        if (State != ConnectionState.Connecting)
            return;

        State = ConnectionState.Connected;
        Accepted?.Invoke(this);
    }

    internal void Reject()
    {
        if (State != ConnectionState.Connecting)
            return;

        State = ConnectionState.Rejected;
        Rejected?.Invoke(this);
    }

    internal void KeepAlive(DateTime now)
    {
        if (State != ConnectionState.Connected)
            return;

        if (now - LastSend > KeepAliveInterval)
        {
            SendPing();
            LastSend = now;
        }

        if (now - LastReceive > TimeoutInterval) Close(DisconnectInfo.Timeout);
    }

    internal void Update(DateTime now)
    {
        KeepAlive(now);

        foreach (var channel in _channels.Values)
            channel.Process(now, packet =>
            {
                if (packet.Type == PacketType.Ack) Console.WriteLine("Sending ack");
                else Console.WriteLine("Sending data");

                Send(packet.Data);
            });
    }

    private void HandleRequest(Packet requestPacket)
    {
        if (State != ConnectionState.Connecting)
            return;

        var request = new ConnectionRequest(this, requestPacket);
        Requested?.Invoke(request);
    }

    private void HandleData(Packet packet)
    {
        var flags = packet.Flags;
        var channelId = packet.ChannelId;

        if (!_channels.TryGetValue(channelId, out var channel))
        {
            channel = Channel.Create(channelId, flags);
            _channels.TryAdd(channelId, channel);
        }

        channel.Receive(packet, p => DataReceived?.Invoke(this, p));
    }

    private void HandleAck(Packet packet)
    {
        if (_channels.TryGetValue(packet.ChannelId, out var channel))
        {
            var seq = packet.Sequence;
            channel.ReceiveAck(seq);
            Console.WriteLine("Ack received for seq: " + seq);
        }
    }

    private void SendPing()
    {
        using var pingPacket = Packet.Create(PacketType.Ping, sizeof(long));
        pingPacket.Writer.WriteInt64(Stopwatch.GetTimestamp());
        Send(pingPacket.Data);
    }

    private void SendPong(Packet pingPacket)
    {
        using var pongPacket = Packet.Create(PacketType.Pong, sizeof(long));
        pongPacket.Writer.WriteInt64(pingPacket.Reader.ReadInt64());
        Send(pongPacket.Data);
    }

    private void UpdateRtt(Packet pongPacket)
    {
        Rtt = Stopwatch.GetElapsedTime(pongPacket.Reader.ReadInt64());
        RttUpdated?.Invoke(this, Rtt);
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