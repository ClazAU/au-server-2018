namespace TimeMachine;

public enum GameOverReason : byte
{
    HumansByVote = 0,
    HumansByTask = 1,
    ImpostorByVote = 2,
    ImpostorByKill = 3,
    ImpostorBySabotage = 4,
    Disconnect = 5
}
