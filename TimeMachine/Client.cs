using Hazel;
using Microsoft.Extensions.Logging;

namespace TimeMachine;

public partial class Client(
    ILogger<Client> logger,
    ClientManager clientManager,
    GameManager gameManager,
    int id,
    Connection connection)
{
    public int Id { get; } = id;
    public Connection Connection { get; } = connection;

    public bool Disconnected { get; private set; }

    public void Disconnect(JoinFailureReason reason)
        => Disconnect((DisconnectReason) reason);

    public void Disconnect(DisconnectReason reason)
    {
        if (Disconnected) return;
        Disconnected = true;

        var messageWriter = MessageWriter.Get(SendOption.Reliable);
        messageWriter.StartMessage((byte) Tags.JoinGame); // Yep, this is correct
        messageWriter.Write((int) reason);
        messageWriter.EndMessage();

        Connection.Send(messageWriter);
        messageWriter.Recycle();

        clientManager.QueueDisconnect(this);
    }

    public void HandleMessage(byte[] bytes, MessageReader messageReader, SendOption sendOption)
    {
        var tag = (Tags) messageReader.Tag;
        LogGotMessage(logger, Id, tag);

        bool CanRead(int byteCount)
        {
            if (messageReader.BytesRemaining() >= byteCount) return true;

            LogTruncatedMessage(logger, Id, tag);
            return false;
        }

        switch (tag)
        {
            case Tags.HostGame:
            {
                // Client doesn't seem to handle disconnect messages from here so don't bother.

                var newGameCode = GameCode.TryFromCode("CLAZ", out var clazGameCode) &&
                                  !gameManager.Games.ContainsKey(clazGameCode)
                    ? clazGameCode
                    : GameCode.GenerateRandom();

                if (!gameManager.TryCreateGame(newGameCode, out var game))
                {
                    // TODO: Log
                    break;
                }

                var hostGameMessageWriter = MessageWriter.Get(SendOption.Reliable);

                {
                    hostGameMessageWriter.StartMessage((byte) Tags.HostGame);
                    hostGameMessageWriter.Write(game.GameCode.Id);
                    hostGameMessageWriter.EndMessage();
                }

                Connection.Send(hostGameMessageWriter);
                hostGameMessageWriter.Recycle();
                break;
            }
            case Tags.JoinGame:
            {
                // TODO: Check if game started or too full or what else? There's an enum for this I think

                if (!CanRead(sizeof(int))) break;

                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    LogJoinInvalidGameId(logger, Id, gameId);
                    Disconnect(JoinFailureReason.GameNotFound);
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log
                    Disconnect(JoinFailureReason.GameNotFound);
                    break;
                }

                if (game.State is not Game.GameState.Lobby)
                {
                    // TODO: Log
                    Disconnect(JoinFailureReason.GameStarted);
                    break;
                }

                if (game.IsFull())
                {
                    // TODO: Log
                    Disconnect(JoinFailureReason.TooManyPlayers);
                    break;
                }

                if (!game.TryAddClient(this))
                {
                    // TODO: Log
                    Disconnect(DisconnectReason.Error);
                    break;
                }

                if (game.Host is not {} host)
                {
                    // TODO: Huh? Log
                    gameManager.CloseGame(gameCode);
                    break;
                }

                LogJoinedGame(logger, Id, gameCode);

                var joinGameMessageWriter = MessageWriter.Get(SendOption.Reliable);

                {
                    joinGameMessageWriter.StartMessage((byte) Tags.JoinedGame);
                    joinGameMessageWriter.Write(gameId);
                    joinGameMessageWriter.Write(Id);
                    joinGameMessageWriter.Write(host.Id);

                    joinGameMessageWriter.WritePacked(game.Clients.Count - 1);
                    foreach (var client in game.Clients.Values)
                    {
                        if (client.Id == Id) continue;

                        joinGameMessageWriter.Write(client.Id);
                    }

                    joinGameMessageWriter.EndMessage();
                }
                
                Connection.Send(joinGameMessageWriter);
                
                joinGameMessageWriter.Clear(SendOption.Reliable);

                {
                    joinGameMessageWriter.StartMessage((byte) Tags.JoinGame);
                    joinGameMessageWriter.Write(gameId);
                    joinGameMessageWriter.Write(Id);
                    joinGameMessageWriter.Write(host.Id);
                    joinGameMessageWriter.EndMessage();
                }

                game.Broadcast(joinGameMessageWriter, this);
                joinGameMessageWriter.Recycle();

                break;
            }
            case Tags.StartGame:
            {
                if (!CanRead(sizeof(int))) break;

                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    // TODO: Log
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log
                    break;
                }

                if (game.Host is not {} host)
                {
                    // TODO: No players? Log
                    break;
                }

                if (Id != host.Id || game.State is not Game.GameState.Lobby)
                {
                    // Nuh uh
                    break;
                }

                game.State = Game.GameState.Started;

                var joinGameMessageWriter = MessageWriter.Get();
                // TODO: I don't know how this makes sense but it's what the base game internal server does, which I
                //       assume works by accident
                joinGameMessageWriter.Write(bytes);

                game.Broadcast(joinGameMessageWriter, this);
                joinGameMessageWriter.Recycle();

                break;
            }
            case Tags.RemoveGame:
            {
                // Base game server implements this, but it's never called by the client?
                break;
            }
            case Tags.RemovePlayer:
            {
                // Among Us 2018.9.6.0 doesn't even have kick/ban functionality,
                // so I'm not gonna bother implementing this
                break;
            }
            case Tags.GameData:
            {
                if (!CanRead(sizeof(int))) break;

                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    // TODO: Log
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log
                    break;
                }

                var gameDataMessageWriter = MessageWriter.Get();
                // TODO: I don't know how this makes sense but it's what the base game internal server does, which I
                //       assume works by accident
                gameDataMessageWriter.Write(bytes);

                game.Broadcast(gameDataMessageWriter, this);
                gameDataMessageWriter.Recycle();

                break;
            }
            case Tags.GameDataTo:
            {
                if (!CanRead(sizeof(int))) break;

                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    // TODO: Log
                    break;
                }

                if (!messageReader.TryReadPackedInt32(out var targetClientId))
                {
                    LogTruncatedMessage(logger, Id, tag);
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log
                    break;
                }

                if (!game.Clients.ContainsKey(targetClientId))
                {
                    // TODO: Log
                    break;
                }

                if (!clientManager.Clients.TryGetValue(targetClientId, out var targetClient))
                {
                    // TODO: Something went wrong. Client in game but not client manager? Log
                    break;
                }

                var gameDataToMessageWriter = MessageWriter.Get(sendOption);
                // TODO: I don't know how this makes sense but it's what the base game internal server does, which I
                //       assume works by accident
                gameDataToMessageWriter.Write(bytes);

                targetClient.Connection.Send(gameDataToMessageWriter);
                gameDataToMessageWriter.Recycle();

                break;
            }
            case Tags.EndGame:
            {
                // Checked up front so a truncated message can't leave the game state half changed.
                if (!CanRead(sizeof(int) + sizeof(byte) + sizeof(bool))) break;

                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    // TODO: Log
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log
                    break;
                }

                if (game.State is not Game.GameState.Started)
                {
                    break;
                }

                if (game.Host is not {} host)
                {
                    // TODO: Huh? Disconnect and clean up game
                    break;
                }

                if (Id != host.Id)
                {
                    // Nuh uh
                    break;
                }

                game.State = Game.GameState.Lobby;

                var reason = (GameOverReason) messageReader.ReadByte();
                var showAd = messageReader.ReadBoolean();

                var endGameMessageWriter = MessageWriter.Get(SendOption.Reliable);
                // TODO: I don't know how this makes sense but it's what the base game internal server does, which I
                //       assume works by accident
                endGameMessageWriter.Write(bytes);

                game.Broadcast(endGameMessageWriter, null);
                endGameMessageWriter.Recycle();

                game.ClearClients();

                break;
            }
            default:
            {
                LogUnknownTag(logger, Id, tag);
                break;
            }
        }
    }

    public void HandleDisconnected(DisconnectedEventArgs disconnectedEventArgs)
    {
        LogClientDisconnect(logger, disconnectedEventArgs.Exception, Id);

        Connection.Dispose();
    }

    [LoggerMessage(LogLevel.Information, "Client {ClientId} disconnected")]
    static partial void LogClientDisconnect(ILogger<Client> logger, Exception exception, int clientId);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} message with tag {Tag}")]
    static partial void LogGotMessage(ILogger<Client> logger, int clientId, Tags tag);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} sent a truncated message with tag {Tag}")]
    static partial void LogTruncatedMessage(ILogger<Client> logger, int clientId, Tags tag);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} tried to join unknown game with invalid game ID {GameId}")]
    static partial void LogJoinInvalidGameId(ILogger<Client> logger, int clientId, int gameId);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} joined game {GameCode}")]
    static partial void LogJoinedGame(ILogger<Client> logger, int clientId, GameCode gameCode);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} sent unknown tag {Tag}")]
    static partial void LogUnknownTag(ILogger<Client> logger, int ClientId, Tags Tag);
}
