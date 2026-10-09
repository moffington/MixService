using System;
using System.Collections.Generic;

namespace MixService.Mixing;

public sealed class MixRecipeLink
{
    public MixRecipeLink(string sourceProductId, string outputProductId)
    {
        SourceProductId = sourceProductId;
        OutputProductId = outputProductId;
    }

    public string SourceProductId { get; }
    public string OutputProductId { get; }
}

public static class WeedRecipeResolver
{
    private static readonly HashSet<string> BaseProductIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ogkush",
            "sourdiesel",
            "greencrack",
            "granddaddypurple"
        };

    public static bool IsBaseProduct(string productId) =>
        BaseProductIds.Contains(productId);

    public static bool TryResolveBase(
        string targetProductId,
        IReadOnlyList<MixRecipeLink> recipes,
        out string baseProductId,
        out string problem)
    {
        baseProductId = string.Empty;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(targetProductId))
        {
            problem = "target product ID is empty";
            return false;
        }

        var byOutput = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (MixRecipeLink recipe in recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.OutputProductId))
                continue;

            string? source = string.IsNullOrWhiteSpace(
                    recipe.SourceProductId)
                ? null
                : recipe.SourceProductId;

            if (!byOutput.TryGetValue(recipe.OutputProductId,
                    out string? existing))
            {
                byOutput.Add(recipe.OutputProductId, source);
            }
            else if (!string.Equals(existing, source,
                         StringComparison.OrdinalIgnoreCase))
            {
                byOutput[recipe.OutputProductId] = null;
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = targetProductId;

        while (visited.Add(current))
        {
            if (IsBaseProduct(current))
            {
                baseProductId = current;
                return true;
            }

            if (!byOutput.TryGetValue(current, out string? source))
            {
                problem = $"no recipe leads to '{current}'";
                return false;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                problem = $"recipe for '{current}' has no unique source";
                return false;
            }

            current = source;
        }

        problem = $"recipe chain for '{targetProductId}' contains a cycle";
        return false;
    }
}
