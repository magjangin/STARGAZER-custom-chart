using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static IReadOnlyList<PatchSpec> GetPlayInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "Play", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "PlayStart", 1, "IPlayHandler"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer", "Load", 2, "TravelArgs", "Action"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", "Load", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", "_Load_b__5_0", 1, "Pattern"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler", "BGMPlayChecker", 1, "Single"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler", "Play", 1, "Single"),
                new PatchSpec("Il2CppStargazer.Play.Widgets.CurrentTrackViewer", "Listen", 1),
            };
        }

        private static IReadOnlyList<PatchSpec> GetTrackLoaderInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPattern", 2, "ELevels", "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadBGMClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPreviewClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader", "LoadTracksAsync", 1, "Action"),
            };
        }

        private static IReadOnlyList<PatchSpec> GetSoundInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlayBGM", 2, "AudioClip", "ESoundType"),
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "StopBGM", 0),
                // PlaySFX 오버로드 [0]: PlaySFX(AudioClip clip, ESoundType type)
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlaySFX", 2, "AudioClip", "ESoundType"),
                // PlaySFX 오버로드 [1]: PlaySFX(AudioClip clip, ESoundType type, Single volume)
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlaySFX", 3, "AudioClip", "ESoundType", "Single"),
                // PlaySFX 오버로드 [2]: PlaySFX(Single startTime, AudioClip clip, ESoundType type)
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlaySFX", 3, "Single", "AudioClip", "ESoundType"),
                // PlaySFX 오버로드 [3]: PlaySFX(Single startTime, AudioClip clip, ESoundType type, Single volume)
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlaySFX", 4, "Single", "AudioClip", "ESoundType", "Single"),
            };
        }

        private static IReadOnlyList<PatchSpec> GetTrackSelectorInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackSelector", "Set", 1, "List"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackSelector", "OpenStandby", 1, "String"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackSelector", "ChangeTrackCursor", 1, "Int32"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackListViewer", "MoveCursor", 1, "Int32"),
            };
        }

        private static IReadOnlyList<PatchSpec> GetTravelInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.Travel.Result.PlayInfoViewer", "ShowPlayInfo", 1, "ITravelResultData"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchTrackRecord", 1, "ITrackRecord"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchJacektImage", 1, "Sprite"),
                // 난이도 표시 덮어쓰기용으로 등록했지만, 실측상 SetTrack은 호출되지 않는다(2026-08-08).
                // 커스텀 트랙 판별은 FocusedTrackViewer 쪽(ApplyCustomLevelDisplayToFocusedViewer)이 맡는다.
                // Refresh는 패치하지 않는다: 호출 빈도가 매우 높고, 패치하면 LevelSelector::Refresh에서
                // NullReferenceException이 계속 발생했다(2026-08-08). 재적용은 FetchTrackRecord/
                // FetchJacektImage 훅에서 처리한다.
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "SetTrack", 1, "ITrackData"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelectLogic", "InitializeSelectLogic", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelectLogic", "SingleFlagEvent", 0),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelectLogic", "Build", 0),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelectLogic", "Cancel", 0),
            };
        }

        private static MethodInfo? ResolveTargetMethod(PatchSpec spec)
        {
            Type? type = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly => spec.TypeNames.Select(typeName => assembly.GetType(typeName, false)))
                .FirstOrDefault(candidate => candidate is not null);

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

        // 패치 클래스의 Prepare()에서 부른다. 대상을 하나도 못 찾으면 false를 돌려줘 그 클래스만 건너뛴다.
        //
        // TargetMethods()가 빈 목록을 돌려주는 것으로는 막을 수 없다. Harmony는 그때 어트리뷰트에서
        // 대상을 찾으려다 "Undefined target method"로 던지고(NoteMotionPatches 주석 참고), PatchAll이
        // 그 자리에서 멈춰 나머지 훅까지 전부 안 걸린다(모드 전체 미기동). 게임 업데이트로 로그용 훅
        // 하나만 사라져도 그렇게 되므로, 모든 패치 클래스가 이 함수를 거친다.
        // Prepare()는 클래스 단위로 한 번, 대상마다 한 번씩 더 불리므로 결과를 resolved에 담아 재사용한다.
        private static bool PrepareTargets(List<MethodBase> resolved, IEnumerable<PatchSpec> specs)
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
                    MelonLogger.Warning($"[HookPatch][attr] target not found: {spec.TypeName}.{spec.MethodName}");
                    continue;
                }

                MelonLogger.Msg($"[HookPatch][attr] target: {target.DeclaringType?.FullName}.{target.Name}");
                resolved.Add(target);
            }

            return resolved.Count > 0;
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
                TypeNames = new[] { typeName };
                MethodName = methodName;
                ParameterCount = parameterCount;
                ParameterTypeNameContains = parameterTypeNameContains;
            }

            public string TypeName { get; }
            public string[] TypeNames { get; }
            public string MethodName { get; }
            public int ParameterCount { get; }
            public string[] ParameterTypeNameContains { get; }
        }
    }
}
