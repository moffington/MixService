using System;
using MelonLoader;
using MelonLoader.Preferences;
using MixService.Utils;

namespace MixService.Configuration;

/// <summary>
/// Standard MelonPreferences entries shared by the config file and phone app.
/// Read values at use time so edits do not require a game restart.
/// </summary>
public static class ServiceSettings
{
    private static MelonPreferences_Entry<int>? _pricePerBrick;
    private static MelonPreferences_Entry<int>? _processingMinutes;
    private static MelonPreferences_Entry<bool>? _debugLogs;

    public static int PricePerBrick => Math.Max(
        Constants.Constraints.MinPricePerBrick,
        Math.Min(Constants.Constraints.MaxPricePerBrick,
            _pricePerBrick?.Value ?? Constants.Defaults.PricePerBrick));

    public static int ProcessingMinutes => Math.Max(
        Constants.Constraints.MinProcessingMinutes,
        Math.Min(Constants.Constraints.MaxProcessingMinutes,
            _processingMinutes?.Value ?? Constants.Defaults.ProcessingMinutes));

    public static bool DebugLogsEnabled =>
        _debugLogs?.Value ?? Constants.Defaults.DebugLogsEnabled;

    public static void Initialize()
    {
        if (_pricePerBrick != null)
            return;

        // The phone app matches this identifier to the MelonInfo name.
        var category = MelonPreferences.CreateCategory(
            Constants.PreferencesCategory, "MixService Settings");

        _pricePerBrick = category.CreateEntry(
            "PricePerBrick",
            Constants.Defaults.PricePerBrick,
            "Price Per Brick ($, New Orders)",
            "Service fee per brick, from $1 to $100,000. Applies to new orders only.",
            validator: new ValueRange<int>(
                Constants.Constraints.MinPricePerBrick,
                Constants.Constraints.MaxPricePerBrick));

        _processingMinutes = category.CreateEntry(
            "ProcessingMinutes",
            Constants.Defaults.ProcessingMinutes,
            "Processing Time (Game Minutes, New Orders)",
            "Processing time from 1 to 10,080 game minutes. 360 is six hours. Applies to new orders only.",
            validator: new ValueRange<int>(
                Constants.Constraints.MinProcessingMinutes,
                Constants.Constraints.MaxProcessingMinutes));

        // Keep the existing key so players retain their debug preference.
        _debugLogs = category.CreateEntry(
            "DebugLogs",
            Constants.Defaults.DebugLogsEnabled,
            "Enable Debug Logs",
            "Show detailed MixService diagnostics immediately. Warnings and errors are always shown.");

        category.SaveToFile(false);
    }
}
