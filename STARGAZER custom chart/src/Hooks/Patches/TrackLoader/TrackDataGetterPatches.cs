using System.Reflection;
using HarmonyLib;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class TrackDisplayNameGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackDisplayName", 0));
            private static void Postfix(object __instance, ref string __result) => TrackDisplayNamePostfix(__instance, ref __result);
        }

        [HarmonyPatch]
        private static class TrackDisplayNameEnGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackDisplayNameEN", 0));
            private static void Postfix(object __instance, ref string __result) => TrackDisplayNameEnPostfix(__instance, ref __result);
        }

        [HarmonyPatch]
        private static class ArtistDisplayNameGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_ArtistDisplayName", 0));
            private static void Postfix(object __instance, ref string __result) => ArtistDisplayNamePostfix(__instance, ref __result);
        }

        [HarmonyPatch]
        private static class TrackIdGetterPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackID", 0));
            private static void Postfix(object __instance, ref string __result) => TrackIDPostfix(__instance, ref __result);
        }
    }
}