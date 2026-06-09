using System.Net;
using Hazel;
using Hazel.Udp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TimeMachine;

public partial class MatchmakerService(ILogger<MatchmakerService> logger, ClientManager clientManager) : BackgroundService
{
    private const int BroadcastVersion = 36943350; // 2018.9.6.0

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

        var handshakeDataMessageReader = MessageReader.Get(newConnectionEventArgs.HandshakeData);
        var hazelVersion = handshakeDataMessageReader.ReadByte();
        var broadcastVersion = handshakeDataMessageReader.ReadInt32();

        if (broadcastVersion != BroadcastVersion)
        {
            SendDisconnectForIncorrectVersion(connection);
            connection.Close();

            logger.LogDebug("Client tried to join with incompatible broadcast version");
            return;
        }

        var client = clientManager.AddClient(connection);

        newConnectionEventArgs.Recycle();
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
}
