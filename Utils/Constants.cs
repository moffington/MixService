namespace MixService.Utils
{
    public static class Constants
    {
        public const string ModName = "MixService";
        public const string ModVersion = "1.0.0";
        public const string ModAuthor = "YourName";
        public const string ModDescription = "Mod description.";

        public const string PreferencesCategory = ModName;

        public static class Defaults
        {
            public const bool DebugLogsEnabled = false;
            public const int PricePerBrick = 1450;
            public const int ProcessingMinutes = 6 * 60;
        }

        public static class Constraints
        {
            public const int MinPricePerBrick = 1;
            public const int MaxPricePerBrick = 100000;
            public const int MinProcessingMinutes = 1;
            public const int MaxProcessingMinutes = 7 * 24 * 60;
        }

        public static class Game
        {
            public const string Studio = "TVGS";
            public const string Name = "Schedule I";
        }
    }
}
