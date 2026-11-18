using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // 공용 디스패처(HookInvocationSupport.HookPrefix/HookPostfix)에 연결되는 훅.
        //
        // 예전에는 Play/TrackSelector/Travel 세 클래스가 모든 대상에 Prefix와 Postfix를 동시에 붙였다.
        // 그러면 Prefix만 필요한 PlayerBase.Play에도 빈 Postfix가, Postfix만 필요한 TrackSelector.Set에도
        // 빈 Prefix가 붙어 호출마다 트램펄린 + object[] 박싱 + 디스패치가 한 번씩 더 돌았다.
        // 지금은 PatchSpec.Phase로 대상을 나눠 필요한 쪽만 건다(대상 목록: PatchTargetSupport.GetInvocationPatchSpecs).
        [HarmonyPatch]
        private static class InvocationPrefixPatches
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, GetInvocationPatchSpecs(HookPhase.Prefix), "공용 훅(Prefix)");

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPrefix(__originalMethod, __instance, __args);
        }

        [HarmonyPatch]
        private static class InvocationPostfixPatches
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, GetInvocationPatchSpecs(HookPhase.Postfix), "공용 훅(Postfix)");

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static void Postfix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPostfix(__originalMethod, __instance, __args);
        }
    }
}
