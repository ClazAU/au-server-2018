using System.Net;
using Hazel;
using Hazel.Udp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TimeMachine;

public partial class MatchmakerService(ILogger<MatchmakerService> logger, ClientManager clientManager) : BackgroundService
{
    private const int BroadcastVersion = 36943350; // 2018.9.6.0

    // Hazel version byte followed by the broadcast version.
    private const int HandshakeLength = sizeof(byte) + sizeof(int);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var connectionListener = new UdpConnectionListener(
            new NetworkEndPoint(
                new IPEndPoint(new IPAddress([127, 0, 0, 1]), 22023)))
        {
            // AcceptConnection = 
        };

        connectionListener.NewConnection += HandleNewConnection;
        connectionListener.Start();

        await Task.Delay(Timeout.Infinite, stoppingToken);

        connectionListener.Close();
    }

    private void HandleNewConnection(object? sender, NewConnectionEventArgs newConnectionEventArgs)
    {
        var connection = newConnectionEventArgs.Connection;

        try
        {
            AcceptConnection(connection, newConnectionEventArgs.HandshakeData);
        }
        catch (Exception e)
        {
            LogNewConnectionFailed(logger, e);
            CloseConnection(connection);
        }
        finally
        {
            newConnectionEventArgs.Recycle();
        }
    }

    private void AcceptConnection(Connection connection, byte[] handshakeData)
    {
        var remoteAddress = GetRemoteAddress(connection);

        if (handshakeData is not { Length: >= HandshakeLength })
        {
            LogMalformedHandshake(logger, remoteAddress);
            CloseConnection(connection);
            return;
        }

        var handshakeDataMessageReader = MessageReader.Get(handshakeData);
        var hazelVersion = handshakeDataMessageReader.ReadByte();
        var broadcastVersion = handshakeDataMessageReader.ReadInt32();
        handshakeDataMessageReader.Recycle();

        if (broadcastVersion != BroadcastVersion)
        {
            SendDisconnectForIncorrectVersion(connection);
            CloseConnection(connection);

            LogIncompatibleBroadcastVersion(logger, remoteAddress, broadcastVersion);
            return;
        }

        clientManager.AddClient(connection);
    }

    private static IPAddress? GetRemoteAddress(Connection connection)
        => connection.EndPoint is NetworkEndPoint { EndPoint: IPEndPoint ipEndPoint } ? ipEndPoint.Address : null;

    private void CloseConnection(Connection connection)
    {
        try
        {
            connection.Close();
        }
        catch (Exception e)
        {
            LogCloseFailed(logger, e);
        }
    }

    private static void SendDisconnectForIncorrectVersion(Connection connection)
    {
        var messageWriter = MessageWriter.Get(SendOption.Reliable);
        messageWriter.StartMessage((byte) Tags.JoinGame); // Yep, this is correct
        messageWriter.Write((int) DisconnectReason.IncorrectVersion);
        messageWriter.EndMessage();
        connection.Send(messageWriter);
        messageWriter.Recycle();

        // TODO: Is it safe to close the connection immediately?
        //       Should we wait a little bit and then disconnect if still connected?
        // connection.Close();
    }

    [LoggerMessage(LogLevel.Debug, "Client {RemoteAddress} tried to join with incompatible broadcast version {BroadcastVersion}")]
    static partial void LogIncompatibleBroadcastVersion(ILogger<MatchmakerService> logger, IPAddress? remoteAddress, int broadcastVersion);

    [LoggerMessage(LogLevel.Debug, "Rejected a connection from {RemoteAddress} sending a malformed handshake")]
    static partial void LogMalformedHandshake(ILogger<MatchmakerService> logger, IPAddress? remoteAddress);

    [LoggerMessage(LogLevel.Error, "Failed to accept a new connection")]
    static partial void LogNewConnectionFailed(ILogger<MatchmakerService> logger, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Failed to close a connection")]
    static partial void LogCloseFailed(ILogger<MatchmakerService> logger, Exception exception);
}
