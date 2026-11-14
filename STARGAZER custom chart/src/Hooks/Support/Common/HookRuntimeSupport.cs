using System;
using System.Collections.Generic;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool EnableVerboseInvocationLogging = false;
        private static bool EnableFocusedHarmonyInvocationLogging = true;
        private static bool EnableRuntimeProbeLogging = false;
        private static bool EnableTrackSelectorVerboseLogging = false;

        private static readonly HashSet<string> LoggedOnceCache = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object LoggedOnceLock = new object();

        private static bool LogOnce(string key)
        {
            lock (LoggedOnceLock)
            {
                return LoggedOnceCache.Add(key);
            }
        }

        // 키는 PatchSpec 논리 이름(TypeName.MethodName)이다. 대체 해석된 대상도 같은 키로 판정된다.
        private static readonly HashSet<string> FocusedHarmonyInvocationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Il2CppStargazer.Play.PlayerBase.Play",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader.Load",
            PatternLoaderTypeName + "." + PatternLoaderLambdaName,
            "Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen",
            "Il2CppStargazer.Travel.Result.PlayInfoViewer.ShowPlayInfo",
            "Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchJacektImage",
        };

        private const int InvocationLogWindowMs = 1000;
        private const int InvocationLogMaxPerWindow = 3;

        // savecustomkey/config.txt의 autoplay 값으로 제어한다. 파일이 없으면 autoplay=false로 새로 만든다.
        private static bool EnableForceAutoPlayAtPlayerBasePlay => CustomConfig.AutoPlay;
        private static bool IsCustomChartPlayActive;
    }
}
