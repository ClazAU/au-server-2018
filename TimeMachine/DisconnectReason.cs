namespace TimeMachine;

public enum DisconnectReason : int
{
    ExitGame = 0,
    GameFull = 1,
    GameStarted = 2,
    GameNotFound = 3,
    IncorrectVersion = 5,
    Destroy = 16,
    Error = 17,
    IncorrectGame = 18,
    ServerRequest = 19
}
