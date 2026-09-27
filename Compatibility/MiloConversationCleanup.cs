using System;
using System.Linq;
using System.Reflection;
using MelonLoader;
using MixService.NPCs;

namespace MixService.Compatibility;

public static class MiloConversationCleanup
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    private const BindingFlags StaticFlags =
        BindingFlags.Static |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    private static int
    RemoveStaleFromCollection(
        string label,
        object? collection,
        MixerContactNpc milo,
        IntPtr currentPointer)
    {
        if (collection == null)
        {
            MelonLogger.Msg(
                $"[MiloCleanup] {label}: <null>");

            return 0;
        }

        int count =
            GetCollectionCount(
                collection);

        MelonLogger.Msg(
            $"[MiloCleanup] {label}: count={count}");

        if (count <= 0)
            return 0;

        int removed = 0;

        for (int i = count - 1;
            i >= 0;
            i--)
        {
            object? conversation =
                GetCollectionItem(
                    collection,
                    i);

            if (conversation == null)
                continue;

            // If S1API replaced Milo's conversation while we were
            // inspecting the UI, stop rather than trust a stale identity.
            object? attachedConversation =
                GetCurrentConversation(milo);

            IntPtr? attachedPointer =
                attachedConversation == null
                    ? null
                    : GetNativePointer(attachedConversation);

            if (attachedPointer != currentPointer)
            {
                MelonLogger.Warning(
                    $"[MiloCleanup] {label}: Milo conversation changed; skipping remaining entries.");

                return removed;
            }

            IntPtr? candidatePointer =
                GetNativePointer(conversation);

            // A name alone cannot distinguish the two Milo threads.
            // Only remove a candidate with a known, different native ID.
            if (candidatePointer == null ||
                candidatePointer == IntPtr.Zero ||
                candidatePointer == currentPointer)
                continue;

            string contactName =
                GetStringMember(
                    conversation,
                    "contactName",
                    "ContactName");

            if (!string.Equals(
                    contactName,
                    "Milo",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MelonLogger.Msg(
                $"[MiloCleanup] {label}: removing stale " +
                $"Milo {NativeIdentity(conversation)} " +
                $"at index {i}.");

            if (!RemoveAt(
                    collection,
                    i))
            {
                MelonLogger.Warning(
                    $"[MiloCleanup] {label}: RemoveAt({i}) failed.");

                continue;
            }

            removed++;
        }

        return removed;
    }

    public static void RemoveStaleThreads(
        MixerContactNpc milo)
    {
        try
        {
            object? currentConversation =
                GetCurrentConversation(milo);

            if (currentConversation == null)
            {
                MelonLogger.Warning(
                    "[MiloCleanup] Current Milo conversation is null.");

                return;
            }

            IntPtr? currentPointer =
                GetNativePointer(currentConversation);

            if (currentPointer == null ||
                currentPointer == IntPtr.Zero)
            {
                MelonLogger.Warning(
                    "[MiloCleanup] Cannot identify the live Milo conversation; skipping cleanup.");

                return;
            }

            Type? appType =
                FindLoadedType(
                    "Il2CppScheduleOne.UI.Phone.Messages.MessagesApp")
                ?? FindLoadedType(
                    "ScheduleOne.UI.Phone.Messages.MessagesApp");

            if (appType == null)
            {
                MelonLogger.Warning(
                    "[MiloCleanup] MessagesApp type not found.");

                return;
            }

            object? app =
                GetStaticMemberValue(
                    appType,
                    "Instance",
                    "instance",
                    "_instance");

            if (app == null)
            {
                MelonLogger.Warning(
                    "[MiloCleanup] MessagesApp instance not found.");

                return;
            }

            object? conversations =
                GetStaticMemberValue(
                    appType,
                    "Conversations");

            object? activeConversations =
                GetStaticMemberValue(
                    appType,
                    "ActiveConversations");

            object? unreadConversations =
                GetMemberValue(
                    app,
                    "unreadConversations");

            int totalRemoved = 0;

            totalRemoved += RemoveStaleFromCollection(
                "MessagesApp.Conversations",
                conversations,
                milo,
                currentPointer.Value);

            totalRemoved += RemoveStaleFromCollection(
                "MessagesApp.ActiveConversations",
                activeConversations,
                milo,
                currentPointer.Value);

            totalRemoved += RemoveStaleFromCollection(
                "MessagesApp.unreadConversations",
                unreadConversations,
                milo,
                currentPointer.Value);

            if (totalRemoved > 0)
            {
                MelonLogger.Msg(
                    $"[MiloCleanup] Removed {totalRemoved} stale " +
                    $"Milo collection reference(s).");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning(
                $"[MiloCleanup] Cleanup failed: " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static object?
        GetCurrentConversation(
            MixerContactNpc milo)
    {
        object? nativeNpc =
            GetMemberValue(
                milo,
                "S1NPC");

        if (nativeNpc == null)
            return null;

        return GetMemberValue(
            nativeNpc,
            "MSGConversation");
    }

    private static bool
        RemoveAt(
            object collection,
            int index)
    {
        Type? type =
            collection.GetType();

        while (type != null)
        {
            MethodInfo? method =
                type
                    .GetMethods(
                        InstanceFlags |
                        BindingFlags.DeclaredOnly)
                    .FirstOrDefault(
                        m =>
                            m.Name == "RemoveAt"
                            && m.GetParameters().Length == 1);

            if (method != null)
            {
                try
                {
                    method.Invoke(
                        collection,
                        new object[] { index });

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            type =
                type.BaseType;
        }

        return false;
    }

    private static object?
        GetCollectionItem(
            object collection,
            int index)
    {
        Type? type =
            collection.GetType();

        while (type != null)
        {
            PropertyInfo? property =
                type
                    .GetProperties(
                        InstanceFlags |
                        BindingFlags.DeclaredOnly)
                    .FirstOrDefault(
                        p =>
                            p.Name == "Item"
                            && p.GetIndexParameters().Length == 1);

            if (property != null)
            {
                try
                {
                    return property.GetValue(
                        collection,
                        new object[] { index });
                }
                catch
                {
                    // Try get_Item below.
                }
            }

            MethodInfo? method =
                type
                    .GetMethods(
                        InstanceFlags |
                        BindingFlags.DeclaredOnly)
                    .FirstOrDefault(
                        m =>
                            m.Name == "get_Item"
                            && m.GetParameters().Length == 1);

            if (method != null)
            {
                try
                {
                    return method.Invoke(
                        collection,
                        new object[] { index });
                }
                catch
                {
                    // Try base type.
                }
            }

            type =
                type.BaseType;
        }

        return null;
    }

    private static int
        GetCollectionCount(
            object collection)
    {
        object? value =
            GetMemberValue(
                collection,
                "Count");

        if (value == null)
            return -1;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return -1;
        }
    }

    private static object?
        GetMemberValue(
            object target,
            params string[] names)
    {
        Type? type =
            target.GetType();

        while (type != null)
        {
            foreach (string name in names)
            {
                PropertyInfo? property =
                    type.GetProperty(
                        name,
                        InstanceFlags |
                        BindingFlags.DeclaredOnly);

                if (property != null &&
                    property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return property.GetValue(target);
                    }
                    catch
                    {
                    }
                }

                FieldInfo? field =
                    type.GetField(
                        name,
                        InstanceFlags |
                        BindingFlags.DeclaredOnly);

                if (field != null)
                {
                    try
                    {
                        return field.GetValue(target);
                    }
                    catch
                    {
                    }
                }
            }

            type =
                type.BaseType;
        }

        return null;
    }

    private static object?
        GetStaticMemberValue(
            Type rootType,
            params string[] names)
    {
        Type? type =
            rootType;

        while (type != null)
        {
            foreach (string name in names)
            {
                PropertyInfo? property =
                    type.GetProperty(
                        name,
                        StaticFlags |
                        BindingFlags.DeclaredOnly);

                if (property != null &&
                    property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return property.GetValue(null);
                    }
                    catch
                    {
                    }
                }

                FieldInfo? field =
                    type.GetField(
                        name,
                        StaticFlags |
                        BindingFlags.DeclaredOnly);

                if (field != null)
                {
                    try
                    {
                        return field.GetValue(null);
                    }
                    catch
                    {
                    }
                }
            }

            type =
                type.BaseType;
        }

        return null;
    }

    private static string
        GetStringMember(
            object target,
            params string[] names)
    {
        return GetMemberValue(
                   target,
                   names)?.ToString()
               ?? string.Empty;
    }

    private static Type?
        FindLoadedType(
            string fullName)
    {
        foreach (Assembly assembly
                 in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type =
                    assembly.GetType(
                        fullName,
                        false,
                        false);

                if (type != null)
                    return type;
            }
            catch
            {
            }
        }

        return null;
    }

    private static IntPtr?
        GetNativePointer(
            object value)
    {
        object? pointer =
            GetMemberValue(
                value,
                "Pointer");

        return pointer is IntPtr ptr
            ? ptr
            : null;
    }

    private static string
        NativeIdentity(
            object value)
    {
        IntPtr? pointer =
            GetNativePointer(value);

        return pointer.HasValue
            ? $"0x{pointer.Value.ToInt64():X}"
            : "<managed>";
    }
}
