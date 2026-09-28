using System;

namespace MixService.Dialogue;

/// <summary>
/// The project's single "already said that" latch. One slot, exact match: emit a
/// value once, swallow it again while nothing else has been emitted, and allow it
/// again once anything else has been said.
///
/// The dialogue picker uses it so two identical lines cannot land back to back,
/// and order handling uses the very same latch so a rejected deposit is not
/// logged over and over. There is deliberately no second cooldown or history
/// mechanism anywhere else.
/// </summary>
public sealed class MiloRepeatGuard
{
    private string? _last;

    /// <summary>
    /// The value this latch is currently holding, or null.
    /// </summary>
    public string? Last => _last;

    /// <summary>
    /// True when the caller should use the value; false when it is a repeat of
    /// what the latch already holds. On true the latch takes the value.
    /// </summary>
    public bool ShouldEmit(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (string.Equals(value, _last, StringComparison.Ordinal))
            return false;

        _last = value;
        return true;
    }

    public void Clear()
    {
        _last = null;
    }
}
