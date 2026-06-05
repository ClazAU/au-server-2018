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

    public bool TryFindGameByClient(Client client, [NotNullWhen(true)] out Game? game)
    {
        foreach (var candidateGame in _games.Values)
        {
            if (candidateGame.Clients.ContainsKey(client.Id))
            {
                game = candidateGame;
                return true;
            }
        }

        game = null;
        return false;
    }
}
