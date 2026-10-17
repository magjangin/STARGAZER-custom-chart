using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppStargazer.Play;
using Il2CppStargazer.Play.Extensions;
using Il2CppStargazer.Play.Widgets;
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

        // 이미 걸린 패치의 이름. Initialize는 OnInitializeMelon / OnLateInitializeMelon / OnSceneWasLoaded에서
        // 여러 번 불리므로, 4개가 모두 걸릴 때까지는 빠진 것만 다시 시도하고 이미 건 것은 다시 걸지 않는다.
        private static readonly HashSet<string> _patched = new HashSet<string>(StringComparer.Ordinal);

        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            if (_patched.Count >= TotalPatchCount) return;

            try
            {
                // 1. FromTimeDiffernce 패치 (RVA: 0x437510)
                if (!_patched.Contains(nameof(FromTimeDiffernce_Postfix)))
                {
                    var fromTimeDiffMethod = AccessTools.Method(typeof(JudgementExtension), "FromTimeDiffernce",
                        new[] { typeof(float), typeof(float), typeof(float).MakeByRefType() });
                    if (fromTimeDiffMethod != null)
                    {
                        var postfix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(FromTimeDiffernce_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(fromTimeDiffMethod, postfix: new HarmonyMethod(postfix));
                        _patched.Add(nameof(FromTimeDiffernce_Postfix));
                        MelonLogger.Msg("[JudgePatch] ✓ FromTimeDiffernce (BLUE 고정) 패치 성공");
                    }
                    else
                    {
                        MelonLogger.Warning("[JudgePatch] FromTimeDiffernce 메서드를 찾지 못했습니다.");
                    }
                }

                // 2. TryJudgeHead 패치 (RVA: 0x437CC0)
                if (!_patched.Contains(nameof(TryJudgeHead_Postfix)))
                {
                    var tryJudgeHeadMethod = AccessTools.Method(typeof(JudgementExtension), "TryJudgeHead",
                        new[] { typeof(LaneIndex), typeof(float), typeof(EJudgements).MakeByRefType() });
                    if (tryJudgeHeadMethod != null)
                    {
                        var postfix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(TryJudgeHead_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(tryJudgeHeadMethod, postfix: new HarmonyMethod(postfix));
                        _patched.Add(nameof(TryJudgeHead_Postfix));
                        MelonLogger.Msg("[JudgePatch] ✓ TryJudgeHead (BLUE 고정) 패치 성공");
                    }
                    else
                    {
                        MelonLogger.Warning("[JudgePatch] TryJudgeHead 메서드를 찾지 못했습니다.");
                    }
                }

                // 3. TravelPlayer.INNER_Result.BufferProcess 패치 (RVA: 0x41A070)
                // TravelPlayer는 Il2CppStargazer.Play 네임스페이스에 위치하며, INNER_Result는 private nested 타입입니다.
                if (!_patched.Contains(nameof(BufferProcess_Prefix)))
                {
                    var innerResultType = typeof(TravelPlayer).GetNestedType("INNER_Result", BindingFlags.NonPublic | BindingFlags.Public);
                    if (innerResultType != null)
                    {
                        var bufferProcessMethod = AccessTools.Method(innerResultType, "BufferProcess", new[] { typeof(TickEventData) });
                        if (bufferProcessMethod != null)
                        {
                            var prefix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(BufferProcess_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                            harmony.Patch(bufferProcessMethod, prefix: new HarmonyMethod(prefix));
                            _patched.Add(nameof(BufferProcess_Prefix));
                            MelonLogger.Msg("[JudgePatch] ✓ TravelPlayer.INNER_Result.BufferProcess (결과/점수 BLUE 집계) 패치 성공");
                        }
                        else
                        {
                            MelonLogger.Warning("[JudgePatch] BufferProcess 메서드를 찾지 못했습니다.");
                        }
                    }
                    else
                    {
                        MelonLogger.Warning("[JudgePatch] TravelPlayer+INNER_Result 타입을 찾지 못했습니다.");
                    }
                }

                // 4. JudgementWidget.Watch 패치 (RVA: 0x41CDD0)
                if (!_patched.Contains(nameof(JudgementWidget_Watch_Prefix)))
                {
                    var watchMethod = AccessTools.Method(typeof(JudgementWidget), "Watch", new[] { typeof(TickEventData) });
                    if (watchMethod != null)
                    {
                        var prefix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(JudgementWidget_Watch_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(watchMethod, prefix: new HarmonyMethod(prefix));
                        _patched.Add(nameof(JudgementWidget_Watch_Prefix));
                        MelonLogger.Msg("[JudgePatch] ✓ JudgementWidget.Watch (UI 블루 이펙트) 패치 성공");
                    }
                    else
                    {
                        MelonLogger.Warning("[JudgePatch] JudgementWidget.Watch 메서드를 찾지 못했습니다.");
                    }
                }

                if (_patched.Count >= TotalPatchCount)
                {
                    MelonLogger.Msg($"=================================================");
                    MelonLogger.Msg($" [JudgeAllPerfectPatch] 올 퍼펙트 시스템 초기화 완료 ({_patched.Count}/{TotalPatchCount})");
                    MelonLogger.Msg($" AllPerfect 활성 상태: {CustomConfig.AllPerfect}");
                    MelonLogger.Msg($"=================================================");
                }
                else
                {
                    MelonLogger.Warning($"[JudgePatch] 올 퍼펙트 패치가 {_patched.Count}/{TotalPatchCount}개만 적용됐습니다. 다음 초기화 시점(후기 초기화/씬 로드)에 빠진 패치만 다시 시도합니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[JudgePatch] 초기화 중 예외 발생: {ex}");
            }
        }

        private static void FromTimeDiffernce_Postfix(ref EJudgements __result, ref float judgeTiming)
        {
            if (CustomConfig.AllPerfect)
            {
                __result = EJudgements.BLUE; // 무조건 최고 판정 (BLUESTAR = 0)
                judgeTiming = 0.0f; // 오차 0ms (FAST/SLOW 오프셋 제거)
            }
        }

        private static void TryJudgeHead_Postfix(bool __result, ref EJudgements result)
        {
            if (CustomConfig.AllPerfect && __result)
            {
                result = EJudgements.BLUE;
            }
        }

        private static void BufferProcess_Prefix(TickEventData te)
        {
            if (CustomConfig.AllPerfect && te != null)
            {
                te.judge = EJudgements.BLUE;
            }
        }

        private static void JudgementWidget_Watch_Prefix(TickEventData t)
        {
            if (CustomConfig.AllPerfect && t != null)
            {
                t.judge = EJudgements.BLUE;
            }
        }
    }
}
