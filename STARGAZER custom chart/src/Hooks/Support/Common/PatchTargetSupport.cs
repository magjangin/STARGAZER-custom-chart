using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 공용 HookPrefix/HookPostfix(로깅 + 기능 분기)를 거는 대상 목록.
        // 기능이 없는 로깅 전용 대상 중 호출 빈도가 높은 것(BGMPlayChecker, TravelPlayHandler.Play,
        // PlaySFX 4종)과 로그도 기능도 없던 것(TrackSelectLogic.*, OpenStandby, ChangeTrackCursor)은 뺐다.
        // 플레이 중 매 프레임/매 타격마다 인자 배열 할당과 문자열 비교가 돌던 비용이다.
        private static IReadOnlyList<PatchSpec> GetInvocationPatchSpecs()
        {
            return new[]
            {
                // 플레이 진입/로딩
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "Play", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "PlayStart", 1, "IPlayHandler"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer", "Load", 2, "TravelArgs", "Action"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", "Load", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", "_Load_b__5_0", 1, "Pattern"),
                new PatchSpec("Il2CppStargazer.Play.Widgets.CurrentTrackViewer", "Listen", 1),

                // 트랙 데이터 로더
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPattern", 2, "ELevels", "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadBGMClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPreviewClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader", "LoadTracksAsync", 1, "Action"),

                // BGM 진단
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlayBGM", 2, "AudioClip", "ESoundType"),
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "StopBGM", 0),

                // 곡 선택 / 난이도 선택 / 결과
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackSelector", "Set", 1, "List"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackListViewer", "MoveCursor", 1, "Int32"),
                new PatchSpec("Il2CppStargazer.Travel.Result.PlayInfoViewer", "ShowPlayInfo", 1, "ITravelResultData"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchTrackRecord", 1, "ITrackRecord"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchJacektImage", 1, "Sprite"),
                // 난이도 표시 덮어쓰기용 — SetTrack에서 커스텀 트랙인지 판별한다.
                // Refresh는 패치하지 않는다: 호출 빈도가 매우 높고, 패치하면 LevelSelector::Refresh에서
                // NullReferenceException이 계속 발생했다(2026-08-08). 재적용은 FetchTrackRecord/
                // FetchJacektImage 훅에서 처리한다.
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "SetTrack", 1, "ITrackData"),
            };
        }

        private static MethodInfo? ResolveTargetMethod(PatchSpec spec)
        {
            Type? type = FindType(spec.TypeName);
            if (type is null)
            {
                return null;
            }

            MethodInfo[] candidates = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, spec.MethodName, StringComparison.Ordinal))
                .ToArray();

            foreach (MethodInfo method in candidates)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != spec.ParameterCount)
                {
                    continue;
                }

                bool signatureMatches = true;
                for (int i = 0; i < spec.ParameterTypeNameContains.Length; i++)
                {
                    string required = spec.ParameterTypeNameContains[i];
                    string actual = parameters[i].ParameterType.Name;
                    if (actual.IndexOf(required, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        signatureMatches = false;
                        break;
                    }
                }

                if (signatureMatches)
                {
                    return method;
                }
            }

            return candidates.FirstOrDefault(method => method.GetParameters().Length == spec.ParameterCount);
        }

        // Harmony는 TargetMethods()가 빈 목록을 돌려주면 어트리뷰트에서 대상을 찾으려다
        // "Undefined target method"로 던지고, MelonLoader의 PatchAll은 그 자리에서 멈춘다(모드 전체 미기동).
        // 그래서 대상 해석은 Prepare()에서 끝내고, 하나도 못 찾으면 false를 돌려 그 패치 클래스만 건너뛴다.
        // Harmony는 원본마다 Prepare를 다시 부르므로, 이미 해석했으면 바로 true를 돌려준다.
        private static bool PrepareTargets(List<MethodBase> resolved, string label, IEnumerable<PatchSpec> specs)
        {
            if (resolved.Count > 0)
            {
                return true;
            }

            foreach (PatchSpec spec in specs)
            {
                MethodInfo? target = ResolveTargetMethod(spec);
                if (target is null)
                {
                    MelonLogger.Warning($"[HookPatch][{label}] target not found: {spec.TypeName}.{spec.MethodName}");
                    continue;
                }

                // 시그니처 폴백으로 두 스펙이 같은 메서드를 가리키면 같은 훅이 두 번 걸린다.
                if (resolved.Contains(target))
                {
                    continue;
                }

                MelonLogger.Msg($"[HookPatch][{label}] target: {target.DeclaringType?.FullName}.{target.Name}");
                resolved.Add(target);
            }

            if (resolved.Count == 0)
            {
                MelonLogger.Warning($"[HookPatch][{label}] 대상을 하나도 찾지 못해 이 패치를 건너뜁니다.");
                return false;
            }

            return true;
        }

        private static string BuildMethodPatchKey(MethodInfo method)
        {
            string parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
            return $"{method.DeclaringType?.FullName}.{method.Name}({parameters})";
        }

        private sealed class PatchSpec
        {
            public PatchSpec(string typeName, string methodName, int parameterCount, params string[] parameterTypeNameContains)
            {
                TypeName = typeName;
                MethodName = methodName;
                ParameterCount = parameterCount;
                ParameterTypeNameContains = parameterTypeNameContains;
            }

            public string TypeName { get; }
            public string MethodName { get; }
            public int ParameterCount { get; }
            public string[] ParameterTypeNameContains { get; }
        }
    }
}
