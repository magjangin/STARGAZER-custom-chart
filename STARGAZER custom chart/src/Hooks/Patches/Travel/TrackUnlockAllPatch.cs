using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppStargazer;
using Il2CppStargazer.Travel;

namespace STARGAZER_custom_chart
{
    /// <summary>
    /// STARGAZER 전곡 해금 및 볼트(VOLT) 제한 해제 패치 (오프라인 영구 보존용)
    /// 1. TrackLoader.INNER_TrackData.get_IsUnlocked: 항상 true 반환 (잠금 해제)
    /// 2. TrackLoader.INNER_TrackData.get_LockType: 항상 ELockType.NONE 반환 (VOLT/DLC/어드벤처 자물쇠 아이콘 제거)
    /// 3. TrackLoader.INNER_TrackData.get_UnlockCost: 항상 0 반환
    /// 4. ITrackData.get_IsUnlocked: 항상 true 반환
    /// 5. ITrackData.get_LockType: 항상 ELockType.NONE 반환
    /// 6. UserDataLoader.INNER_UnlockDataModule.IsUnlocked: 항상 true 반환
    /// 7. UserDataLoader.INNER_CurrencyModule.GetVolts: 볼트 999,999 반환
    /// </summary>
    public static class TrackUnlockAllPatch
    {
        private const int TotalPatchCount = 7;
        private static readonly HashSet<string> _patched = new HashSet<string>(StringComparer.Ordinal);

        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            if (!CustomConfig.UnlockAll || _patched.Count >= TotalPatchCount)
            {
                return;
            }

            try
            {
                // 1. INNER_TrackData.get_IsUnlocked
                PatchGetter<bool>(harmony, typeof(TrackLoader.INNER_TrackData), "IsUnlocked", nameof(Get_IsUnlocked_Prefix));

                // 2. INNER_TrackData.get_LockType
                PatchGetter<ELockType>(harmony, typeof(TrackLoader.INNER_TrackData), "LockType", nameof(Get_LockType_Prefix));

                // 3. INNER_TrackData.get_UnlockCost
                PatchGetter<int>(harmony, typeof(TrackLoader.INNER_TrackData), "UnlockCost", nameof(Get_UnlockCost_Prefix));

                // 4. ITrackData.get_IsUnlocked
                PatchGetter<bool>(harmony, typeof(ITrackData), "IsUnlocked", nameof(Get_IsUnlocked_Prefix));

                // 5. ITrackData.get_LockType
                PatchGetter<ELockType>(harmony, typeof(ITrackData), "LockType", nameof(Get_LockType_Prefix));

                // 6. UserDataLoader.INNER_UnlockDataModule.IsUnlocked
                PatchMethod(harmony, typeof(UserDataLoader.INNER_UnlockDataModule), "IsUnlocked", nameof(UnlockDataModule_IsUnlocked_Prefix));

                // 7. UserDataLoader.INNER_CurrencyModule.GetVolts
                PatchMethod(harmony, typeof(UserDataLoader.INNER_CurrencyModule), "GetVolts", nameof(CurrencyModule_GetVolts_Prefix));

                MelonLogger.Msg($"[UnlockAllPatch] 전곡 해금 패치 적용 완료 ({_patched.Count}개 훅 등록)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UnlockAllPatch] 초기화 중 예외 발생: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void PatchGetter<T>(HarmonyLib.Harmony harmony, Type targetType, string propName, string prefixMethodName)
        {
            string key = $"{targetType.FullName}.get_{propName}";
            if (_patched.Contains(key)) return;

            MethodInfo? getter = AccessTools.PropertyGetter(targetType, propName);
            if (getter == null)
            {
                getter = targetType.GetMethod($"get_{propName}", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (getter != null)
            {
                MethodInfo? prefix = typeof(TrackUnlockAllPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix != null)
                {
                    harmony.Patch(getter, prefix: new HarmonyMethod(prefix));
                    _patched.Add(key);
                    MelonLogger.Msg($"[UnlockAllPatch] ✓ {key} 패치 성공");
                }
            }
            else
            {
                MelonLogger.Warning($"[UnlockAllPatch] {key} 메서드를 찾을 수 없습니다.");
            }
        }

        private static void PatchMethod(HarmonyLib.Harmony harmony, Type targetType, string methodName, string prefixMethodName)
        {
            string key = $"{targetType.FullName}.{methodName}";
            if (_patched.Contains(key)) return;

            MethodInfo? method = AccessTools.Method(targetType, methodName);
            if (method != null)
            {
                MethodInfo? prefix = typeof(TrackUnlockAllPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix != null)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                    _patched.Add(key);
                    MelonLogger.Msg($"[UnlockAllPatch] ✓ {key} 패치 성공");
                }
            }
            else
            {
                MelonLogger.Warning($"[UnlockAllPatch] {key} 메서드를 찾을 수 없습니다.");
            }
        }

        private static bool Get_IsUnlocked_Prefix(ref bool __result)
        {
            if (CustomConfig.UnlockAll)
            {
                __result = true;
                return false;
            }
            return true;
        }

        private static bool Get_LockType_Prefix(ref ELockType __result)
        {
            if (CustomConfig.UnlockAll)
            {
                __result = ELockType.NONE;
                return false;
            }
            return true;
        }

        private static bool Get_UnlockCost_Prefix(ref int __result)
        {
            if (CustomConfig.UnlockAll)
            {
                __result = 0;
                return false;
            }
            return true;
        }

        private static bool UnlockDataModule_IsUnlocked_Prefix(ref bool __result)
        {
            if (CustomConfig.UnlockAll)
            {
                __result = true;
                return false;
            }
            return true;
        }

        private static bool CurrencyModule_GetVolts_Prefix(ref long __result)
        {
            if (CustomConfig.UnlockAll)
            {
                __result = 999999L;
                return false;
            }
            return true;
        }
    }
}
