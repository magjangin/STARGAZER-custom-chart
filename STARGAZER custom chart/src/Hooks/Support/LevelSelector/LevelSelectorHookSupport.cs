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


                // 1. 트랙 ID를 가져옵니다.
                string songId = TryGetMemberValue(instance, type, "trackID")?.ToString() ?? "";
                if (string.IsNullOrEmpty(songId) && _lastSelectedTrack is not null)
                {
                    songId = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackID")?.ToString()
                             ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "trackId")?.ToString()
                             ?? "";
                }

                // 2. TextProvider에서 곡 제목과 아티스트 이름을 가져옵니다.
                string songTitle = "";
                string artistName = "";

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

                // 제목이나 아티스트가 비어 있거나 에디터 플레이스홀더면 _lastSelectedTrack로 되돌립니다.
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

                string songInfo = "";
                if (!string.IsNullOrEmpty(songTitle) || !string.IsNullOrEmpty(songId))
                {
                    songInfo = $"Song: {songTitle} / {artistName} (id={songId}) | ";
                }

                // 3. 선택된 난이도 이름을 가져옵니다.
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
                                string levelName = "?";
                                string levelText = "?";
                                object? levelItem = TryGetMemberValue(activeLevelObj, activeLevelObj.GetType(), "item");
                                if (levelItem is not null)
                                {

                                    levelName = TryGetMemberValue(levelItem, levelItem.GetType(), "name")?.ToString() ?? "?";
                                    object? tp = TryGetMemberValue(levelItem, levelItem.GetType(), "levelText");
                                    if (tp is not null)
                                    {
                                        levelText = TryGetMemberValue(tp, tp.GetType(), "text")?.ToString()
                                                    ?? TryGetMemberValue(tp, tp.GetType(), "Text")?.ToString()
                                                    ?? tp.ToString() ?? "?";
                                    }
                                    if (string.IsNullOrEmpty(levelText) || levelText == "?")
                                    {
                                        if (!string.IsNullOrEmpty(songId) && songId != "?")
                                        {
                                            lock (_trackLevelsCache)
                                            {
                                                if (_trackLevelsCache.TryGetValue(songId, out var dict) && dict.TryGetValue(levelName, out string? cachedVal))
                                                {
                                                    levelText = cachedVal;
                                                }
                                            }
                                        }
                                    }
                                    if (string.IsNullOrEmpty(levelText) || levelText == "?")
                                    {
                                        levelText = TryScanForNumericDifficulty(levelItem);
                                    }
                                    if (string.IsNullOrEmpty(levelText) || levelText == "?")
                                    {
                                        levelText = TryScanForNumericDifficulty(activeLevelObj);
                                    }

                                    // 캐시를 갱신합니다.
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
                                difficultyInfo = $"Difficulty: {levelName} (Lv.{levelText}) | ";
                            }
                        }
                    }
                }

                // 4. 기록 인자를 포맷합니다.
                string recordInfo = "";
                if (args.Length > 0 && args[0] is not null)
                {
                    object record = args[0];
                    List<string> details = new List<string>();
                    string[] detailNames = { "score", "rate", "perfect", "accuracy", "combo", "grade", "clear", "rank", "difficulty", "level" };
                    foreach (string name in detailNames)
                    {
                        if (TryGetValueByNameCandidates(record, new[] { name }, out object? val) && val is not null)
                        {
                            details.Add($"{name}={val}");
                        }
                    }
                    if (details.Count > 0)
                    {
                        recordInfo = $"Record: [{string.Join(", ", details)}]";
                    }
                    else
                    {
                        recordInfo = $"Record: {record.GetType().FullName}";
                    }
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

                // 1. 트랙 ID를 가져옵니다.
                string songId = TryGetMemberValue(instance, type, "trackID")?.ToString() ?? "";
                if (string.IsNullOrEmpty(songId) && _lastSelectedTrack is not null)
                {
                    songId = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackID")?.ToString()
                             ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "trackId")?.ToString()
                             ?? "";
                }

                // 2. TextProvider에서 곡 제목과 아티스트 이름을 가져옵니다.
                string songTitle = "";
                string artistName = "";

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

                // 제목이나 아티스트가 비어 있거나 에디터 플레이스홀더면 _lastSelectedTrack로 되돌립니다.
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

                string songInfo = "";
                if (!string.IsNullOrEmpty(songTitle) || !string.IsNullOrEmpty(songId))
                {
                    songInfo = $"Song: {songTitle} / {artistName} (id={songId}) | ";
                }

                // 3. 스프라이트 인자를 포맷합니다.
                object? sprite = args.Length > 0 ? args[0] : null;
                string spriteName = sprite is not null ? (TryGetPropertyValue(sprite, "name")?.ToString() ?? sprite.GetType().Name) : "null";

                // 4. 레벨 선택기 레벨 요약을 추출합니다.
                string levelInfo = "";
                object? levels = TryGetMemberValue(instance, type, "levels");
                if (levels is not null)
                {
                    var items = EnumerateCollectionItems(levels, 12).ToList();
                    var levelSummary = new List<string>();
                    foreach (object? item in items)
                    {
                        if (item is null) continue;
                        string levelName = "?";
                        string levelText = "?";
                        try
                        {
                            object? levelItem = TryGetMemberValue(item, item.GetType(), "item");
                            if (levelItem is not null)
                            {
                                levelName = TryGetMemberValue(levelItem, levelItem.GetType(), "name")?.ToString() ?? "?";
                                object? tp = TryGetMemberValue(levelItem, levelItem.GetType(), "levelText");
                                if (tp is not null)
                                {
                                    levelText = TryGetMemberValue(tp, tp.GetType(), "text")?.ToString()
                                                ?? TryGetMemberValue(tp, tp.GetType(), "Text")?.ToString()
                                                ?? tp.ToString() ?? "?";
                                }
                                if (string.IsNullOrEmpty(levelText) || levelText == "?")
                                {
                                    if (!string.IsNullOrEmpty(songId) && songId != "?")
                                    {
                                        lock (_trackLevelsCache)
                                        {
                                            if (_trackLevelsCache.TryGetValue(songId, out var dict) && dict.TryGetValue(levelName, out string? cachedVal))
                                            {
                                                levelText = cachedVal;
                                            }
                                        }
                                    }
                                }
                                if (string.IsNullOrEmpty(levelText) || levelText == "?")
                                {
                                    levelText = TryScanForNumericDifficulty(levelItem);
                                }
                                if (string.IsNullOrEmpty(levelText) || levelText == "?")
                                {
                                    levelText = TryScanForNumericDifficulty(item);
                                }

                                // 캐시를 갱신합니다.
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
                        catch { }
                        levelSummary.Add($"{levelName}={levelText}");
                    }
                    if (levelSummary.Count > 0)
                    {
                        levelInfo =  $" | levels=[{string.Join(", ", levelSummary)}]";
                    }
                }

                // 출력합니다.
                MelonLogger.Msg($"[LevelSelector][FetchJacektImage] {songInfo}sprite={spriteName}{levelInfo}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][FetchJacektImage] Failed: {ex.Message}");
            }
        }
    }
}
