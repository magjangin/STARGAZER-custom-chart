using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static object? _lastSelectedTrack;

        private static void HandleTrackListViewerMoveCursor(object? instance, object[] args)
        {
            if (instance is null) return;
            try
            {
                int delta = args.Length > 0 && args[0] is int d ? d : 0;

                Type type = instance.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // 1. 직접 선택된 트랙 객체를 찾습니다.
                string[] trackCandidates = { "CurrentFocused", "selectedTrack", "currentTrack", "focusedTrack", "track", "trackData" };
                object? trackObj = null;
                foreach (var fieldName in trackCandidates)
                {
                    trackObj = TryGetMemberValue(instance, type, fieldName);
                    if (trackObj is not null && trackObj.GetType().FullName != "System.Int32" && trackObj.GetType().FullName != "System.Boolean")
                    {
                        break;
                    }
                }

                // 2. 트랙 컬렉션과 인덱스를 찾습니다.
                if (trackObj is null)
                {
                    string[] listCandidates = { "tracklist", "tracks", "trackList", "items", "list", "tracksData", "array" };
                    object? listObj = null;
                    foreach (var fieldName in listCandidates)
                    {
                        listObj = TryGetMemberValue(instance, type, fieldName);
                        if (listObj is not null && listObj is not string && (listObj is System.Collections.IEnumerable || listObj.GetType().IsArray || listObj.GetType().FullName?.Contains("List") == true))
                        {
                            break;
                        }
                    }

                    string[] indexCandidates = { "currentIndex", "Index", "selectIndex", "selectedIndex", "cursor", "index" };
                    object? indexObj = null;
                    foreach (var fieldName in indexCandidates)
                    {
                        indexObj = TryGetMemberValue(instance, type, fieldName);
                        if (indexObj is not null && (indexObj is int || indexObj is long || indexObj is short))
                        {
                            break;
                        }
                    }

                    if (listObj is not null && indexObj is not null)
                    {
                        int index = Convert.ToInt32(indexObj);
                        var items = EnumerateCollectionItems(listObj, 1024).ToList();
                        if (index >= 0 && index < items.Count)
                        {
                            trackObj = items[index];
                        }
                        else
                        {
                            MelonLogger.Msg($"[TrackListViewer.MoveCursor] Found list (count={items.Count}) and index={index}, but index out of range.");
                        }
                    }
                }

                // 3. 트랙을 찾았다면 표시합니다!
                if (trackObj is not null)
                {
                    _lastSelectedTrack = trackObj;
                    Type t = trackObj.GetType();
                    string id = TryGetMemberValue(trackObj, t, "TrackID")?.ToString()
                                ?? TryGetMemberValue(trackObj, t, "trackId")?.ToString()
                                ?? "?";
                    string title = TryGetMemberValue(trackObj, t, "TrackDisplayName")?.ToString()
                                   ?? TryGetMemberValue(trackObj, t, "TrackDisplayNameEN")?.ToString()
                                   ?? TryGetMemberValue(trackObj, t, "displayName")?.ToString()
                                   ?? "?";
                    string artist = TryGetMemberValue(trackObj, t, "ArtistDisplayName")?.ToString() ?? "?";

                    // 커서를 옮길 때마다 불리므로 한 줄로만 남긴다(예전엔 7줄씩 찍혀 곡 목록을 훑으면 로그가 폭주했다).
                    MelonLogger.Msg($"[TrackSelection] delta={delta} title={title} artist={artist} id={id} custom={IsCustomChartTrack(trackObj)}");
                }
                else
                {
                    // 대체: 찾을 수 있는 null이 아닌 멤버를 모두 나열합니다. 커서 이동마다 반복되지 않게 한 번만.
                    if (!LogOnce("TrackListViewer.MoveCursor.memberDump"))
                    {
                        return;
                    }

                    MelonLogger.Msg("[TrackListViewer.MoveCursor] 선택된 트랙을 확인하지 못했습니다. 멤버를 덤프합니다(처음 1회만):");
                    foreach (var field in type.GetFields(flags))
                    {
                        try
                        {
                            object? val = field.GetValue(instance);
                            if (val is not null)
                            {
                                MelonLogger.Msg($"  Field: {field.Name} ({field.FieldType.Name}) = {val}");
                            }
                        }
                        catch { }
                    }
                    foreach (var prop in type.GetProperties(flags))
                    {
                        try
                        {
                            if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                            {
                                object? val = prop.GetValue(instance);
                                if (val is not null)
                                {
                                    MelonLogger.Msg($"  Property: {prop.Name} ({prop.PropertyType.Name}) = {val}");
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackListViewer.MoveCursor] Error probing selected track: {ex.Message}");
            }
        }
    }
}
