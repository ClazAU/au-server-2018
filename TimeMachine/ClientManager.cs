using System.Timers;
using Hazel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace TimeMachine;

public partial class ClientManager
{
    public IReadOnlyDictionary<int, Client> Clients => _clients;

    public event Action<Client>? OnClientDisconnected;

    private readonly Dictionary<int, Client> _clients = [];
    private int _lastId;

    private readonly Queue<DisconnectArgs> _queuedDisconnects = new();
    private readonly Timer _disconnectTimer;

    private readonly ILogger<ClientManager> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ClientManager(ILogger<ClientManager> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        _disconnectTimer = new Timer();

        _disconnectTimer.Elapsed += HandleDisconnectTimer;
    }

    public Client AddClient(Connection connection)
    {
        var client = ActivatorUtilities.CreateInstance<Client>(
            _serviceProvider,
            Interlocked.Increment(ref _lastId),
            connection);

        _clients.Add(client.Id, client);

        client.Connection.DataReceived += HandleDataReceived;
        client.Connection.Disconnected += HandleDisconnected;

        LogClientAdded(_logger, client.Id, connection.EndPoint);

        return client;

        void HandleDataReceived(object? sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            var messageReader = MessageReader.Get(dataReceivedEventArgs.Bytes);

            while (messageReader.Position < messageReader.Length)
            {
                client.HandleMessage(
                    dataReceivedEventArgs.Bytes,
                    messageReader.ReadMessage(),
                    dataReceivedEventArgs.SendOption);
            }

            messageReader.Recycle();
        }

        void HandleDisconnected(object? sender, DisconnectedEventArgs disconnectedEventArgs)
        {
            client.Connection.DataReceived -= HandleDataReceived;
            client.Connection.Disconnected -= HandleDisconnected;

            client.HandleDisconnected(disconnectedEventArgs);
            _clients.Remove(client.Id);
            OnClientDisconnected?.Invoke(client);

            disconnectedEventArgs.Recycle();
        }
    }

    internal void QueueDisconnect(Client client)
    {
        if (client.Connection.State is not ConnectionState.Connected || client.Disconnected) return;

        LogQueuedForDisconnect(client.Id);

        var disconnectAt = DateTimeOffset.UtcNow.AddSeconds(2);
        _queuedDisconnects.Enqueue(new DisconnectArgs(disconnectAt, client));

        if (_queuedDisconnects.Count == 1)
        {
            _disconnectTimer.Interval = 2;
            _disconnectTimer.Start();
        }
    }

    private void HandleDisconnectTimer(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        if (!_queuedDisconnects.TryPeek(out var disconnectArgs)) return;

        var now = DateTimeOffset.UtcNow;
        while (disconnectArgs.DisconnectAt < now + TimeSpan.FromMilliseconds(100))
        {
            var connection = disconnectArgs.Client.Connection;
            if (connection.State is ConnectionState.Connecting or ConnectionState.Connected)
            {
                connection.Close();
                LogForciblyDisconnected(disconnectArgs.Client.Id);
            }

            _queuedDisconnects.Dequeue();

            if (!_queuedDisconnects.TryPeek(out disconnectArgs)) break;
        }

        if (_queuedDisconnects.Count > 0)
        {
            _disconnectTimer.Interval = (disconnectArgs!.DisconnectAt - now).TotalSeconds;
        }
        else
        {
            _disconnectTimer.Stop();
        }
    }

    [LoggerMessage(LogLevel.Information, "Client {ClientId} added from {EndPoint}")]
    static partial void LogClientAdded(ILogger<ClientManager> logger, int clientId, ConnectionEndPoint endPoint);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} queued for disconnect")]
    partial void LogQueuedForDisconnect(int clientId);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} forcibly disconnected")]
    partial void LogForciblyDisconnected(int clientId);

    private record DisconnectArgs(DateTimeOffset DisconnectAt, Client Client);
}
