using MixService.Mixing;

namespace MixService.Tests;

internal static class WeedRecipeResolverTests
{
    public static void Run()
    {
        var recipes = new[]
        {
            new MixRecipeLink("granddaddypurple", "firstmix"),
            new MixRecipeLink("firstmix", "secondmix"),
            new MixRecipeLink("secondmix", "thirdmix"),
            new MixRecipeLink("thirdmix", "finalmix"),
            new MixRecipeLink("ogkush", "othermix")
        };

        ExpectBase("finalmix", recipes, "granddaddypurple");
        ExpectBase("othermix", recipes, "ogkush");

        var unrelatedAmbiguity = recipes.Append(
            new MixRecipeLink("greencrack", "othermix")).ToArray();
        ExpectBase("finalmix", unrelatedAmbiguity,
            "granddaddypurple");
        ExpectFailure("othermix", unrelatedAmbiguity);

        ExpectFailure("finalmix", new[]
        {
            new MixRecipeLink("missingintermediate", "finalmix")
        });

        ExpectFailure("finalmix", new[]
        {
            new MixRecipeLink("loop", "finalmix"),
            new MixRecipeLink("finalmix", "loop")
        });
    }

    private static void ExpectBase(
        string target,
        IReadOnlyList<MixRecipeLink> recipes,
        string expected)
    {
        if (!WeedRecipeResolver.TryResolveBase(
                target, recipes, out string actual, out string problem) ||
            !string.Equals(actual, expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception(
                $"{target}: expected {expected}, got {actual}: {problem}");
        }
    }

    private static void ExpectFailure(
        string target,
        IReadOnlyList<MixRecipeLink> recipes)
    {
        if (WeedRecipeResolver.TryResolveBase(
                target, recipes, out _, out _))
        {
            throw new Exception($"{target}: invalid chain was accepted");
        }
    }
}
