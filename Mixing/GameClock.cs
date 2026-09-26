using S1API.GameTime;

namespace MixService.Mixing;

public static class GameClock
{
    public static int CurrentAbsoluteMinutes =>
        TimeManager.ElapsedDays * 1440
        + TimeManager.GetMinutesFrom24HourTime(
            TimeManager.CurrentTime);
}