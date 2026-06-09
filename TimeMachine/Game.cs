using Hazel;

namespace TimeMachine;

public class Game : IDisposable
{
    public GameCode GameCode { get; }

    public Client? Host { get; private set; }

    public GameState State { get; set; }

    public IReadOnlyDictionary<int, Client> Clients => _clients;

    public const int Capacity = 10;

    private bool _disposed;

    private readonly Dictionary<int, Client> _clients = [];

    private readonly Lock _clientsLock = new();

    private readonly ClientManager _clientManager;
    private readonly GameManager _gameManager;

    public Game(ClientManager clientManager, GameManager gameManager, GameCode gameCode)
    {
        _clientManager = clientManager;
        _gameManager = gameManager;

        GameCode = gameCode;

        _clientManager.OnClientDisconnected += HandleClientDisconnected;
    }

    public bool IsFull()
        => _clients.Count >= Capacity;

    public bool TryAddClient(Client client)
    {
        using var clientsLock = _clientsLock.EnterScope();

        if (IsFull() || !_clients.TryAdd(client.Id, client)) return false;

        if (_clients.Count == 1) Host = client;

        return true;
    }

    public bool TryRemoveClient(int clientId)
    {
        using var clientsLock = _clientsLock.EnterScope();

        var didRemove = _clients.Remove(clientId);
        if (!didRemove) return false;

        // If the host is null then something has gone terribly wrong,
        // so shrug it off and just set it here :)
        if (Host == null || clientId == Host.Id)
        {
            Host = _clients.Count == 0 ? null : _clients.Values.First();
        }

        return true;
    }

    public void ClearClients()
    {
        using var clientsLock = _clientsLock.EnterScope();

        _clients.Clear();
        Host = null;
    }

    public void Broadcast(MessageWriter messageWriter, Client? sender)
    {
        using var clientsLock = _clientsLock.EnterScope();

        foreach (var client in _clients.Values)
        {
            if (client.Id == sender?.Id) continue;

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _clientManager.OnClientDisconnected -= HandleClientDisconnected;

        ClearClients();

        GC.SuppressFinalize(this);
    }

    private void HandleClientDisconnected(Client client)
    {
        using var clientsLock = _clientsLock.EnterScope();

        TryRemoveClient(client.Id);

        if (Host == null)
        {
            // Close the lobby I guess
            _gameManager.CloseGame(GameCode);
            return;
        }

        var messageWriter = MessageWriter.Get(SendOption.Reliable);
        {
            messageWriter.StartMessage((byte) Tags.RemovePlayer);
            messageWriter.Write(GameCode.Id);
            messageWriter.Write(client.Id);
            messageWriter.Write(Host.Id);
            messageWriter.EndMessage();
        }

        Broadcast(messageWriter, null);
        messageWriter.Recycle();

        // TODO: Log
    }

    public enum GameState
    {
        Lobby,
        Started,
        Closed
    }
}
