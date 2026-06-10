using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class TrackItemViewerAccessPatches
        {
            private static readonly string[] TargetNames = new[] { "trackNameText", "composerNameText", "trackNameScroller" };

            private static IEnumerable<MethodBase> TargetMethods()
            {
                // Assembly.GetType(name, throwOnError: false)로 안전하게 조회합니다.
                Type? viewerType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType("Il2CppStargazer.Travel.TrackSelector.TrackItemViewer", false))
                    .FirstOrDefault(t => t is not null);

                if (viewerType is null)
                {
                    MelonLogger.Warning("[HookPatch][TrackItemViewer] target type not found: Il2CppStargazer.Travel.TrackSelector.TrackItemViewer");
                    yield break;
                }

                foreach (string name in TargetNames)
                {
                    // 프로퍼티 getter를 시도합니다.
                    PropertyInfo? prop = viewerType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop?.GetGetMethod(true) is MethodInfo getter)
                    {
                        MelonLogger.Msg($"[HookPatch][TrackItemViewer] target getter: {viewerType.FullName}.{getter.Name}");
                        yield return getter;
                        continue;
                    }

                    // get_<name> 이름의 명시적 메서드를 시도합니다.
                    MethodInfo? method = viewerType.GetMethod("get_" + name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method is not null)
                    {
                        MelonLogger.Msg($"[HookPatch][TrackItemViewer] target method: {viewerType.FullName}.{method.Name}");
                        yield return method;
                        continue;
                    }

                    // 마지막 수단으로, UnityEngine.UI.Text 또는 Scroller 계열을 반환하고 이름을 포함하는 메서드를 찾습니다.
                    MethodInfo[] candidates = viewerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var cand in candidates)
                    {
                        if (cand.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && cand.GetParameters().Length == 0)
                        {
                            MelonLogger.Msg($"[HookPatch][TrackItemViewer] fallback target: {viewerType.FullName}.{cand.Name}");
                            yield return cand;
                            break;
                        }
                    }
                }
            }

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPrefix(__originalMethod, __instance, __args);
            private static void Postfix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPostfix(__originalMethod, __instance, __args);
        }
    }
}
