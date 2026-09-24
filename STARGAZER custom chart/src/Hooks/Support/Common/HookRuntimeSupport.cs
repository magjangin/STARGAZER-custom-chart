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
        private static readonly object InvocationLogThrottleLock = new object();
        private static readonly Dictionary<string, InvocationLogThrottleEntry> InvocationLogThrottleMap = new Dictionary<string, InvocationLogThrottleEntry>(StringComparer.Ordinal);
        private static readonly HashSet<string> TrackLoaderCallbackPatchedMethods = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> FocusedHarmonyInvocationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Il2CppStargazer.Play.PlayerBase.Play",
            "Il2CppStargazer.Play.PlayerBase.PlayStart",
            "Il2CppStargazer.Play.StargazerPlayer.Load",
            "Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPattern",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader.Load",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader._Load_b__5_0",
            "Il2CppStargazer.TrackLoader+INNER_TrackData.LoadBGMClip",
            "Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPreviewClip",
            "Il2CppStarlike.Sound.SoundPlayer.PlayBGM",
            "Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen",
            "Il2CppStargazer.Travel.Result.PlayInfoViewer.ShowPlayInfo",
            "Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchJacektImage",
        };
        // 메서드별로 1초에 최대 3줄(PRE와 POST가 같은 한도를 나눠 쓴다).
        private const int InvocationLogWindowMs = 1000;
        private const int InvocationLogMaxPerWindow = 3;
        // savecustomkey/config.txt의 autoplay 값으로 제어한다(기본 false). 커스텀 곡에만 적용된다.
        private static bool EnableForceAutoPlayAtPlayerBasePlay => CustomConfig.AutoPlay;
        private static bool PlayerBaseJacketLogged;
        private static bool TrackLoaderListLogged;
        private static bool IsInPlayScene;
        private static bool IsCustomChartPlayActive;

        private sealed class InvocationLogThrottleEntry
        {
            public InvocationLogThrottleEntry(long windowStartMs, int count)
            {
                WindowStartMs = windowStartMs;
                Count = count;
            }

            public long WindowStartMs { get; set; }
            public int Count { get; set; }
        }
    }
}
