using System.Net;

namespace TimeMachine;

public sealed record ServerOptions(
    IPAddress ListenAddress,
    int ListenPort,
    int MaxConnections,
    int MaxNewConnectionsPerIpPerMinute)
{
    public static ServerOptions FromEnvironment()
        => new(
            ReadIpAddress("AU2018_LISTEN_IP", IPAddress.Any),
            ReadInt32("AU2018_LISTEN_PORT", 22023, ushort.MaxValue),
            // 500 connections is 50 full lobbies, far more than this server is meant to host, while still bounding
            // the state an unauthenticated peer can make us allocate.
            ReadInt32("AU2018_MAX_CONNECTIONS", 500),
            // A client connects once per session; the allowance is high enough for a shared NAT address to reconnect.
            ReadInt32("AU2018_MAX_CONNECTIONS_PER_IP_PER_MINUTE", 30));

    private static IPAddress ReadIpAddress(string variable, IPAddress fallback)
        => IPAddress.TryParse(Environment.GetEnvironmentVariable(variable), out var address) ? address : fallback;

    private static int ReadInt32(string variable, int fallback, int max = int.MaxValue)
        => int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value > 0 && value <= max
            ? value
            : fallback;
}
