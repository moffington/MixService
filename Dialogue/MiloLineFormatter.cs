using System;
using System.Globalization;
using System.Text;

namespace MixService.Dialogue;

/// <summary>
/// Substitutes the small set of placeholders a bank line is allowed to use.
/// Order matters: plural tags are filled before their singular form.
/// </summary>
internal static class MiloLineFormatter
{
    public static string Format(
        string template,
        MiloLineFacts facts)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        string result = template;

        result = Replace(result, "product", facts.ProductName);

        result = Replace(
            result,
            "drop",
            facts.DropLocation.Length > 0
                ? facts.DropLocation
                : "the drop");

        result = Replace(
            result,
            "bricks",
            Number(facts.BrickCount));

        result = Replace(
            result,
            "brick",
            facts.BrickCount == 1
                ? "brick"
                : "bricks");

        result = Replace(
            result,
            "count",
            facts.PackageCount.HasValue
                ? Number(facts.PackageCount.Value)
                : string.Empty);

        result = Replace(
            result,
            "packagings",
            Plural(facts.Packaging, facts.PackageCount));

        result = Replace(
            result,
            "packaging",
            facts.Packaging.ToLowerInvariant());

        result = Replace(
            result,
            "quality",
            facts.Quality.ToLowerInvariant());

        result = Replace(result, "fee", Money(facts.Fee));

        result = Replace(result, "due", Money(facts.Due));

        result = Replace(result, "duration", facts.Duration);

        result = Replace(result, "left", facts.TimeLeft);

        return Tidy(result);
    }

    /// <summary>
    /// Joins dialogue fragments into one message without leaving double spaces
    /// behind when a fragment is optional and came out empty.
    /// </summary>
    public static string Join(params string?[] parts)
    {
        if (parts == null)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (string? part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(part!.Trim());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Words a number of game minutes for a text message: "40m", "6h", "4h 20m".
    /// </summary>
    public static string DescribeDuration(int totalMinutes)
    {
        totalMinutes = Math.Max(0, totalMinutes);

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        if (hours <= 0)
            return $"{minutes}m";

        if (minutes == 0)
            return $"{hours}h";

        return $"{hours}h {minutes}m";
    }

    private static string Replace(
        string value,
        string tag,
        string replacement) =>
        value.Replace(
            "{" + tag + "}",
            replacement ?? string.Empty);

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Money(int? value) =>
        value.HasValue
            ? "$" + value.Value.ToString("N0", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>
    /// Agrees with the package count, so one package never reads as "1 bricks".
    /// </summary>
    private static string Plural(
        string word,
        int? count)
    {
        if (string.IsNullOrWhiteSpace(word))
            return string.Empty;

        string lowered = word.ToLowerInvariant();

        if (count == 1)
            return lowered;

        return lowered.EndsWith("s", StringComparison.Ordinal)
            ? lowered
            : lowered + "s";
    }

    private static string Tidy(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool pendingSpace = false;

        foreach (char character in value)
        {
            if (character == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
