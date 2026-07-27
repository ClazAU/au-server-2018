using System.Net;

namespace TimeMachine;

public sealed class ConnectionRateLimiter(ServerOptions options)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // A connection is created from a single unverified datagram, so the source address can be spoofed freely and the
    // table has to be bounded or it becomes the memory exhaustion vector it is meant to prevent.
    private const int MaxTrackedAddresses = 8192;

    private readonly Dictionary<IPAddress, Attempts> _attempts = [];
    private readonly Lock _attemptsLock = new();

    public bool TryAcceptFrom(IPAddress address)
    {
        var now = DateTimeOffset.UtcNow;

        using var attemptsLock = _attemptsLock.EnterScope();

        var tracked = _attempts.TryGetValue(address, out var attempts);
        if (!tracked || now - attempts.WindowStart >= Window) attempts = new Attempts(now, 0);

        if (attempts.Count >= options.MaxNewConnectionsPerIpPerMinute)
        {
            _attempts[address] = attempts;
            return false;
        }

        attempts = attempts with { Count = attempts.Count + 1 };

        if (tracked)
        {
            _attempts[address] = attempts;
            return true;
        }

        if (_attempts.Count >= MaxTrackedAddresses) RemoveExpired(now);
        if (_attempts.Count < MaxTrackedAddresses) _attempts.Add(address, attempts);

        return true;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        // Dictionary allows removal while enumerating since .NET Core 3.0.
        foreach (var (address, attempts) in _attempts)
        {
            if (now - attempts.WindowStart >= Window) _attempts.Remove(address);
        }
    }

    private readonly record struct Attempts(DateTimeOffset WindowStart, int Count);
}
