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
        private static readonly HashSet<string> FocusedTrackViewerHookMethodNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "SetTrack", "Refresh", "FetchJacketImage", "LoadPreviewBGM",
        };

        private static readonly MethodInfo? FocusedTrackViewerPostfixMethod =
            typeof(GameTypeEnumeratorMod).GetMethod(nameof(FocusedTrackViewerPostfix), BindingFlags.Static | BindingFlags.NonPublic);

        private static bool _focusedTrackViewerPatched;

        [ThreadStatic]
        private static bool _isProbingFocusedTrackViewer;

        private void TryApplyFocusedTrackViewerPatches()
        {
            if (_focusedTrackViewerPatched)
            {
                return;
            }

            try
            {
                Type? type = ResolveGameType("Il2CppStargazer.Travel.FocusedTrackViewer");
                if (type is null)
                {
                    if (LogOnce("FocusedTrackViewerProbe.TypeMissing"))
                    {
                        LoggerInstance.Warning("[FocusedTrackViewerProbe] 로드된 어셈블리에서 Il2CppStargazer.Travel.FocusedTrackViewer 타입을 찾지 못했습니다.");
                    }
                    return;
                }

                // 예전에는 getter/setter를 뺀 "모든" 선언 메서드를 걸었다. 그러면 업데이트로 Update/LateUpdate 같은
                // 매 프레임 메서드가 생기는 순간 매 프레임 리플렉션 열거가 돌게 된다. 실측상 필요한 4개만 명시적으로 건다.
                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => !m.IsGenericMethod && !m.IsAbstract && m.DeclaringType == type)
                    .Where(m => FocusedTrackViewerHookMethodNames.Contains(m.Name))
                    .ToArray();

                foreach (string expected in FocusedTrackViewerHookMethodNames)
                {
                    if (!methods.Any(m => string.Equals(m.Name, expected, StringComparison.Ordinal)))
                    {
                        LoggerInstance.Warning($"[FocusedTrackViewerProbe] {expected} 메서드를 찾지 못했습니다(게임 업데이트로 이름이 바뀌었을 수 있음).");
                    }
                }

                if (FocusedTrackViewerPostfixMethod is null)
                {
                    return;
                }

                int successCount = 0;
                var postfixHarmony = new HarmonyMethod(FocusedTrackViewerPostfixMethod);
                foreach (MethodInfo method in methods)
                {
                    try
                    {
                        RuntimeHarmonyInstance.Patch(method, postfix: postfixHarmony);
                        successCount++;
                    }
                    catch
                    {
                    }
                }

                if (successCount > 0)
                {
                    _focusedTrackViewerPatched = true;
                }

                LoggerInstance.Msg($"[FocusedTrackViewerProbe] Successfully patched {successCount}/{methods.Length} methods of FocusedTrackViewer (postfix only).");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[FocusedTrackViewerProbe] 패치를 적용하지 못했습니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void FocusedTrackViewerPostfix(object? __instance)
        {
            if (_isProbingFocusedTrackViewer || __instance is null)
            {
                return;
            }

            _isProbingFocusedTrackViewer = true;
            try
            {
                // 게임이 난이도 표시를 채운 뒤에 덮어쓴다(표시 전용 — 메타데이터는 건드리지 않는다).
                ApplyCustomLevelDisplayToFocusedViewer(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer][POST] Exception: {ex.Message}");
            }
            finally
            {
                _isProbingFocusedTrackViewer = false;
            }
        }
    }
}
