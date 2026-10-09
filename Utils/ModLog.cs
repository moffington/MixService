using MelonLoader;
using MixService.Configuration;

namespace MixService.Utils;

public static class ModLog
{
    public static void Debug(string message)
    {
        if (ServiceSettings.DebugLogsEnabled)
            MelonLogger.Msg(message);
    }
}
