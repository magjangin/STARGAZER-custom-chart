using System;
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
        private static bool _initialized = false;

        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            if (_initialized) return;

            try
            {
                int successCount = 0;

                // 1. FromTimeDiffernce 패치 (RVA: 0x437510)
                var fromTimeDiffMethod = AccessTools.Method(typeof(JudgementExtension), "FromTimeDiffernce",
                    new[] { typeof(float), typeof(float), typeof(float).MakeByRefType() });
                if (fromTimeDiffMethod != null)
                {
                    var postfix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(FromTimeDiffernce_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(fromTimeDiffMethod, postfix: new HarmonyMethod(postfix));
                    successCount++;
                    MelonLogger.Msg("[JudgePatch] ✓ FromTimeDiffernce (BLUE 고정) 패치 성공");
                }
                else
                {
                    MelonLogger.Warning("[JudgePatch] FromTimeDiffernce 메서드를 찾지 못했습니다.");
                }

                // 2. TryJudgeHead 패치 (RVA: 0x437CC0)
                var tryJudgeHeadMethod = AccessTools.Method(typeof(JudgementExtension), "TryJudgeHead",
                    new[] { typeof(LaneIndex), typeof(float), typeof(EJudgements).MakeByRefType() });
                if (tryJudgeHeadMethod != null)
                {
                    var postfix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(TryJudgeHead_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(tryJudgeHeadMethod, postfix: new HarmonyMethod(postfix));
                    successCount++;
                    MelonLogger.Msg("[JudgePatch] ✓ TryJudgeHead (BLUE 고정) 패치 성공");
                }
                else
                {
                    MelonLogger.Warning("[JudgePatch] TryJudgeHead 메서드를 찾지 못했습니다.");
                }

                // 3. TravelPlayer.INNER_Result.BufferProcess 패치 (RVA: 0x41A070)
                // TravelPlayer는 Il2CppStargazer.Play 네임스페이스에 위치하며, INNER_Result는 private nested 타입입니다.
                var innerResultType = typeof(TravelPlayer).GetNestedType("INNER_Result", BindingFlags.NonPublic | BindingFlags.Public);
                if (innerResultType != null)
                {
                    var bufferProcessMethod = AccessTools.Method(innerResultType, "BufferProcess", new[] { typeof(TickEventData) });
                    if (bufferProcessMethod != null)
                    {
                        var prefix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(BufferProcess_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(bufferProcessMethod, prefix: new HarmonyMethod(prefix));
                        successCount++;
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

                // 4. JudgementWidget.Watch 패치 (RVA: 0x41CDD0)
                var watchMethod = AccessTools.Method(typeof(JudgementWidget), "Watch", new[] { typeof(TickEventData) });
                if (watchMethod != null)
                {
                    var prefix = typeof(JudgeAllPerfectPatch).GetMethod(nameof(JudgementWidget_Watch_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(watchMethod, prefix: new HarmonyMethod(prefix));
                    successCount++;
                    MelonLogger.Msg("[JudgePatch] ✓ JudgementWidget.Watch (UI 블루 이펙트) 패치 성공");
                }
                else
                {
                    MelonLogger.Warning("[JudgePatch] JudgementWidget.Watch 메서드를 찾지 못했습니다.");
                }

                _initialized = true;
                MelonLogger.Msg($"=================================================");
                MelonLogger.Msg($" [JudgeAllPerfectPatch] 올 퍼펙트 시스템 초기화 완료 ({successCount}/4)");
                MelonLogger.Msg($" AllPerfect 활성 상태: {CustomConfig.AllPerfect}");
                MelonLogger.Msg($"=================================================");
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
