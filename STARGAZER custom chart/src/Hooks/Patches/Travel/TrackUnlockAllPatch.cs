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

        // 못 찾은 대상 경고는 씬 로드마다 반복하지 않도록 키별로 한 번만 남긴다.
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            if (!CustomConfig.UnlockAll)
            {
                HookHealth.ReportDisabled("전곡 해금", "설정으로 꺼짐");
                return;
            }

            if (_patched.Count >= TotalPatchCount)
            {
                return;
            }

            int patchedBefore = _patched.Count;
            try
            {
                // 1. INNER_TrackData.get_IsUnlocked
                PatchGetter<bool>(harmony, typeof(TrackLoader.INNER_TrackData), "IsUnlocked", nameof(Get_IsUnlocked_Prefix), "Unlocked");

                // 2. INNER_TrackData.get_LockType
                PatchGetter<ELockType>(harmony, typeof(TrackLoader.INNER_TrackData), "LockType", nameof(Get_LockType_Prefix), "TrackLockType");

                // 3. INNER_TrackData.get_UnlockCost
                PatchGetter<int>(harmony, typeof(TrackLoader.INNER_TrackData), "UnlockCost", nameof(Get_UnlockCost_Prefix), "Cost", "Price");

                // 4. ITrackData.get_IsUnlocked
                PatchGetter<bool>(harmony, typeof(ITrackData), "IsUnlocked", nameof(Get_IsUnlocked_Prefix), "Unlocked");

                // 5. ITrackData.get_LockType
                PatchGetter<ELockType>(harmony, typeof(ITrackData), "LockType", nameof(Get_LockType_Prefix), "TrackLockType");

                // 6. UserDataLoader.INNER_UnlockDataModule.IsUnlocked
                PatchMethod(harmony, typeof(UserDataLoader.INNER_UnlockDataModule), "IsUnlocked", nameof(UnlockDataModule_IsUnlocked_Prefix), typeof(bool), preferredParamCount: 1, "IsTrackUnlocked", "CheckUnlocked");

                // 7. UserDataLoader.INNER_CurrencyModule.GetVolts
                PatchMethod(harmony, typeof(UserDataLoader.INNER_CurrencyModule), "GetVolts", nameof(CurrencyModule_GetVolts_Prefix), typeof(long), preferredParamCount: 0, "GetVolt", "GetCurrency");

                HookHealth.Report("전곡 해금", _patched.Count, TotalPatchCount);
                if (_patched.Count != patchedBefore)
                {
                    MelonLogger.Msg($"[UnlockAllPatch] 전곡 해금 패치 적용 ({_patched.Count}/{TotalPatchCount}개 훅 등록)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UnlockAllPatch] 초기화 중 예외 발생: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void PatchGetter<T>(HarmonyLib.Harmony harmony, Type targetType, string propName, string prefixMethodName, params string[] alternatePropNames)
        {
            string key = $"{targetType.FullName}.get_{propName}";
            if (_patched.Contains(key)) return;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type expectedReturnType = typeof(T);
            MethodInfo? getter = FindGetterByName(targetType, propName, expectedReturnType, Flags);

            if (getter == null && alternatePropNames.Length > 0)
            {
                foreach (string altName in alternatePropNames)
                {
                    getter = FindGetterByName(targetType, altName, expectedReturnType, Flags);
                    if (getter != null)
                    {
                        MelonLogger.Warning($"[UnlockAllPatch][resolve] {key} 대신 대체 프로퍼티 '{getter.Name}' 으로 연결합니다.");
                        break;
                    }
                }
            }

            // ELockType 같은 고유 enum 반환 타입이면 이름이 완전히 바뀌어도 유일한 인자 0개 getter로 폴백한다.
            if (getter == null && expectedReturnType.IsEnum)
            {
                var enumGetters = new List<MethodInfo>();
                foreach (MethodInfo candidate in targetType.GetMethods(Flags))
                {
                    if (candidate.DeclaringType == targetType
                        && candidate.Name.StartsWith("get_", StringComparison.Ordinal)
                        && candidate.GetParameters().Length == 0
                        && candidate.ReturnType == expectedReturnType)
                    {
                        enumGetters.Add(candidate);
                    }
                }

                if (enumGetters.Count == 1)
                {
                    getter = enumGetters[0];
                    MelonLogger.Warning($"[UnlockAllPatch][resolve] {key} 대신 반환 타입({expectedReturnType.Name}) 유일 getter '{getter.Name}' 으로 연결합니다.");
                }
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
            else if (_warned.Add(key))
            {
                MelonLogger.Warning($"[UnlockAllPatch] {key} 메서드를 찾을 수 없습니다.");
            }
        }

        private static MethodInfo? FindGetterByName(Type targetType, string propName, Type expectedReturnType, BindingFlags flags)
        {
            try
            {
                MethodInfo? found = AccessTools.PropertyGetter(targetType, propName);
                if (found != null && found.ReturnType == expectedReturnType)
                {
                    return found;
                }
            }
            catch (AmbiguousMatchException)
            {
            }

            string getterName = $"get_{propName}";
            foreach (MethodInfo candidate in targetType.GetMethods(flags))
            {
                if (string.Equals(candidate.Name, getterName, StringComparison.Ordinal)
                    && candidate.GetParameters().Length == 0
                    && candidate.ReturnType == expectedReturnType)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void PatchMethod(
            HarmonyLib.Harmony harmony,
            Type targetType,
            string methodName,
            string prefixMethodName,
            Type expectedReturnType,
            int? preferredParamCount = null,
            params string[] alternateMethodNames)
        {
            string key = $"{targetType.FullName}.{methodName}";
            if (_patched.Contains(key)) return;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var exactMatches = CollectMatchingMethods(targetType, methodName, expectedReturnType, Flags);

            if (exactMatches.Count == 0 && alternateMethodNames.Length > 0)
            {
                foreach (string altName in alternateMethodNames)
                {
                    exactMatches = CollectMatchingMethods(targetType, altName, expectedReturnType, Flags);
                    if (exactMatches.Count > 0)
                    {
                        MelonLogger.Warning($"[UnlockAllPatch][resolve] {key} 대신 대체 이름 '{altName}' 후보로 연결합니다.");
                        break;
                    }
                }
            }

            MethodInfo? method = null;
            if (exactMatches.Count == 1)
            {
                method = exactMatches[0];
            }
            else if (exactMatches.Count > 1)
            {
                if (preferredParamCount.HasValue)
                {
                    foreach (MethodInfo candidate in exactMatches)
                    {
                        if (candidate.GetParameters().Length == preferredParamCount.Value)
                        {
                            method = candidate;
                            break;
                        }
                    }
                }

                if (method == null)
                {
                    // 오버로드가 여럿인데 기대 인자 수와 맞는 게 없으면 첫 후보를 쓰되, 잘못 걸렸을 가능성을 로그로 남긴다.
                    method = exactMatches[0];
                    if (_warned.Add(key + "#ambiguous"))
                    {
                        MelonLogger.Warning($"[UnlockAllPatch] {key} 오버로드 {exactMatches.Count}개 중 인자 {preferredParamCount?.ToString() ?? "?"}개짜리가 없어 첫 후보({method})로 대체합니다. 게임 업데이트 후 해금이 어긋나면 이 줄부터 확인하세요.");
                    }
                }
            }

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
            else if (_warned.Add(key))
            {
                MelonLogger.Warning($"[UnlockAllPatch] {key} 메서드를 찾을 수 없습니다.");
            }
        }

        private static List<MethodInfo> CollectMatchingMethods(Type targetType, string methodName, Type expectedReturnType, BindingFlags flags)
        {
            var matches = new List<MethodInfo>();
            foreach (MethodInfo candidate in targetType.GetMethods(flags))
            {
                if (string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                    && !candidate.IsGenericMethod
                    && candidate.ReturnType == expectedReturnType)
                {
                    matches.Add(candidate);
                }
            }

            return matches;
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
