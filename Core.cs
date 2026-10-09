using MelonLoader;
using S1API.Lifecycle;
using MixService.Utils;
using System.Collections;
using MixService.NPCs;
using MixService.Compatibility;
using MixService.Configuration;
using S1API.Entities;
using UnityEngine;

[assembly: MelonInfo(typeof(MixService.Core), Constants.ModName, Constants.ModVersion, Constants.ModAuthor)]
[assembly: MelonGame(Constants.Game.Studio, Constants.Game.Name)]

namespace MixService
{
    public sealed class Core : MelonMod
    {
        public static Core? Instance { get; private set; }

        public static bool DebugLogsEnabled => ServiceSettings.DebugLogsEnabled;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ServiceSettings.Initialize();
            GameLifecycle.OnPreLoad += OnPreLoad;
            GameLifecycle.OnLoadComplete += OnLoadComplete;

            LoggerInstance.Msg($"{Constants.ModName} initialized.");
        }

        public override void OnApplicationQuit()
        {
            GameLifecycle.OnPreLoad -= OnPreLoad;
            GameLifecycle.OnLoadComplete -= OnLoadComplete;
            Instance = null;
        }

        private void OnPreLoad()
        {
            ModLog.Debug("Game is preparing to load. Register stable S1API content needed by save data here.");
        }

        private static void OnLoadComplete()
        {
            ModLog.Debug(
                "[MixService] Save loading is complete. " +
                "Initialize systems that depend on the loaded world here.");

            MelonCoroutines.Start(
                InitializeMiloAfterLoad());
        }

        private static IEnumerator InitializeMiloAfterLoad()
        {
            /*
            * beta.6 custom NPCs can undergo a delayed native spawn
            * and conversation replacement after OnLoadComplete.
            *
            * Give that path time to finish before we touch messaging.
            */
            yield return new WaitForSeconds(10f);

            const float timeoutSeconds = 20f;

            float deadline =
                Time.realtimeSinceStartup +
                timeoutSeconds;

            while (Time.realtimeSinceStartup <
                deadline)
            {
                NPC? npc =
                    NPC.Get("mixservice:milo");

                if (npc is MixerContactNpc milo)
                {
                    ModLog.Debug(
                        $"[MixService] Resolved final Milo wrapper. " +
                        $"Icon={(milo.Icon != null ? "ready" : "missing")}");

                    // Conversation cleanup is a compatibility workaround;
                    // restoring the saved job must run even if it fails.
                    try
                    {
                        MiloConversationCleanup.RemoveStaleThreads(milo);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning(
                            $"[MixService] Milo conversation cleanup failed: {ex}");
                    }

                    milo.InitializeAfterWorldLoad();
                    yield break;
                }

                if (npc != null)
                {
                    MelonLogger.Warning(
                        $"[MixService] Milo ID resolved to unexpected " +
                        $"wrapper type '{npc.GetType().FullName}'.");
                }

                yield return new WaitForSeconds(0.5f);
            }

            MelonLogger.Error(
                "[MixService] Could not resolve MixerContactNpc " +
                "within 20 seconds after load.");
        }

    }
}
