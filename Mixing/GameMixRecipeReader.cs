using System;
using System.Collections.Generic;
using System.Reflection;
using S1API.Items;
using S1API.Products;

namespace MixService.Mixing;

/// <summary>
/// Reads the active game's mix registry. Its native wrappers differ between
/// Mono and IL2CPP, so only this boundary uses reflection.
/// </summary>
public static class GameMixRecipeReader
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static bool TryReadWeedRecipes(
        out IReadOnlyList<MixRecipeLink> recipes,
        out string problem)
    {
        var links = new List<MixRecipeLink>();
        recipes = links;
        problem = string.Empty;

        try
        {
            Type? managerType = FindManagerType();
            if (managerType == null)
            {
                problem = "game product manager type is unavailable";
                return false;
            }

            PropertyInfo? instanceProperty = managerType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static |
                BindingFlags.FlattenHierarchy);
            object? manager = instanceProperty?.GetValue(null);
            if (manager == null)
            {
                problem = "game product manager has not loaded";
                return false;
            }

            object? nativeRecipes = ReadMember(manager, "mixRecipes");
            if (nativeRecipes == null)
            {
                problem = "game mix recipe list is unavailable";
                return false;
            }

            int count = ReadCount(nativeRecipes);
            for (int i = 0; i < count; i++)
            {
                object recipe = ReadAt(nativeRecipes, i);
                object output = RequireMember(recipe, "Product");
                string outputId = ReadItemId(RequireMember(output, "Item"));

                if (ItemManager.GetDefinition(outputId)
                    is not ProductDefinition outputDefinition ||
                    outputDefinition.PrimaryDrugType != DrugType.Marijuana)
                {
                    continue;
                }

                object ingredients = RequireMember(recipe, "Ingredients");
                if (ReadCount(ingredients) != 2)
                {
                    links.Add(new MixRecipeLink(string.Empty, outputId));
                    continue;
                }

                string firstId;
                string secondId;
                try
                {
                    firstId = ReadIngredientId(ReadAt(ingredients, 0));
                    secondId = ReadIngredientId(ReadAt(ingredients, 1));
                }
                catch (InvalidOperationException)
                {
                    links.Add(new MixRecipeLink(string.Empty, outputId));
                    continue;
                }

                ProductDefinition? firstProduct =
                    ItemManager.GetDefinition(firstId) as ProductDefinition;
                ProductDefinition? secondProduct =
                    ItemManager.GetDefinition(secondId) as ProductDefinition;

                if ((firstProduct == null) == (secondProduct == null))
                {
                    links.Add(new MixRecipeLink(string.Empty, outputId));
                    continue;
                }

                ProductDefinition sourceProduct = firstProduct ?? secondProduct!;
                links.Add(new MixRecipeLink(
                    sourceProduct.PrimaryDrugType == DrugType.Marijuana
                        ? sourceProduct.ID
                        : string.Empty,
                    outputId));
            }

            return true;
        }
        catch (Exception ex)
        {
            problem = $"could not read loaded mix recipes: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static Type? FindManagerType()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(
                "Il2CppScheduleOne.Product.ProductManager", false) ??
                assembly.GetType("ScheduleOne.Product.ProductManager", false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static string ReadIngredientId(object ingredient)
    {
        object items = RequireMember(ingredient, "Items");
        if (ReadCount(items) != 1)
            throw new InvalidOperationException("mix ingredient has multiple item choices");

        return ReadItemId(ReadAt(items, 0));
    }

    private static string ReadItemId(object item)
    {
        string? id = ReadMember(item, "ID") as string;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("mix recipe item has no ID");
        return id;
    }

    private static object RequireMember(object target, string name) =>
        ReadMember(target, name) ??
        throw new InvalidOperationException($"mix recipe member '{name}' is missing");

    private static object? ReadMember(object target, string name)
    {
        Type type = target.GetType();
        PropertyInfo? property = type.GetProperty(name, InstanceFlags);
        if (property != null)
            return property.GetValue(target);

        FieldInfo? field = type.GetField(name, InstanceFlags);
        return field?.GetValue(target);
    }

    private static int ReadCount(object list) =>
        ReadMember(list, "Count") is int count
            ? count
            : throw new InvalidOperationException("mix recipe list has no count");

    private static object ReadAt(object list, int index)
    {
        PropertyInfo? indexer = list.GetType().GetProperty("Item", InstanceFlags);
        return indexer?.GetValue(list, new object[] { index }) ??
            throw new InvalidOperationException("mix recipe list index failed");
    }
}
