using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool _focusedTrackViewerPatched;
        private static string _lastFocusedTrackViewerSummary = "";

        [ThreadStatic]
        private static bool _isProbingFocusedTrackViewer;

        private void TryApplyFocusedTrackViewerPatches()
        {
            if (_focusedTrackViewerPatched) return;
            _focusedTrackViewerPatched = true;

            try
            {
                Type? type = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType("Il2CppStargazer.Travel.FocusedTrackViewer", false))
                    .FirstOrDefault(t => t is not null);

                if (type is null)
                {
                    LoggerInstance.Warning("[FocusedTrackViewerProbe] 로드된 어셈블리에서 Il2CppStargazer.Travel.FocusedTrackViewer 타입을 찾지 못했습니다.");
                    return;
                }

                LoggerInstance.Msg($"[FocusedTrackViewerProbe] Found type: {type.FullName}. Proceeding with dynamic hooks.");

                // 고주파 로그와 재귀를 피하려고 프로퍼티 getter/setter를 제외하고 이 타입의 메서드를 후킹합니다.
                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => !m.IsGenericMethod && !m.IsAbstract && m.DeclaringType == type)
                    .Where(m => !m.Name.StartsWith("get_", StringComparison.Ordinal) && !m.Name.StartsWith("set_", StringComparison.Ordinal))
                    .ToArray();

                int successCount = 0;
                foreach (MethodInfo method in methods)
                {
                    try
                    {
                        RuntimeHarmonyInstance.Patch(
                            method,
                            prefix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(FocusedTrackViewerPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(FocusedTrackViewerPostfix), BindingFlags.Static | BindingFlags.NonPublic))
                        );
                        successCount++;
                    }
                    catch
                    {
                        // 일부 메서드는 패치에 실패할 수 있지만 괜찮습니다.
                    }
                }

                LoggerInstance.Msg($"[FocusedTrackViewerProbe] Successfully patched {successCount}/{methods.Length} methods of FocusedTrackViewer (excluding getters/setters).");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[FocusedTrackViewerProbe] 패치를 적용하지 못했습니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void FocusedTrackViewerPrefix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            // PRE에서는 아무것도 하지 않음 (POST에서 출력)
        }

        private static void FocusedTrackViewerPostfix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            if (_isProbingFocusedTrackViewer) return;
            _isProbingFocusedTrackViewer = true;

            try
            {
                if (__instance is not null)
                {
                    EnumerateFocusedTrackViewerLevelItems(__instance, __originalMethod.Name);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer][POST] Exception: {ex.Message}");
            }
            finally
            {
                _isProbingFocusedTrackViewer = false;
            }
        }

        private static void EnumerateFocusedTrackViewerLevelItems(object instance, string methodName)
        {
            try
            {
                Type type = instance.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                string[] candidates = { "level", "item", "list", "array" };

                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                    if (!NameMatchesAny(property.Name, candidates)) continue;

                    object? val = null;
                    try { val = property.GetValue(instance); } catch { continue; }
                    if (val is null) continue;

                    EnumerateFocusedTrackViewerCollection(instance, property.Name, val, methodName);
                }

                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!NameMatchesAny(field.Name, candidates)) continue;

                    object? val = null;
                    try { val = field.GetValue(instance); } catch { continue; }
                    if (val is null) continue;

                    EnumerateFocusedTrackViewerCollection(instance, field.Name, val, methodName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer] 열거에 실패했습니다: {ex.Message}");
            }
        }

        private static void EnumerateFocusedTrackViewerCollection(object parentInstance, string memberName, object collection, string methodName)
        {
            try
            {
                var items = EnumerateCollectionItems(collection, 100).ToList();
                if (items.Count == 0) return;

                var levelSummary = new List<string>();
                var cacheEntry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (object? item in items)
                {
                    if (item is null) { levelSummary.Add("null"); continue; }

                    string level = "?";
                    string text = "?";

                    object? lvEnum = TryGetMemberValue(item, item.GetType(), "targetLevel")
                        ?? TryGetMemberValue(item, item.GetType(), "TargetLevel")
                        ?? TryGetMemberValue(item, item.GetType(), "level")
                        ?? TryGetMemberValue(item, item.GetType(), "Level");
                    if (lvEnum is not null)
                        level = lvEnum.ToString() ?? "?";

                    try
                    {
                        object? subItem = TryGetMemberValue(item, item.GetType(), "levelItem")
                            ?? TryGetMemberValue(item, item.GetType(), "LevelItem");
                        if (subItem is not null)
                        {
                            object? tp = TryGetMemberValue(subItem, subItem.GetType(), "levelText")
                                ?? TryGetMemberValue(subItem, subItem.GetType(), "LevelText");
                            if (tp is not null && TryGetExactPropertyValue(tp, "Text", out object? tVal) && tVal is not null)
                                text = tVal.ToString() ?? "?";
                        }
                    }
                    catch { }

                    levelSummary.Add($"{level}={text}");
                    if (level != "?" && text != "?")
                    {
                        cacheEntry[level] = text;
                    }
                }

                string songInfo = "";
                string songId = "";

                string[] trackCandidates = { "CurrentFocused", "selectedTrack", "currentTrack", "focusedTrack", "track", "trackData" };
                object? trackObj = null;
                Type parentType = parentInstance.GetType();
                foreach (var fieldName in trackCandidates)
                {
                    trackObj = TryGetMemberValue(parentInstance, parentType, fieldName);
                    if (trackObj is not null && trackObj.GetType().FullName != "System.Int32" && trackObj.GetType().FullName != "System.Boolean")
                    {
                        break;
                    }
                }

                if (trackObj is not null)
                {
                    Type t = trackObj.GetType();
                    string id = TryGetMemberValue(trackObj, t, "TrackID")?.ToString()
                                ?? TryGetMemberValue(trackObj, t, "trackId")?.ToString()
                                ?? "?";
                    string title = TryGetMemberValue(trackObj, t, "TrackDisplayName")?.ToString()
                                   ?? TryGetMemberValue(trackObj, t, "TrackDisplayNameEN")?.ToString()
                                   ?? TryGetMemberValue(trackObj, t, "displayName")?.ToString()
                                   ?? "?";
                    string artist = TryGetMemberValue(trackObj, t, "ArtistDisplayName")?.ToString() ?? "?";

                    songInfo = $"Song: {title} / {artist} (id={id}) | ";
                    songId = id;
                }

                // 캐시에 저장합니다.
                if (!string.IsNullOrEmpty(songId) && songId != "?" && cacheEntry.Count > 0)
                {
                    lock (_trackLevelsCache)
                    {
                        _trackLevelsCache[songId] = cacheEntry;
                    }
                }

                string summary = string.Join(", ", levelSummary);
                string changeKey = $"{songId}_{summary}";

                // 값이 변경됐을 때만 출력
                if (string.Equals(_lastFocusedTrackViewerSummary, changeKey, StringComparison.Ordinal)) return;
                _lastFocusedTrackViewerSummary = changeKey;

                MelonLogger.Msg($"[FocusedTrackViewer] {songInfo}levels: [{summary}] via {methodName}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FocusedTrackViewer] '{memberName}' 멤버 열거에 실패했습니다: {ex.Message}");
            }
        }

        private static bool TryGetExactPropertyValue(object owner, string name, out object? value)
        {
            value = null;
            if (owner is null) return false;

            Type type = owner.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo? prop = type.GetProperty(name, flags)
                ?? type.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                try
                {
                    value = prop.GetValue(owner);
                    return value is not null;
                }
                catch {}
            }

            FieldInfo? field = type.GetField(name, flags)
                ?? type.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                try
                {
                    value = field.GetValue(owner);
                    return value is not null;
                }
                catch {}
            }

            return false;
        }
    }
}
