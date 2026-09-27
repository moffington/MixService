using System;
using System.Reflection;
using S1API.Products;

namespace MixService.Mixing;

public static class PackagedProductFactory
{
    public static bool TryCreate(
        ProductDefinition product,
        PackagingDefinition packaging,
        int packageCount,
        string qualityName,
        out ProductInstance? instance,
        out string problem)
    {
        instance = null;
        problem = string.Empty;

        if (packageCount <= 0)
        {
            problem = "package count must be positive";
            return false;
        }

        if (!Enum.TryParse(
                qualityName,
                ignoreCase: true,
                out Quality desiredQuality))
        {
            problem =
                $"unknown product quality '{qualityName}'";

            return false;
        }

        instance =
            product.CreatePackagedInstance(
                packageCount,
                packaging);

        if (instance == null)
        {
            problem =
                "S1API failed to create packaged product instance";

            return false;
        }

        /*
         * S1API's public packaged-instance helper currently
         * constructs EQuality.Standard unconditionally.
         *
         * Standard jobs need no adjustment.
         */
        if (instance.Quality == desiredQuality)
            return true;

        if (!TrySetNativeQuality(
                instance,
                desiredQuality,
                out problem))
        {
            instance = null;
            return false;
        }

        /*
         * Read it back through S1API. Don't trust the reflection
         * mutation unless the public wrapper agrees.
         */
        if (instance.Quality != desiredQuality)
        {
            problem =
                $"quality mutation failed; expected " +
                $"{desiredQuality}, got {instance.Quality}";

            instance = null;
            return false;
        }

        return true;
    }

    private static bool TrySetNativeQuality(
        ProductInstance instance,
        Quality quality,
        out string problem)
    {
        problem = string.Empty;

        try
        {
            PropertyInfo? nativeProperty =
                typeof(ProductInstance).GetProperty(
                    "S1ProductInstance",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);

            if (nativeProperty == null)
            {
                problem =
                    "could not locate native ProductItemInstance";
                return false;
            }

            object? native =
                nativeProperty.GetValue(instance);

            if (native == null)
            {
                problem =
                    "native ProductItemInstance was null";
                return false;
            }

            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            for (Type? type = native.GetType();
                 type != null;
                 type = type.BaseType)
            {
                FieldInfo? field =
                    type.GetField(
                        "Quality",
                        flags);

                if (field != null)
                {
                    object nativeQuality =
                        Enum.ToObject(
                            field.FieldType,
                            (int)quality);

                    field.SetValue(
                        native,
                        nativeQuality);

                    return true;
                }

                PropertyInfo? property =
                    type.GetProperty(
                        "Quality",
                        flags);

                if (property?.CanWrite == true)
                {
                    object nativeQuality =
                        Enum.ToObject(
                            property.PropertyType,
                            (int)quality);

                    property.SetValue(
                        native,
                        nativeQuality);

                    return true;
                }
            }

            problem =
                "native ProductItemInstance exposes no writable Quality member";

            return false;
        }
        catch (Exception ex)
        {
            problem =
                $"quality mutation threw {ex.GetType().Name}: " +
                ex.Message;

            return false;
        }
    }
}