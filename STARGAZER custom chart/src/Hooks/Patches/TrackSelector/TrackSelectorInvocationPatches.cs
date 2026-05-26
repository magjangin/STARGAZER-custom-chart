using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class TrackSelectorInvocationPatches
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (PatchSpec spec in GetTrackSelectorInvocationPatchSpecs())
                {
                    MethodInfo? target = ResolveTargetMethod(spec);
                    if (target is null)
                    {
                        MelonLogger.Warning($"[HookPatch][attr] target not found: {spec.TypeName}.{spec.MethodName}");
                        continue;
                    }

                    MelonLogger.Msg($"[HookPatch][attr] target: {target.DeclaringType?.FullName}.{target.Name}");
                    yield return target;
                }
            }

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPrefix(__originalMethod, __instance, __args);
            private static void Postfix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPostfix(__originalMethod, __instance, __args);
        }
    }
}