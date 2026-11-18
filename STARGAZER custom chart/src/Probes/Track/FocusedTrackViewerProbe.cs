using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        private static readonly HashSet<string> FocusedTrackViewerHookMethodNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "SetTrack", "Refresh", "FetchJacketImage", "LoadPreviewBGM",
        };

        // 게임 원본의 오타(LevelSelector.FetchJacektImage)가 이 타입에도 있을 수 있고, 수정/되돌림될 수도 있어
        // 같은 역할의 철자 변형을 하나로 취급한다(키 = 기대 이름, 값 = 허용하는 대체 이름).
        private static readonly Dictionary<string, string[]> FocusedTrackViewerAlternateNames = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SetTrack"] = new[] { "SetFocusedTrack", "BindTrack" },
            ["Refresh"] = new[] { "UpdateView", "RefreshView" },
            ["FetchJacketImage"] = new[] { "FetchJacektImage", "SetJacketImage", "UpdateJacketImage" },
            ["LoadPreviewBGM"] = new[] { "LoadPreviewClip", "LoadPreviewBgm", "PlayPreviewBGM" },
        };

        private static readonly MethodInfo? FocusedTrackViewerPostfixMethod =
            typeof(StargazerCustomChartMod).GetMethod(nameof(FocusedTrackViewerPostfix), BindingFlags.Static | BindingFlags.NonPublic);

        // 이미 패치된 메서드와 충족된 논리 역할 이름. 일부만 걸렸을 때 다음 씬 로드에서 빠진 역할만 다시 시도하되,
        // 같은 메서드를 중복 패치하지 않는다(JudgeAllPerfectPatch / TrackUnlockAllPatch와 동일한 방식).
        private static readonly HashSet<MethodBase> PatchedFocusedTrackViewerMethods = new HashSet<MethodBase>();
        private static readonly HashSet<string> PatchedFocusedTrackViewerRoles = new HashSet<string>(StringComparer.Ordinal);

        [ThreadStatic]
        private static bool _isProbingFocusedTrackViewer;

        private void TryApplyFocusedTrackViewerPatches()
        {
            if (PatchedFocusedTrackViewerRoles.Count >= FocusedTrackViewerHookMethodNames.Count)
            {
                return;
            }

            try
            {
                Type? type = ResolveGameType("Il2CppStargazer.Travel.FocusedTrackViewer");
                if (type is null)
                {
                    HookHealth.Report("선택 화면 난이도(FocusedTrackViewer)", 0, FocusedTrackViewerHookMethodNames.Count, "타입 미발견");
                    if (LogOnce("FocusedTrackViewerProbe.TypeMissing"))
                    {
                        LoggerInstance.Warning("[FocusedTrackViewerProbe] 로드된 어셈블리에서 Il2CppStargazer.Travel.FocusedTrackViewer 타입을 찾지 못했습니다.");
                    }
                    return;
                }

                if (FocusedTrackViewerPostfixMethod is null)
                {
                    return;
                }

                // 예전에는 getter/setter를 뺀 "모든" 선언 메서드를 걸었다. 그러면 업데이트로 Update/LateUpdate 같은
                // 매 프레임 메서드가 생기는 순간 매 프레임 리플렉션 열거가 돌게 된다. 실측상 필요한 4개만 명시적으로 건다.
                MethodInfo[] declaredMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => !m.IsGenericMethod && !m.IsAbstract && m.DeclaringType == type)
                    .ToArray();

                int newlyPatched = 0;
                var postfixHarmony = new HarmonyMethod(FocusedTrackViewerPostfixMethod);

                foreach (string expectedRole in FocusedTrackViewerHookMethodNames)
                {
                    MethodInfo[] roleMatches = declaredMethods
                        .Where(m => MatchesExpectedHookName(m.Name, expectedRole))
                        .ToArray();

                    if (roleMatches.Length == 0)
                    {
                        if (LogOnce($"FocusedTrackViewerProbe.Missing.{expectedRole}"))
                        {
                            LoggerInstance.Warning($"[FocusedTrackViewerProbe] {expectedRole} 메서드를 찾지 못했습니다(게임 업데이트로 이름이 바뀌었을 수 있음).");
                        }
                        continue;
                    }

                    foreach (MethodInfo method in roleMatches)
                    {
                        if (PatchedFocusedTrackViewerMethods.Contains(method))
                        {
                            PatchedFocusedTrackViewerRoles.Add(expectedRole);
                            continue;
                        }

                        if (!string.Equals(method.Name, expectedRole, StringComparison.Ordinal)
                            && LogOnce($"FocusedTrackViewerProbe.Resolve.{expectedRole}.{method.Name}"))
                        {
                            LoggerInstance.Warning($"[HookPatch][resolve] FocusedTrackViewer.{expectedRole} 대신 대체 이름 '{method.Name}' 으로 연결합니다.");
                        }

                        try
                        {
                            RuntimeHarmonyInstance.Patch(method, postfix: postfixHarmony);
                            PatchedFocusedTrackViewerMethods.Add(method);
                            PatchedFocusedTrackViewerRoles.Add(expectedRole);
                            newlyPatched++;
                        }
                        catch (Exception ex)
                        {
                            if (LogOnce($"FocusedTrackViewerProbe.PatchFail.{method.Name}"))
                            {
                                LoggerInstance.Warning($"[FocusedTrackViewerProbe] {method.Name} 패치 실패: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                }

                HookHealth.Report("선택 화면 난이도(FocusedTrackViewer)", PatchedFocusedTrackViewerRoles.Count, FocusedTrackViewerHookMethodNames.Count);
                if (newlyPatched > 0)
                {
                    LoggerInstance.Msg($"[FocusedTrackViewerProbe] Successfully patched {PatchedFocusedTrackViewerRoles.Count}/{FocusedTrackViewerHookMethodNames.Count} roles ({PatchedFocusedTrackViewerMethods.Count} methods, postfix only).");
                }
                else if (PatchedFocusedTrackViewerRoles.Count == 0 && LogOnce("FocusedTrackViewerProbe.NoneApplied"))
                {
                    LoggerInstance.Warning("[FocusedTrackViewerProbe] 패치된 메서드가 없습니다. 다음 씬 로드에서 다시 시도합니다.");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[FocusedTrackViewerProbe] 패치를 적용하지 못했습니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool MatchesExpectedHookName(string actual, string expected)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return true;
            }

            if (FocusedTrackViewerAlternateNames.TryGetValue(expected, out string[]? alternates))
            {
                foreach (string alternate in alternates)
                {
                    if (string.Equals(actual, alternate, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
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
