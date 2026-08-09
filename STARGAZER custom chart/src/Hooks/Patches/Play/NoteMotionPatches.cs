using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 노트 연출용 훅. 다른 훅과 달리 매 프레임 노트마다 불리는 초고빈도 지점이라
        // 공용 HookPrefix/HookPostfix(로깅 포함)를 쓰지 않고 전용 얇은 패치를 붙인다.
        // 설정이 전부 꺼져 있으면 Prepare가 false를 돌려줘 아예 패치되지 않는다(오버헤드 0).
        [HarmonyPatch]
        private static class NoteMotionPatches
        {
            // TargetMethods가 빈 목록을 돌려주면 Harmony가 어트리뷰트에서 대상을 찾으려다
            // "Undefined target method"로 던진다. 그래서 대상 해석은 Prepare에서 끝내고,
            // 하나도 못 찾으면 패치 자체를 건너뛴다.
            private static readonly List<MethodBase> ResolvedTargets = new List<MethodBase>();

            private static bool Prepare()
            {
                if (ResolvedTargets.Count > 0)
                {
                    return true;
                }

                if (!NoteMotionEnabled)
                {
                    MelonLogger.Msg("[NoteMotion] NoteSway/NoteSpeedChaos가 모두 꺼져 있어 노트 훅을 걸지 않습니다.");
                    return false;
                }

                // 롱노트는 Behaviour를 따로 override하므로 두 타입 모두 잡는다.
                // override가 base.Behaviour를 부르면 한 프레임에 두 번 들어오는데,
                // ApplyNoteMotion의 프레임 가드가 두 번째 호출을 걸러 낸다.
                var specs = new[]
                {
                    new PatchSpec("Il2CppStargazer.Play.StargazerNote", "Behaviour", 1, "Single"),
                    new PatchSpec("Il2CppStargazer.Play.StargazerLongNote", "Behaviour", 1, "Single"),
                };

                foreach (PatchSpec spec in specs)
                {
                    MethodInfo? target = ResolveTargetMethod(spec);
                    if (target is null)
                    {
                        MelonLogger.Warning($"[NoteMotion] 대상을 찾지 못했습니다: {spec.TypeName}.{spec.MethodName}");
                        continue;
                    }

                    MelonLogger.Msg($"[NoteMotion] 훅 대상: {target.DeclaringType?.FullName}.{target.Name}");
                    ResolvedTargets.Add(target);
                }

                if (ResolvedTargets.Count == 0)
                {
                    MelonLogger.Warning("[NoteMotion] 노트 타입을 하나도 찾지 못해 연출을 켜지 못했습니다.");
                    return false;
                }

                return true;
            }

            private static IEnumerable<MethodBase> TargetMethods() => ResolvedTargets;

            private static void Prefix(object? __instance) => RestoreNoteBasePosition(__instance);

            private static void Postfix(object? __instance, float __0) => ApplyNoteMotion(__instance, __0);
        }
    }
}
