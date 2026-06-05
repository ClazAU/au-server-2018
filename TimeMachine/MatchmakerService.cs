using System.Net;
using Hazel;
using Hazel.Udp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace TimeMachine;

public class MatchmakerService(ILogger<MatchmakerService> logger, ClientManager clientManager) : BackgroundService
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

        connectionListener.NewConnection += OnNewConnection;
        connectionListener.Start();

        await Task.Delay(Timeout.Infinite, stoppingToken);

        connectionListener.Close();
    }

    private void OnNewConnection(object? sender, NewConnectionEventArgs newConnectionEventArgs)
    {
        var connection = newConnectionEventArgs.Connection;

        var handshakeDataMessageReader = MessageReader.Get(newConnectionEventArgs.HandshakeData);
        var whatIsThis = handshakeDataMessageReader.ReadByte();
        var broadcastVersion = handshakeDataMessageReader.ReadInt32();

        if (broadcastVersion != BroadcastVersion)
        {
            SendIncorrectVersion(connection);
            Log.Debug("Client tried to join with incompatible broadcast version");
            return;
        }

        var client = clientManager.AddClient(connection);
        Log.Information("Client {ClientId} added from {ConnectionEndPoint}", client.Id, connection.EndPoint);

        newConnectionEventArgs.Recycle();
    }

    private void SendIncorrectVersion(Connection connection)
    {
        var messageWriter = MessageWriter.Get(SendOption.Reliable);
        messageWriter.StartMessage(1); // TODO: Enum
        messageWriter.Write(5); // TODO: Wat dis?
        messageWriter.EndMessage();
        connection.Send(messageWriter);
        messageWriter.Recycle();
    }
}
