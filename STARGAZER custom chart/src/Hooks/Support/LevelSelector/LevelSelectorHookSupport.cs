using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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


                // 1. Get track ID
                string songId = TryGetMemberValue(instance, type, "trackID")?.ToString() ?? "";
                if (string.IsNullOrEmpty(songId) && _lastSelectedTrack is not null)
                {
                    songId = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackID")?.ToString()
                             ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "trackId")?.ToString()
                             ?? "";
                }

                // 2. Get song title and artist name from TextProviders
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

                // Fallback to _lastSelectedTrack if title or artist are empty or editor placeholders
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

                // 3. Get selected difficulty name
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
                                                if (_trackLevelsCache.TryGetValue(songId, out var dict) && dict.TryGetValue(levelName, out string cachedVal))
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

                                    // Update cache
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

                // 4. Format record arguments
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

                // 1. Get track ID
                string songId = TryGetMemberValue(instance, type, "trackID")?.ToString() ?? "";
                if (string.IsNullOrEmpty(songId) && _lastSelectedTrack is not null)
                {
                    songId = TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "TrackID")?.ToString()
                             ?? TryGetMemberValue(_lastSelectedTrack, _lastSelectedTrack.GetType(), "trackId")?.ToString()
                             ?? "";
                }

                // 2. Get song title and artist name from TextProviders
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

                // Fallback to _lastSelectedTrack if title or artist are empty or editor placeholders
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

                // 3. Format sprite argument
                object? sprite = args.Length > 0 ? args[0] : null;
                string spriteName = sprite is not null ? (TryGetPropertyValue(sprite, "name")?.ToString() ?? sprite.GetType().Name) : "null";

                // 4. Extract level selector levels summary
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
                                            if (_trackLevelsCache.TryGetValue(songId, out var dict) && dict.TryGetValue(levelName, out string cachedVal))
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

                                // Update cache
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

                // Print
                MelonLogger.Msg($"[LevelSelector][FetchJacektImage] {songInfo}sprite={spriteName}{levelInfo}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][FetchJacektImage] Failed: {ex.Message}");
            }
        }

        private static void EnumerateLevelSelectorLevels(object instance)
        {
            try
            {
                object? levels = TryGetMemberValue(instance, instance.GetType(), "levels");
                if (levels is null)
                {
                    MelonLogger.Warning("[LevelSelector][Enumerate] 'levels' collection is null.");
                    return;
                }

                var items = EnumerateCollectionItems(levels, 12).ToList();
                if (items.Count == 0)
                {
                    return;
                }

                var levelSummary = new List<string>();
                foreach (object? item in items)
                {
                    if (item is null)
                    {
                        levelSummary.Add("null");
                        continue;
                    }

                    string levelName = "?";
                    string levelText = "?";
                    try
                    {
                        object? levelItem = TryGetMemberValue(item, item.GetType(), "item");
                        if (levelItem is not null)
                        {
                            levelName = TryGetMemberValue(levelItem, levelItem.GetType(), "name")?.ToString() ?? "?";
                            object? tp = TryGetMemberValue(levelItem, levelItem.GetType(), "levelText");
                            if (tp is not null && TryGetExactPropertyValue(tp, "Text", out object? textValue) && textValue is not null)
                            {
                                levelText = textValue.ToString() ?? "?";
                            }
                        }
                    }
                    catch
                    {
                    }

                    levelSummary.Add($"{levelName}={levelText}");
                }

                MelonLogger.Msg($"[LevelSelector] levels: [{string.Join(", ", levelSummary)}]");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][Enumerate] Failed: {ex.Message}");
            }
        }

        private static string TryScanForNumericDifficulty(object? obj)
        {
            if (obj is null) return "?";
            try
            {
                Type type = obj.GetType();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                
                // 1. Try to search for fields or properties with exact/partial names containing level/difficulty
                string[] targetCandidates = { "level", "difficulty", "lv", "val", "value" };
                
                // Try properties first
                foreach (var prop in type.GetProperties(Flags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    string name = prop.Name.ToLowerInvariant();
                    bool matches = false;
                    foreach (string candidate in targetCandidates)
                    {
                        if (name.Contains(candidate, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches) continue;
                    
                    try
                    {
                        object? val = prop.GetValue(obj);
                        if (val is not null && IsNumericAndPositive(val, out string strVal))
                        {
                            return strVal;
                        }
                    }
                    catch {}
                }
                
                // Try fields
                foreach (var field in type.GetFields(Flags))
                {
                    string name = field.Name.ToLowerInvariant();
                    bool matches = false;
                    foreach (string candidate in targetCandidates)
                    {
                        if (name.Contains(candidate, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches) continue;
                    
                    try
                    {
                        object? val = field.GetValue(obj);
                        if (val is not null && IsNumericAndPositive(val, out string strVal))
                        {
                            return strVal;
                        }
                    }
                    catch {}
                }
                
                // 2. If it's a TextProvider or similar, try to extract its text value
                object? textVal = TryGetMemberValue(obj, type, "text")
                                  ?? TryGetMemberValue(obj, type, "Text")
                                  ?? TryGetMemberValue(obj, type, "string");
                if (textVal is not null)
                {
                    string textStr = textVal.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(textStr) && textStr != "?" && int.TryParse(textStr, out int num) && num > 0)
                    {
                        return textStr;
                    }
                }
            }
            catch {}
            
            return "?";
        }
        
        private static bool IsNumericAndPositive(object val, out string stringValue)
        {
            stringValue = "?";
            if (val is int iVal)
            {
                if (iVal > 0 && iVal < 100) { stringValue = iVal.ToString(); return true; }
            }
            else if (val is long lVal)
            {
                if (lVal > 0 && lVal < 100) { stringValue = lVal.ToString(); return true; }
            }
            else if (val is short sVal)
            {
                if (sVal > 0 && sVal < 100) { stringValue = sVal.ToString(); return true; }
            }
            else if (val is byte bVal)
            {
                if (bVal > 0 && bVal < 100) { stringValue = bVal.ToString(); return true; }
            }
            else if (val is float fVal)
            {
                if (fVal > 0f && fVal < 100f) { stringValue = ((int)fVal).ToString(); return true; }
            }
            else if (val is double dVal)
            {
                if (dVal > 0d && dVal < 100d) { stringValue = ((int)dVal).ToString(); return true; }
            }
            else if (val is string str)
            {
                if (int.TryParse(str, out int num) && num > 0 && num < 100)
                {
                    stringValue = str;
                    return true;
                }
            }
            return false;
        }

        private static void DumpObjectValues(string label, object? obj)
        {
            if (obj is null) return;
            try
            {
                Type type = obj.GetType();
                var list = new List<string>();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                
                foreach (var prop in type.GetProperties(Flags))
                {
                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                    {
                        try
                        {
                            object? val = prop.GetValue(obj);
                            list.Add($"P:{prop.Name}={val ?? "null"} ({prop.PropertyType.Name})");
                        }
                        catch (Exception ex)
                        {
                            list.Add($"P:{prop.Name}=<error: {ex.Message}>");
                        }
                    }
                }
                
                foreach (var field in type.GetFields(Flags))
                {
                    try
                    {
                        object? val = field.GetValue(obj);
                        list.Add($"F:{field.Name}={val ?? "null"} ({field.FieldType.Name})");
                    }
                    catch (Exception ex)
                    {
                        list.Add($"F:{field.Name}=<error: {ex.Message}>");
                    }
                }
                
                MelonLogger.Msg($"[DumpValues][{label}] Type={type.FullName}:\n  {string.Join("\n  ", list)}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[DumpValues][{label}] Failed: {ex.Message}");
            }
        }
    }
}