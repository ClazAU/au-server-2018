using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace TimeMachine;

public class GameCode : IEquatable<GameCode>
{
    public string Code { get; }
    public int Id { get; }

    public static bool TryFromCode(string code, [NotNullWhen(true)] out GameCode? gameCode)
    {
        if (!TryCodeToInt(code, out var id))
        {
            gameCode = null;
            return false;
        }

        gameCode = new GameCode(code, id.Value);
        return true;
    }

    public static bool TryFromId(int id, [NotNullWhen(true)] out GameCode? gameCode)
    {
        if (!TryIntToCode(id, out var code))
        {
            gameCode = null;
            return false;
        }

        gameCode = new GameCode(code, id);
        return true;
    }

    public static GameCode GenerateRandom()
    {
        const string choices = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var code = RandomNumberGenerator.GetString(choices, 4);
        if (!TryFromCode(code, out var gameCode))
        {
            throw new Exception($"Failed to create `GameCode` from code \"{code}\"");
        }

        return gameCode;
    }

    private GameCode(string code, int id)
    {
        Code = code;
        Id = id;
    }

    private static bool TryCodeToInt(string code, [NotNullWhen(true)] out int? id)
    {
        if (code.Length != 4)
        {
            id = null;
            return false;
        }

        var uppercaseGameCode = code.ToUpperInvariant();

        id = uppercaseGameCode[0] |
                 uppercaseGameCode[1] << 8 |
                 uppercaseGameCode[2] << 16 |
                 uppercaseGameCode[3] << 24;

        return true;
    }

    private static bool TryIntToCode(int id, [NotNullWhen(true)] out string? code)
    {
        char[] characters =
        [
            (char) (id >> 0 & byte.MaxValue),
            (char) (id >> 8 & byte.MaxValue),
            (char) (id >> 16 & byte.MaxValue),
            (char) (id >> 24 & byte.MaxValue)
        ];

        for (var i = 0; i < characters.Length; i++)
        {
            var character = characters[i];

            if (!char.IsAsciiLetter(character))
            {
                code = null;
                return false;
            }

            if (char.IsLower(character)) characters[i] = char.ToUpperInvariant(character);
        }

        code = new string(characters).ToUpperInvariant();
        return true;
    }

    public override string ToString()
        => Code;

    public bool Equals(GameCode? other)
        => other is not null && Id == other.Id;

    public override bool Equals(object? obj)
        => obj is GameCode gameCode && Equals(gameCode);

    public override int GetHashCode()
        => Id;
}
