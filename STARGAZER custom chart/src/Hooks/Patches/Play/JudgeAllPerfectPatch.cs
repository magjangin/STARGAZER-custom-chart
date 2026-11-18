using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppStargazer.Play;
using Il2CppStargazer.Travel;

namespace STARGAZER_custom_chart
{
    /// <summary>
    /// STARGAZER (Unity IL2CPP) 올 퍼펙트 (All-Perfect) 판정 조작 패치
    /// 1. JudgementExtension.FromTimeDiffernce: 타이밍 오차 없이 무조건 BLUE(0) 반환
    /// 2. JudgementExtension.TryJudgeHead: 헤드 노트 판정 성공 시 BLUE 고정
    /// 3. TravelPlayer.INNER_Result.BufferProcess: 결과/점수/콤보 100% BLUE 누적 집계
    /// 4. JudgementWidget.Watch: 인게임 이펙트/UI 100% BLUE 연출
    /// </summary>
    public static class JudgeAllPerfectPatch
    {
        private const int TotalPatchCount = 4;

        private const string JudgementExtensionTypeName = "Il2CppStargazer.Play.Extensions.JudgementExtension";
        private const string TravelPlayerTypeName = "Il2CppStargazer.Play.TravelPlayer";
        private const string JudgementWidgetTypeName = "Il2CppStargazer.Play.Widgets.JudgementWidget";

        // 이미 걸린 패치의 이름. Initialize는 OnInitializeMelon / OnLateInitializeMelon / OnSceneWasLoaded에서
        // 여러 번 불리므로, 4개가 모두 걸릴 때까지는 빠진 것만 다시 시도하고 이미 건 것은 다시 걸지 않는다.
        private static readonly HashSet<string> _patched = new HashSet<string>(StringComparer.Ordinal);

        // 같은 경고를 씬 로드마다 반복하지 않도록 키별로 한 번만 남긴다.
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private static bool _disabledLogged;

        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            if (_patched.Count >= TotalPatchCount) return;

            // FromTimeDiffernce/TryJudgeHead/Watch/BufferProcess는 노트 판정마다 불리는 핫패스다.
            // 설정은 게임 재시작 시에만 바뀌므로(config.txt 안내 문구), 꺼져 있으면 아예 걸지 않아 트램펄린 비용을 0으로 만든다.
            if (!CustomConfig.AllPerfect)
            {
                if (!_disabledLogged)
                {
                    _disabledLogged = true;
                    MelonLogger.Msg("[JudgePatch] AllPerfect=false 라 판정 훅을 걸지 않습니다.");
                    HookHealth.ReportDisabled("올 퍼펙트 판정", "설정으로 꺼짐");
                }
                return;
            }

            int patchedBefore = _patched.Count;

            // 대상 타입은 컴파일 타임 typeof 대신 ResolveGameType(네임스페이스 변경 시 짧은 이름 폴백)으로 찾고,
            // 각 패치를 개별 try/catch로 격리해 한 타입이 바뀌어도 나머지 판정 훅이 살아남도록 한다.
            Type? judgeExtType = StargazerCustomChartMod.ResolveGameType(JudgementExtensionTypeName);

            // 1. FromTimeDiffernce 패치 (RVA: 0x437510)
            if (!_patched.Contains(nameof(FromTimeDiffernce_Postfix)))
            {
                TryPatchJudgeTarget(
                    harmony,
                    judgeExtType,
                    JudgementExtensionTypeName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { "FromTimeDiffernce", "FromTimeDifference" },
                    "TimeDiffer",
                    "EJudgements",
                    new[] { ("Single", false), ("Single", false), ("Single", true) },
                    nameof(FromTimeDiffernce_Postfix),
                    isPrefix: false,
                    "BLUE 고정");
            }

            // 2. TryJudgeHead 패치 (RVA: 0x437CC0)
            if (!_patched.Contains(nameof(TryJudgeHead_Postfix)))
            {
                TryPatchJudgeTarget(
                    harmony,
                    judgeExtType,
                    JudgementExtensionTypeName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { "TryJudgeHead" },
                    "JudgeHead",
                    "Boolean",
                    new[] { ("LaneIndex", false), ("Single", false), ("EJudgements", true) },
                    nameof(TryJudgeHead_Postfix),
                    isPrefix: false,
                    "BLUE 고정");
            }

            // 3. TravelPlayer.INNER_Result.BufferProcess 패치 (RVA: 0x41A070)
            if (!_patched.Contains(nameof(BufferProcess_Prefix)))
            {
                Type? innerResultType = ResolveInnerResultType();
                TryPatchJudgeTarget(
                    harmony,
                    innerResultType,
                    $"{TravelPlayerTypeName}+INNER_Result",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    new[] { "BufferProcess" },
                    "Buffer",
                    "Void",
                    new[] { ("TickEventData", false) },
                    nameof(BufferProcess_Prefix),
                    isPrefix: true,
                    "결과/점수 BLUE 집계");
            }

            // 4. JudgementWidget.Watch 패치 (RVA: 0x41CDD0)
            if (!_patched.Contains(nameof(JudgementWidget_Watch_Prefix)))
            {
                Type? widgetType = StargazerCustomChartMod.ResolveGameType(JudgementWidgetTypeName);
                TryPatchJudgeTarget(
                    harmony,
                    widgetType,
                    JudgementWidgetTypeName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    new[] { "Watch" },
                    "Watch",
                    "Void",
                    new[] { ("TickEventData", false) },
                    nameof(JudgementWidget_Watch_Prefix),
                    isPrefix: true,
                    "UI 블루 이펙트");
            }

