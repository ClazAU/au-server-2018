using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace TimeMachine;

public class GameManager(IServiceProvider serviceProvider)
{
    public IReadOnlyDictionary<GameCode, Game> Games => _games;

    // Concurrent: every mutation here runs on a socket callback thread, so two
    // clients sending HostGame at once were racing a plain Dictionary.
    private readonly ConcurrentDictionary<GameCode, Game> _games = new();

    public bool TryCreateGame(GameCode gameCode, [NotNullWhen(true)] out Game? game)
    {
        var candidate = ActivatorUtilities.CreateInstance<Game>(serviceProvider, gameCode);

        // TryAdd, not ContainsKey-then-Add: the old check-then-act let two
        // callers both pass the check and the second overwrite the first,
        // stranding a live game nobody could close.
        if (!_games.TryAdd(gameCode, candidate))
        {
            candidate.Dispose();
            game = null;
            return false;
        }

        game = candidate;
        return true;
    }

    public void CloseGame(GameCode gameCode)
    {
        if (!_games.TryRemove(gameCode, out var game)) return;

        game.State = Game.GameState.Closed;

        foreach (var client in game.Clients.Values)
        {
            client.Disconnect(DisconnectReason.Destroy);
        }

        // TODO: Log

        game.Dispose();
    }
}
