using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class TrackNameTextGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackItemViewer", "get_trackNameText", 0));
            private static void Postfix(object __instance, ref object __result)
            {
                try
                {
                    string trackId = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
                    string val = __result?.ToString() ?? "<null>";
                    bool isStartingPoint = string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase);
                    if (isStartingPoint)
                    {
                        MelonLogger.Msg($"[Accessor][TrackItemViewer.trackNameText] trackId={trackId} value={val}");
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch]
        private static class ComposerNameTextGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackItemViewer", "get_composerNameText", 0));
            private static void Postfix(object __instance, ref object __result)
            {
                try
                {
                    string trackId = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
                    string val = __result?.ToString() ?? "<null>";
                    bool isStartingPoint = string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase);
                    if (isStartingPoint)
                    {
                        MelonLogger.Msg($"[Accessor][TrackItemViewer.composerNameText] trackId={trackId} value={val}");
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch]
        private static class TrackNameScrollerGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackItemViewer", "get_trackNameScroller", 0));
            private static void Postfix(object __instance, ref object __result)
            {
                try
                {
                    string trackId = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
                    string val = __result is null ? "<null>" : (__result.GetType().FullName ?? __result.ToString() ?? "<unknown-type>");
                    bool isStartingPoint = string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase);
                    if (isStartingPoint)
                    {
                        MelonLogger.Msg($"[Accessor][TrackItemViewer.trackNameScroller] trackId={trackId} valueType={val}");
                    }
                }
                catch { }
            }
        }
    }
}