            HookHealth.Report("올 퍼펙트 판정", _patched.Count, TotalPatchCount);
            if (_patched.Count >= TotalPatchCount)
            {
                MelonLogger.Msg($"=================================================");
                MelonLogger.Msg($" [JudgeAllPerfectPatch] 올 퍼펙트 시스템 초기화 완료 ({_patched.Count}/{TotalPatchCount})");
                MelonLogger.Msg($" AllPerfect 활성 상태: {CustomConfig.AllPerfect}");
                MelonLogger.Msg($"=================================================");
            }
            else if (_patched.Count != patchedBefore || WarnOnceKey("partial"))
            {
                MelonLogger.Warning($"[JudgePatch] 올 퍼펙트 패치가 {_patched.Count}/{TotalPatchCount}개만 적용됐습니다. 다음 초기화 시점(후기 초기화/씬 로드)에 빠진 패치만 다시 시도합니다.");
            }
        }

        private static Type? ResolveInnerResultType()
        {
            Type? direct = StargazerCustomChartMod.ResolveGameType($"{TravelPlayerTypeName}+INNER_Result");
            if (direct is not null)
            {
                return direct;
            }

            Type? travelPlayerType = StargazerCustomChartMod.ResolveGameType(TravelPlayerTypeName);
            if (travelPlayerType is null)
            {
                return null;
            }

            var (nested, usedFallback) = PatchResolutionLogic.ResolveNestedType(travelPlayerType, "INNER_Result", "Result");
            if (nested is not null && usedFallback && WarnOnceKey("resolve.INNER_Result"))
            {
                MelonLogger.Warning($"[JudgePatch][resolve] TravelPlayer+INNER_Result 대신 중첩 타입 '{nested.Name}' 으로 연결합니다.");
            }

            return nested;
        }

        private static void TryPatchJudgeTarget(
            HarmonyLib.Harmony harmony,
            Type? targetType,
            string targetTypeLabel,
            BindingFlags flags,
            string[] exactNames,
            string preferredNameFragment,
            string expectedReturnTypeName,
            (string TypeName, bool IsByRef)[] expectedParams,
            string hookMethodName,
            bool isPrefix,
            string description)
        {
            try
            {
                if (targetType is null)
                {
                    WarnOnce($"type.{targetTypeLabel}", $"[JudgePatch] {targetTypeLabel} 타입을 찾지 못했습니다.");
                    return;
                }

                var (method, fallbackReason) = PatchResolutionLogic.ResolveMethodByShortSignature(
                    targetType,
                    flags,
                    exactNames,
                    preferredNameFragment,
                    expectedReturnTypeName,
                    expectedParams);

                if (method is null)
                {
                    WarnOnce(exactNames[0], $"[JudgePatch] {targetType.Name}.{exactNames[0]} 메서드를 찾지 못했습니다.");
                    return;
                }

                if (fallbackReason is not null && WarnOnceKey($"resolve.{targetType.Name}.{exactNames[0]}"))
                {
                    MelonLogger.Warning($"[JudgePatch][resolve] {targetType.Name}.{exactNames[0]} 대체 해석({fallbackReason}): {method.Name}");
                }

                MethodInfo? hookMethod = typeof(JudgeAllPerfectPatch).GetMethod(hookMethodName, BindingFlags.Static | BindingFlags.NonPublic);
                if (hookMethod is null)
                {
                    return;
                }

                if (isPrefix)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(hookMethod));
                }
                else
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(hookMethod));
                }

                _patched.Add(hookMethodName);
                MelonLogger.Msg($"[JudgePatch] ✓ {targetType.Name}.{method.Name} ({description}) 패치 성공");
            }
            catch (Exception ex)
            {
                WarnOnce($"patch.{hookMethodName}", $"[JudgePatch] {targetTypeLabel}.{exactNames[0]} 패치 중 예외: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool WarnOnceKey(string key) => _warned.Add(key);

        private static void WarnOnce(string key, string message)
        {
            if (WarnOnceKey(key))
            {
                MelonLogger.Warning(message);
            }
        }

        private static bool ShouldApplyAllPerfect()
        {
            return CustomConfig.AllPerfect
                && (!CustomConfig.AllPerfectCustomOnly || StargazerCustomChartMod.IsCustomChartPlayActive);
        }

        // Harmony 위치 기반 바인딩(__0, __2)을 사용해 게임 업데이트로 파라미터 이름이 바뀌거나
        // IL2CPP 메타데이터에서 이름이 스트립돼도 안전하게 바인딩된다.
        private static void FromTimeDiffernce_Postfix(ref EJudgements __result, ref float __2)
        {
            if (ShouldApplyAllPerfect())
            {
                __result = EJudgements.BLUE; // 무조건 최고 판정 (BLUESTAR = 0)
                __2 = 0.0f; // 오차 0ms (FAST/SLOW 오프셋 제거)
            }
        }

        private static void TryJudgeHead_Postfix(bool __result, ref EJudgements __2)
        {
            if (ShouldApplyAllPerfect() && __result)
            {
                __2 = EJudgements.BLUE;
            }
        }

        private static void BufferProcess_Prefix(TickEventData __0)
        {
            if (ShouldApplyAllPerfect() && __0 != null)
            {
                __0.judge = EJudgements.BLUE;
            }
        }

        private static void JudgementWidget_Watch_Prefix(TickEventData __0)
        {
            if (ShouldApplyAllPerfect() && __0 != null)
            {
                __0.judge = EJudgements.BLUE;
            }
        }
    }
}
