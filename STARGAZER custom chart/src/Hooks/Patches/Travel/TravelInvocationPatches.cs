using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class TravelInvocationPatches
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, GetTravelInvocationPatchSpecs());

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPrefix(__originalMethod, __instance, __args);
            private static void Postfix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPostfix(__originalMethod, __instance, __args);
        }
    }
}