using System.Diagnostics;
using Hazel;

namespace TimeMachine;

public class Game(GameCode gameCode)
{
    public GameCode GameCode { get; } = gameCode;

    public Client? Host { get; private set; }

    public IReadOnlyDictionary<int, Client> Clients => _clients;

    private readonly Dictionary<int, Client> _clients = [];

    private readonly Lock _clientsLock = new();

    public bool TryAddClient(Client client)
    {
        using var clientsLock = _clientsLock.EnterScope();

        if (_clients.Count >= 10 || !_clients.TryAdd(client.Id, client)) return false;

        if (_clients.Count == 1) Host = client;

        return true;
    }

    public bool TryRemoveClient(int clientId)
    {
        using var clientsLock = _clientsLock.EnterScope();

        var didRemove = _clients.Remove(clientId);
        if (!didRemove) return false;

        Host = _clients.Count == 0 ? null : _clients.Values.First();
        return true;
    }

    public void Broadcast(MessageWriter messageWriter, Client sender)
    {
        using var clientsLock = _clientsLock.EnterScope();

        foreach (var client in _clients.Values)
        {
            if (client.Id == sender.Id) continue;

            try
            {
                client.Connection.Send(messageWriter);
            }
            catch (Exception e)
            {
                // TODO: Log
            }
        }
    }
}
