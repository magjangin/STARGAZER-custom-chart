using System;
using System.Collections.Generic;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool EnableVerboseInvocationLogging = true;
        private static bool EnableFocusedHarmonyInvocationLogging = true;
        private static bool EnableRuntimeProbeLogging = true;
        private static bool EnablePlaySceneJacketLogging = true;
        private static bool EnablePlaySceneTrackLogging = true;
        private static bool EnableResultSceneJacketLogging = true;
        private static bool EnableTrackSelectorVerboseLogging = false;
        private static bool EnableTrackSelectorMetadataDump = false;
        private static readonly HashSet<string> LoggedOnceCache = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object LoggedOnceLock = new object();

        private static bool LogOnce(string key)
        {
            lock (LoggedOnceLock)
            {
                return LoggedOnceCache.Add(key);
            }
        }

        private static readonly HashSet<string> LoggedCurrentTrackViewerImageHits = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedResultImageHits = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> SuppressedInvocationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Il2CppStargazer.Play.PlayerBase.SetupPlay",
            "Il2CppStargazer.Play.TravelPlayer.SetupPlay",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler.BGMPlayChecker",
        };
        // 키는 PatchSpec 논리 이름(TypeName.MethodName)이다. 대체 해석된 대상도 같은 키로 판정된다.
        private static readonly HashSet<string> FocusedHarmonyInvocationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Il2CppStargazer.Play.PlayerBase.Play",
            "Il2CppStargazer.Play.PlayerBase.PlayStart",
            "Il2CppStargazer.Play.StargazerPlayer.Load",
            "Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPattern",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader.Load",
            PatternLoaderTypeName + "." + PatternLoaderLambdaName,
            "Il2CppStargazer.TrackLoader+INNER_TrackData.LoadBGMClip",
            "Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPreviewClip",
            "Il2CppStarlike.Sound.SoundPlayer.PlayBGM",
            "Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen",
            "Il2CppStargazer.Travel.Result.PlayInfoViewer.ShowPlayInfo",
            "Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchJacektImage",
        };
        private const int InvocationLogWindowMs = 1000;
        private const int InvocationLogMaxPerWindow = 3;
        // savecustomkey/config.txt의 autoplay 값으로 제어한다. 파일이 없으면 autoplay=false로 새로 만든다.
        private static bool EnableForceAutoPlayAtPlayerBasePlay => CustomConfig.AutoPlay;
        private static bool PlayerBaseJacketLogged;
        private static bool IsInPlayScene;
        private static bool IsCustomChartPlayActive;
    }
}
