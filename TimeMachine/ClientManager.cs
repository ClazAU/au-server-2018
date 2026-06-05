using Hazel;
using Microsoft.Extensions.DependencyInjection;

namespace TimeMachine;

public class ClientManager(IServiceProvider serviceProvider)
{
    public IReadOnlyDictionary<int, Client> Clients => _clients;

    private readonly Dictionary<int, Client> _clients = [];
    private int _lastId;

    public Client AddClient(Connection connection)
    {
        var client = ActivatorUtilities.CreateInstance<Client>(
            serviceProvider,
            Interlocked.Increment(ref _lastId),
            connection);

        _clients.Add(client.Id, client);

        client.Connection.DataReceived += OnDataReceived;

        client.Connection.Disconnected += OnDisconnected;

        return client;

        void OnDataReceived(object? sender, DataReceivedEventArgs dataReceivedEventArgs)
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

        void OnDisconnected(object? sender, DisconnectedEventArgs disconnectedEventArgs)
        {
            client.Connection.DataReceived -= OnDataReceived;
            client.Connection.Disconnected -= OnDisconnected;

            client.OnDisconnected(disconnectedEventArgs);
            _clients.Remove(client.Id);

            disconnectedEventArgs.Recycle();
        }
    }
}
