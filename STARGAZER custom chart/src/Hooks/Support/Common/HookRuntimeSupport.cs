using System;
using System.Collections.Generic;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool EnableVerboseInvocationLogging = false;
        private static bool EnableKeepBgmPlaying = true;
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
            "Il2CppStargazer.Starlike.Sound.SoundPlayer.GetBGMHandler",
            "Il2CppStarlike.Sound.SoundPlayer.GetBGMHandler",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler.BGMPlayChecker",
        };
        private static readonly object InvocationLogThrottleLock = new object();
        private static readonly Dictionary<string, InvocationLogThrottleEntry> InvocationLogThrottleMap = new Dictionary<string, InvocationLogThrottleEntry>(StringComparer.Ordinal);
        private static readonly HashSet<string> TrackLoaderCallbackPatchedMethods = new HashSet<string>(StringComparer.Ordinal);
        private const int InvocationLogWindowMs = 1000;
        private const int InvocationLogMaxPerWindow = 6;
        private const bool EnableForceAutoPlayAtPlayerBasePlay = true;
        private static bool PlayerBaseJacketLogged;
        private static bool LevelSelectorCatalogLogged;
        private static bool TrackLoaderCatalogLogged;
        private static bool TrackLoaderListLogged;
        private static bool TrackSelectorSetStartingPointDuplicated;

        private void TryApplyHarmonyAttributePatches(string phase)
        {
            if (_hooksPatchAttempted)
            {
                LoggerInstance.Msg($"[HookPatch][{phase}] skip patching (already attempted, appliedAll={_hooksApplied}).");
                return;
            }

            _hooksPatchAttempted = true;
            MelonLogger.Msg($"[HookPatch][{phase}] begin patching. mode=harmony-attributes");
            RuntimeHarmonyInstance.PatchAll(typeof(GameTypeEnumeratorMod).Assembly);
            _hooksApplied = true;
            LoggerInstance.Msg($"[HookPatch][{phase}] patch summary: appliedAll={_hooksApplied}");
        }

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