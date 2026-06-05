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

    public void HandleMessage(byte[] bytes, MessageReader messageReader, SendOption sendOption)
    {
        var tag = (Tags) messageReader.Tag;
        LogGotMessage(logger, Id, tag);

        switch (tag)
        {
            case Tags.HostGame:
            {
                // TODO: Read client host game message

                var newGameCode = GameCode.GenerateRandom();

                if (!gameManager.TryCreateGame(newGameCode, out var game))
                {
                    // TODO: Log and disconnect
                    break;
                }

                var hostGameMessageWriter = MessageWriter.Get(SendOption.Reliable);

                {
                    hostGameMessageWriter.StartMessage((byte) Tags.HostGame);
                    hostGameMessageWriter.Write(newGameCode.Id);
                    hostGameMessageWriter.EndMessage();
                }

                Connection.Send(hostGameMessageWriter);
                hostGameMessageWriter.Recycle();
                break;
            }
            case Tags.JoinGame:
            {
                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    LogJoinInvalidGameId(logger, Id, gameId);
                    // TODO: Log and disconnect
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log and disconnect
                    break;
                }

                if (!game.TryAddClient(this))
                {
                    // TODO: Log and disconnect
                    break;
                }

                if (game.Host is not {} host)
                {
                    // TODO: Huh? Disconnect and clean up game
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


            case Tags.GameData:
            {
                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    // TODO: Log and disconnect(?)
                    break;
                }

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log and disconnect(?)
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
                var gameId = messageReader.ReadInt32();
                if (!GameCode.TryFromId(gameId, out var gameCode))
                {
                    // TODO: Log and disconnect(?)
                    break;
                }

                var targetClientId = messageReader.ReadPackedInt32();

                if (!gameManager.Games.TryGetValue(gameCode, out var game))
                {
                    // TODO: Log and disconnect(?)
                    break;
                }

                if (!game.Clients.ContainsKey(targetClientId))
                {
                    // TODO: Log and disconnect(?)
                    break;
                }

                if (!clientManager.Clients.TryGetValue(targetClientId, out var targetClient))
                {
                    // TODO: Something went wrong. Client in game but not client manager?
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
        }
    }

    public void OnDisconnected(DisconnectedEventArgs disconnectedEventArgs)
    {
        LogClientDisconnect(logger, disconnectedEventArgs.Exception, Id);
        disconnectedEventArgs.Recycle();

        Connection.Dispose();
    }

    [LoggerMessage(LogLevel.Information, "Client {ClientId} DC")]
    static partial void LogClientDisconnect(ILogger<Client> logger, Exception exception, int clientId);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} message with tag {Tag}")]
    static partial void LogGotMessage(ILogger<Client> logger, int clientId, Tags tag);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} tried to join unknown game with invalid game ID {GameId}")]
    static partial void LogJoinInvalidGameId(ILogger<Client> logger, int clientId, int gameId);

    [LoggerMessage(LogLevel.Debug, "Client {ClientId} joined game {GameCode}")]
    static partial void LogJoinedGame(ILogger<Client> logger, int clientId, GameCode gameCode);
}
