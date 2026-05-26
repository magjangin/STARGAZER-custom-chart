using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class IAssetBundleLoaderAddBundlePatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStarlike.AssetLoader.IAssetBundleLoader", "AddBundle", 2, "String", "AssetBundle"));

            private static void Prefix(MethodBase __originalMethod, object? __instance, object[]? __args)
            {
                try
                {
                    string key = "<null>";
                    if (__args != null && __args.Length > 0 && __args[0] != null)
                        key = __args[0].ToString() ?? "<null>";

                    object? bundleObj = (__args != null && __args.Length > 1) ? __args[1] : null;
                    string bundleType = bundleObj?.GetType().FullName ?? "<null>";

                    string instanceType = __instance is null ? "<static>" : __instance.GetType().FullName ?? __instance.GetType().Name;
                    MelonLogger.Msg($"[AssetLoader][IAddBundle][PRE] key={key}, bundleType={bundleType}, instance={instanceType}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[AssetLoader][IAddBundle][PRE] logging failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
