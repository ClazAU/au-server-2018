using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace TimeMachine;

public class GameManager(IServiceProvider serviceProvider)
{
    public IReadOnlyDictionary<GameCode, Game> Games => _games;

    private readonly Dictionary<GameCode, Game> _games = [];

    public bool TryCreateGame(GameCode gameCode, [NotNullWhen(true)] out Game? game)
    {
        if (_games.ContainsKey(gameCode))
        {
            game = null;
            return false;
        }

        game = ActivatorUtilities.CreateInstance<Game>(serviceProvider, gameCode);
        _games.Add(gameCode, game);

        return true;
    }

    public void CloseGame(GameCode gameCode)
    {
        if (!_games.Remove(gameCode, out var game)) return;

        game.State = Game.GameState.Closed;

        foreach (var client in game.Clients.Values)
        {
            client.Disconnect(DisconnectReason.Destroy);
        }

        // TODO: Log

        game.Dispose();
    }
}
