using System.Collections.Concurrent;
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

    // Clients are added and removed straight from the socket callbacks, which run on the thread pool.
    private readonly ConcurrentDictionary<int, Client> _clients = [];
    private int _lastId;

    private readonly Queue<DisconnectArgs> _queuedDisconnects = new();
    private readonly Lock _queuedDisconnectsLock = new();
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

        _clients[client.Id] = client;

        client.Connection.DataReceived += HandleDataReceived;
        client.Connection.Disconnected += HandleDisconnected;

        LogClientAdded(_logger, client.Id, connection.EndPoint);

        return client;

        void HandleDataReceived(object? sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            var messageReader = MessageReader.Get(dataReceivedEventArgs.Bytes);

            try
            {
                while (messageReader.Position < messageReader.Length)
                {
                    var position = messageReader.Position;
                    var subMessageReader = messageReader.ReadMessage();

                    // A datagram that ends mid-header yields no sub-message and leaves the position where it was,
                    // so drop the rest of it rather than spinning on the same bytes.
                    if (subMessageReader is null || messageReader.Position <= position)
                    {
                        LogMalformedDatagram(_logger, client.Id);
                        break;
                    }

                    client.HandleMessage(
                        dataReceivedEventArgs.Bytes,
                        subMessageReader,
                        dataReceivedEventArgs.SendOption);
                }
            }
            catch (Exception e)
            {
                LogMessageHandlingFailed(_logger, e, client.Id);
            }
            finally
            {
                messageReader.Recycle();
            }
        }

        void HandleDisconnected(object? sender, DisconnectedEventArgs disconnectedEventArgs)
        {
            client.Connection.DataReceived -= HandleDataReceived;
            client.Connection.Disconnected -= HandleDisconnected;

            try
            {
                client.HandleDisconnected(disconnectedEventArgs);
                _clients.TryRemove(client.Id, out _);
                OnClientDisconnected?.Invoke(client);
            }
            catch (Exception e)
            {
                LogDisconnectHandlingFailed(_logger, e, client.Id);
            }
            finally
            {
                disconnectedEventArgs.Recycle();
            }
        }
    }

    internal void QueueDisconnect(Client client)
    {
        if (client.Connection.State is not ConnectionState.Connected || client.Disconnected) return;

        LogQueuedForDisconnect(client.Id);

        var disconnectAt = DateTimeOffset.UtcNow.AddSeconds(2);

        using var queuedDisconnectsLock = _queuedDisconnectsLock.EnterScope();

        _queuedDisconnects.Enqueue(new DisconnectArgs(disconnectAt, client));

        if (_queuedDisconnects.Count == 1)
        {
            _disconnectTimer.Interval = 2;
            _disconnectTimer.Start();
        }
    }

    private void HandleDisconnectTimer(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        try
        {
            DisconnectDueClients();
        }
        catch (Exception e)
        {
            LogDisconnectTimerFailed(_logger, e);
        }
    }

    private void DisconnectDueClients()
    {
        var now = DateTimeOffset.UtcNow;
        List<Client> dueClients = [];

        using (_queuedDisconnectsLock.EnterScope())
        {
            while (_queuedDisconnects.TryPeek(out var disconnectArgs) &&
                   disconnectArgs.DisconnectAt < now + TimeSpan.FromMilliseconds(100))
            {
                dueClients.Add(disconnectArgs.Client);
                _queuedDisconnects.Dequeue();
            }

            if (_queuedDisconnects.TryPeek(out var nextDisconnectArgs))
            {
                _disconnectTimer.Interval = (nextDisconnectArgs.DisconnectAt - now).TotalSeconds;
            }
            else
            {
                _disconnectTimer.Stop();
            }
        }

        foreach (var client in dueClients)
        {
            var connection = client.Connection;
            if (connection.State is not (ConnectionState.Connecting or ConnectionState.Connected)) continue;

            try
            {
                connection.Close();
                LogForciblyDisconnected(client.Id);
            }
            catch (Exception e)
            {
                LogDisconnectHandlingFailed(_logger, e, client.Id);
            }
        }
    }

    [LoggerMessage(LogLevel.Information, "Client {ClientId} added from {EndPoint}")]
    static partial void LogClientAdded(ILogger<ClientManager> logger, int clientId, ConnectionEndPoint endPoint);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} queued for disconnect")]
    partial void LogQueuedForDisconnect(int clientId);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} forcibly disconnected")]
    partial void LogForciblyDisconnected(int clientId);

    [LoggerMessage(LogLevel.Debug, "Dropped a malformed datagram from client {ClientId}")]
    static partial void LogMalformedDatagram(ILogger<ClientManager> logger, int clientId);

    [LoggerMessage(LogLevel.Error, "Failed to handle a message from client {ClientId}")]
    static partial void LogMessageHandlingFailed(ILogger<ClientManager> logger, Exception exception, int clientId);

    [LoggerMessage(LogLevel.Error, "Failed to handle the disconnect of client {ClientId}")]
    static partial void LogDisconnectHandlingFailed(ILogger<ClientManager> logger, Exception exception, int clientId);

    [LoggerMessage(LogLevel.Error, "Failed to process the queued disconnects")]
    static partial void LogDisconnectTimerFailed(ILogger<ClientManager> logger, Exception exception);

    private record DisconnectArgs(DateTimeOffset DisconnectAt, Client Client);
}
