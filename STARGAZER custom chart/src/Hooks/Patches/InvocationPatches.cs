using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 공용 PRE/POST 훅. 대상 목록은 GetInvocationPatchSpecs(PatchTargetSupport.cs) 하나로 관리한다.
        // 예전에는 영역별로 파일 5개(Play/Sound/TrackLoader/TrackSelector/Travel)에 똑같은 클래스가 있었다.
        [HarmonyPatch]
        private static class InvocationPatches
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, "Invocation", GetInvocationPatchSpecs());

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPrefix(__originalMethod, __instance, __args);

            private static void Postfix(MethodBase __originalMethod, object? __instance, object[]? __args) => HookPostfix(__originalMethod, __instance, __args);
        }

        // 에셋 번들 등록 진단 로그. 인터페이스와 구현 클래스 양쪽을 건다.
        [HarmonyPatch]
        private static class AssetBundleLoaderAddBundlePatch
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, "AssetLoader", new[]
            {
                new PatchSpec("Il2CppStarlike.AssetLoader.IAssetBundleLoader", "AddBundle", 2, "String", "AssetBundle"),
                new PatchSpec("Il2CppStarlike.AssetLoader.StarlikeAssetBundleLoader", "AddBundle", 2, "String", "AssetBundle"),
            });

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args)
            {
                try
                {
                    // IL2CPP 인터페이스는 인터롭에서 클래스로 생성되므로 IsInterface가 아니라 이름으로 구분한다.
                    string tag = string.Equals(__originalMethod.DeclaringType?.Name, "IAssetBundleLoader", StringComparison.Ordinal)
                        ? "IAddBundle"
                        : "AddBundle";
                    string key = "<null>";
                    if (__args != null && __args.Length > 0 && __args[0] != null)
                        key = __args[0].ToString() ?? "<null>";

                    object? bundleObj = (__args != null && __args.Length > 1) ? __args[1] : null;
                    string bundleType = bundleObj?.GetType().FullName ?? "<null>";

                    string instanceType = __instance is null ? "<static>" : __instance.GetType().FullName ?? __instance.GetType().Name;
                    MelonLogger.Msg($"[AssetLoader][{tag}][PRE] key={key}, bundleType={bundleType}, instance={instanceType}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[AssetLoader][PRE] logging failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
