using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _trackLevelsCache = new Dictionary<string, Dictionary<string, string>>();

        private static void HandleLevelSelectorFetchTrackRecord(object? instance, object[] args)
        {
            if (instance is null) return;
            try
            {
                Type type = instance.GetType();
                string songInfo = BuildSongInfo(instance, type, out string songId);

                string difficultyInfo = "";
                object? selectedIndexObj = TryGetMemberValue(instance, type, "SelectedIndex")
                                           ?? TryGetMemberValue(instance, type, "index");
                if (selectedIndexObj is not null)
                {
                    int selectedIndex = Convert.ToInt32(selectedIndexObj);
                    object? levels = TryGetMemberValue(instance, type, "levels");
                    if (levels is not null)
                    {
                        var levelItems = EnumerateCollectionItems(levels, 12).ToList();
                        if (selectedIndex >= 0 && selectedIndex < levelItems.Count)
                        {
                            object? activeLevelObj = levelItems[selectedIndex];
                            if (activeLevelObj is not null)
                            {
                                object? levelItem = TryGetMemberValue(activeLevelObj, activeLevelObj.GetType(), "item");
                                TryResolveLevelItemText(levelItem, activeLevelObj, songId, out string levelName, out string levelText);
                                difficultyInfo = $"Difficulty: {levelName} (Lv.{levelText}) | ";
                            }
                        }
                    }
                }

                string recordInfo;
                if (args.Length > 0 && args[0] is not null)
                {
                    object record = args[0];
                    var details = new List<string>();
                    string[] detailNames = { "score", "rate", "perfect", "accuracy", "combo", "grade", "clear", "rank", "difficulty", "level" };
                    foreach (string name in detailNames)
                    {
                        if (TryGetValueByNameCandidates(record, new[] { name }, out object? val) && val is not null)
                            details.Add($"{name}={val}");
                    }
                    recordInfo = details.Count > 0
                        ? $"Record: [{string.Join(", ", details)}]"
                        : $"Record: {record.GetType().FullName}";
                }
                else
                {
                    recordInfo = "Record: <null>";
                }

                MelonLogger.Msg($"[LevelSelector][FetchTrackRecord] {songInfo}{difficultyInfo}{recordInfo}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][FetchTrackRecord] Failed: {ex.Message}");
            }
        }

        private static void HandleLevelSelectorFetchJacektImage(object? instance, object[] args)
        {
            if (instance is null) return;
            try
            {
                Type type = instance.GetType();
                string songInfo = BuildSongInfo(instance, type, out string songId);

                object? sprite = args.Length > 0 ? args[0] : null;
                string spriteName = sprite is not null
                    ? (TryGetPropertyValue(sprite, "name")?.ToString() ?? sprite.GetType().Name)
                    : "null";

                string levelInfo = "";
                object? levels = TryGetMemberValue(instance, type, "levels");
                if (levels is not null)
                {
                    var levelSummary = new List<string>();
                    foreach (object? item in EnumerateCollectionItems(levels, 12))
                    {
                        if (item is null) continue;
                        try
                        {
                            object? levelItem = TryGetMemberValue(item, item.GetType(), "item");
                            TryResolveLevelItemText(levelItem, item, songId, out string levelName, out string levelText);
                            levelSummary.Add($"{levelName}={levelText}");
                        }
                        catch { }
                    }
                    if (levelSummary.Count > 0)
                        levelInfo = $" | levels=[{string.Join(", ", levelSummary)}]";
                }

                MelonLogger.Msg($"[LevelSelector][FetchJacektImage] {songInfo}sprite={spriteName}{levelInfo}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][FetchJacektImage] Failed: {ex.Message}");
            }
        }

        private static string BuildSongInfo(object instance, Type type, out string songId)
        {
            TryExtractSongContext(instance, type, out songId, out string songTitle, out string artistName);
            return !string.IsNullOrEmpty(songTitle) || !string.IsNullOrEmpty(songId)
                ? $"Song: {songTitle} / {artistName} (id={songId}) | "
                : "";
        }

        private static void TryExtractSongContext(object instance, Type type, out string songId, out string songTitle, out string artistName)
        {
            songId = TryGetMemberValue(instance, type, "trackID")?.ToString() ?? "";
            if (string.IsNullOrEmpty(songId) && _lastSelectedTrack is not null)
            {
                songId = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackID")?.ToString()
                         ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "trackId")?.ToString()
                         ?? "";
            }

            songTitle = "";
            artistName = "";

            object? trackNameObj = TryGetMemberValue(instance, type, "trackName");
            if (trackNameObj is not null)
            {
                songTitle = TryGetMemberValue(trackNameObj, trackNameObj.GetType(), "text")?.ToString()
                            ?? TryGetMemberValue(trackNameObj, trackNameObj.GetType(), "Text")?.ToString()
                            ?? TryGetMemberValue(trackNameObj, trackNameObj.GetType(), "string")?.ToString()
                            ?? "";
            }

            object? artistNameObj = TryGetMemberValue(instance, type, "artistName");
            if (artistNameObj is not null)
            {
                artistName = TryGetMemberValue(artistNameObj, artistNameObj.GetType(), "text")?.ToString()
                             ?? TryGetMemberValue(artistNameObj, artistNameObj.GetType(), "Text")?.ToString()
                             ?? TryGetMemberValue(artistNameObj, artistNameObj.GetType(), "string")?.ToString()
                             ?? "";
            }

            if ((string.IsNullOrEmpty(songTitle) || songTitle == "Track Name") && _lastSelectedTrack is not null)
            {
                songTitle = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackDisplayName")?.ToString()
                            ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackDisplayNameEN")?.ToString()
                            ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "displayName")?.ToString()
                            ?? songTitle;
            }

            if ((string.IsNullOrEmpty(artistName) || artistName == "Artist Name") && _lastSelectedTrack is not null)
            {
                artistName = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "ArtistDisplayName")?.ToString()
                             ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "artist")?.ToString()
                             ?? artistName;
            }
        }

        private static void TryResolveLevelItemText(object? levelItem, object? fallbackItem, string songId, out string levelName, out string levelText)
        {
            levelName = "?";
            levelText = "?";
            if (levelItem is null) return;

            levelName = TryGetMemberValue(levelItem, levelItem.GetType(), "name")?.ToString() ?? "?";

            object? tp = TryGetMemberValue(levelItem, levelItem.GetType(), "levelText");
            if (tp is not null)
            {
                levelText = TryGetMemberValue(tp, tp.GetType(), "text")?.ToString()
                            ?? TryGetMemberValue(tp, tp.GetType(), "Text")?.ToString()
                            ?? tp.ToString() ?? "?";
            }

            if ((string.IsNullOrEmpty(levelText) || levelText == "?") && !string.IsNullOrEmpty(songId) && songId != "?")
            {
                lock (_trackLevelsCache)
                {
                    if (_trackLevelsCache.TryGetValue(songId, out var dict) && dict.TryGetValue(levelName, out string? cachedVal))
                        levelText = cachedVal;
                }
            }

            if (string.IsNullOrEmpty(levelText) || levelText == "?")
                levelText = TryScanForNumericDifficulty(levelItem);

            if ((string.IsNullOrEmpty(levelText) || levelText == "?") && fallbackItem is not null)
                levelText = TryScanForNumericDifficulty(fallbackItem);

            if (levelName != "?" && levelText != "?" && !string.IsNullOrEmpty(songId) && songId != "?")
            {
                lock (_trackLevelsCache)
                {
                    if (!_trackLevelsCache.TryGetValue(songId, out var dict))
                    {
                        dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _trackLevelsCache[songId] = dict;
                    }
                    dict[levelName] = levelText;
                }
            }
        }
    }
}
