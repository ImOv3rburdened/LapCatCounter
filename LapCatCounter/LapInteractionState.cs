namespace LapCatCounter;

public enum LapInteractionRole
{
    None = 0,
    SittingInOtherLap = 1,
    OtherSittingInMyLap = 2,
}

public enum LapInteractionStatus
{
    None = 0,
    Starting = 1,
    Active = 2,
    Ending = 3,
}
