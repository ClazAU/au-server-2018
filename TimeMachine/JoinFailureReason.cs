namespace TimeMachine;

public enum JoinFailureReason : byte
{
    TooManyPlayers = 1,
    GameStarted = 2,
    GameNotFound = 3,
    HostNotReady = 4,
    IncorrectVersion = 5
}
