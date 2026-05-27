using System;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        /// <summary>
        /// PlayBGM Postfix에서 최초 1회 호출 — SoundPlayer 타입에 있는 PlaySFX 오버로드를
        /// 전부 reflection으로 열거하여 로그에 출력합니다.
        /// 출력 결과를 보고 PatchSpec 파라미터 수와 타입명을 결정하세요.
        /// </summary>
        private static void DumpPlaySFXOverloads(Type soundPlayerType)
        {
            try
            {
                MethodInfo[] overloads = soundPlayerType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => string.Equals(m.Name, "PlaySFX", StringComparison.Ordinal))
                    .ToArray();

                if (overloads.Length == 0)
                {
                    MelonLogger.Msg($"[SoundPlayer][PlaySFX] No overloads found on {soundPlayerType.FullName}");
                    return;
                }

                MelonLogger.Msg($"[SoundPlayer][PlaySFX] Found {overloads.Length} overload(s) on {soundPlayerType.FullName}:");
                for (int i = 0; i < overloads.Length; i++)
                {
                    ParameterInfo[] parameters = overloads[i].GetParameters();
                    string paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    MelonLogger.Msg($"  [{i}] PlaySFX({paramStr})  // paramCount={parameters.Length}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SoundPlayer][PlaySFX] DumpPlaySFXOverloads failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// PlaySFX 4개 오버로드 공통 Postfix 핸들러.
        /// 파라미터 수와 첫 번째 파라미터 타입으로 어느 오버로드인지 구분하여 로깅합니다.
        ///
        ///  [0] PlaySFX(AudioClip clip, ESoundType type)
        ///  [1] PlaySFX(AudioClip clip, ESoundType type, Single volume)
        ///  [2] PlaySFX(Single startTime, AudioClip clip, ESoundType type)
        ///  [3] PlaySFX(Single startTime, AudioClip clip, ESoundType type, Single volume)
        /// </summary>
        private static void HandlePlaySFX(MethodBase method, object[] args)
        {
            try
            {
                // 플레이씬이 아니면 무시
                if (!IsInPlayScene)
                {
                    return;
                }

                ParameterInfo[] parameters = method.GetParameters();
                int paramCount = parameters.Length;
                bool firstIsSingle = paramCount > 0
                    && string.Equals(parameters[0].ParameterType.Name, "Single", StringComparison.Ordinal);

                // 어느 오버로드인지 판별
                string overloadLabel;
                string clipInfo  = "<null>";
                string typeInfo  = "<null>";
                string startTime = "-";
                string volume    = "-";

                if (paramCount == 2)
                {
                    // [0] PlaySFX(AudioClip clip, ESoundType type)
                    overloadLabel = "[0] (clip, type)";
                    clipInfo  = FormatInvocationValue(args.Length > 0 ? args[0] : null);
                    typeInfo  = FormatInvocationValue(args.Length > 1 ? args[1] : null);
                }
                else if (paramCount == 3 && !firstIsSingle)
                {
                    // [1] PlaySFX(AudioClip clip, ESoundType type, Single volume)
                    overloadLabel = "[1] (clip, type, volume)";
                    clipInfo  = FormatInvocationValue(args.Length > 0 ? args[0] : null);
                    typeInfo  = FormatInvocationValue(args.Length > 1 ? args[1] : null);
                    volume    = FormatInvocationValue(args.Length > 2 ? args[2] : null);
                }
                else if (paramCount == 3 && firstIsSingle)
                {
                    // [2] PlaySFX(Single startTime, AudioClip clip, ESoundType type)
                    overloadLabel = "[2] (startTime, clip, type)";
                    startTime = FormatInvocationValue(args.Length > 0 ? args[0] : null);
                    clipInfo  = FormatInvocationValue(args.Length > 1 ? args[1] : null);
                    typeInfo  = FormatInvocationValue(args.Length > 2 ? args[2] : null);
                }
                else if (paramCount == 4)
                {
                    // [3] PlaySFX(Single startTime, AudioClip clip, ESoundType type, Single volume)
                    overloadLabel = "[3] (startTime, clip, type, volume)";
                    startTime = FormatInvocationValue(args.Length > 0 ? args[0] : null);
                    clipInfo  = FormatInvocationValue(args.Length > 1 ? args[1] : null);
                    typeInfo  = FormatInvocationValue(args.Length > 2 ? args[2] : null);
                    volume    = FormatInvocationValue(args.Length > 3 ? args[3] : null);
                }
                else
                {
                    overloadLabel = $"[?] paramCount={paramCount}";
                }

                MelonLogger.Msg(
                    $"[SoundPlayer][PlaySFX] overload={overloadLabel}" +
                    $" clip={clipInfo} type={typeInfo}" +
                    (startTime != "-" ? $" startTime={startTime}" : "") +
                    (volume    != "-" ? $" volume={volume}"        : ""));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SoundPlayer][PlaySFX] HandlePlaySFX failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
