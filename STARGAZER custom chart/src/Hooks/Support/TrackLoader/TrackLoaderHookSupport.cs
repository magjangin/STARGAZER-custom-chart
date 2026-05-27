using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void TryPatchTrackLoaderOnLoadedCallback(object[] args)
        {
            if (args.Length == 0 || args[0] is null)
            {
                return;
            }

            try
            {
                object callback = args[0];
                Type delegateType = callback.GetType();

                MethodInfo? invokeMethod = delegateType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                if (invokeMethod is null)
                {
                    return;
                }

                string invokeMethodKey = BuildMethodPatchKey(invokeMethod);
                if (!TrackLoaderCallbackPatchedMethods.Add(invokeMethodKey))
                {
                    return;
                }

                RuntimeHarmonyInstance.Patch(
                    invokeMethod,
                    postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(TrackLoaderOnLoadedCallbackPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLoader] failed to patch onLoaded callback: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void TrackLoaderOnLoadedCallbackPostfix(MethodBase __originalMethod, object[]? __args)
        {
            try
            {
                object[] args = __args ?? Array.Empty<object>();
                if (args.Length == 0 || args[0] is null)
                {
                    return;
                }

                if (!TrackLoaderListLogged)
                {
                    TrackLoaderListLogged = true;
                    int loadedCount = EnumerateCollectionItems(args[0], 2048).Count();
                    MelonLogger.Msg($"[TrackLoader] onLoaded callback invoked. Loaded {loadedCount} tracks successfully.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLoader] failed to inspect loaded track list: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}