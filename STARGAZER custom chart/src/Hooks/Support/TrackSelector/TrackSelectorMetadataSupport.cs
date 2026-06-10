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
        private static void SetTrackId(object track, string trackId)
        {
            try
            {
                Type ct = track.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // 쓰기 가능하면 INNER_TrackData 래퍼 자체에도 같이 설정합니다.
                PropertyInfo? wrapperIdProp = ct.GetProperty("TrackID", flags)
                                              ?? ct.GetProperty("TrackId", flags)
                                              ?? ct.GetProperty("trackId", flags);
                if (wrapperIdProp is not null && wrapperIdProp.CanWrite)
                {
                    wrapperIdProp.SetValue(track, trackId);
                }
                else
                {
                    FieldInfo? wrapperIdField = ct.GetField("_trackId", flags)
                                                ?? ct.GetField("trackId", flags)
                                                ?? ct.GetField("m_trackId", flags)
                                                ?? ct.GetField("TrackID", flags);
                    if (wrapperIdField is not null)
                    {
                        wrapperIdField.SetValue(track, trackId);
                    }
                }

                // 내부 metaData 객체에 설정합니다.
                FieldInfo? metaField = ct.GetField("_metaData", flags)
                                      ?? ct.GetField("metaData", flags)
                                      ?? ct.GetField("m_metaData", flags);

                if (metaField is not null)
                {
                    object? metaObj = metaField.GetValue(track);
                    if (metaObj is not null)
                    {
                        Type mt = metaObj.GetType();
                        PropertyInfo? idProp = mt.GetProperty("trackid", flags)
                                               ?? mt.GetProperty("trackId", flags)
                                               ?? mt.GetProperty("TrackID", flags);
                        if (idProp is not null && idProp.CanWrite)
                        {
                            idProp.SetValue(metaObj, trackId);
                            MelonLogger.Msg($"[TrackSelector.Set] trackid 설정 성공: {trackId}");
                        }
                        else
                        {
                            FieldInfo? idField = mt.GetField("trackid", flags)
                                                 ?? mt.GetField("trackId", flags)
                                                 ?? mt.GetField("TrackID", flags);
                            if (idField is not null)
                            {
                                idField.SetValue(metaObj, trackId);
                                MelonLogger.Msg($"[TrackSelector.Set] trackid 설정 성공(필드): {trackId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] SetTrackId 실패: {ex.Message}");
            }
        }

        private static void DumpInjectedTracksMetadata(object tracks)
        {
            try
            {
                var items = EnumerateCollectionItems(tracks, 2).ToList();
                if (items.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set.Dump] 주입된 트랙을 찾지 못했습니다.");
                    return;
                }

                Type? concreteTrackType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                if (concreteTrackType is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set.Dump] Il2CppStargazer.TrackLoader+INNER_TrackData 타입을 로드하지 못했습니다.");
                }

                for (int i = 0; i < items.Count; i++)
                {
                    object? track = items[i];
                    if (track is null)
                    {
                        MelonLogger.Msg($"[TrackSelector.Set.Dump] 주입된 트랙 [{i}]은 null입니다.");
                        continue;
                    }

                    MelonLogger.Msg($"\n[TrackSelector.Set.Dump] ==================== 주입된 트랙 [{i}] 정보 덤프 ====================");
                    Type t = track.GetType();
                    string trackId = TryGetMemberValue(track, t, "TrackID")?.ToString()
                                     ?? TryGetMemberValue(track, t, "TrackId")?.ToString()
                                     ?? TryGetMemberValue(track, t, "trackId")?.ToString() ?? "<unknown>";
                    string displayName = TryGetMemberValue(track, t, "TrackDisplayName")?.ToString() ?? "?";
                    string displayNameEn = TryGetMemberValue(track, t, "TrackDisplayNameEN")?.ToString() ?? "?";
                    string artistName = TryGetMemberValue(track, t, "ArtistDisplayName")?.ToString() ?? "?";

                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - Original Wrapper Type: {t.FullName}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - TrackID: {trackId}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - TrackDisplayName: {displayName}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - TrackDisplayNameEN: {displayNameEn}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - ArtistDisplayName: {artistName}");

                    // INNER_TrackData로 캐스팅해 봅니다.
                    object? concreteTrack = null;
                    if (concreteTrackType is not null)
                    {
                        concreteTrack = CastToConcreteTrackData(track, concreteTrackType);
                    }

                    if (concreteTrack is not null)
                    {
                        Type ct = concreteTrack.GetType();
                        MelonLogger.Msg($"[TrackSelector.Set.Dump] - Concrete Wrapper Type: {ct.FullName}");

                        // 내부 metaData(INNER_TrackMetaData)를 추출해 덤프합니다.
                        BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        FieldInfo? f = ct.GetField("_metaData", searchFlags)
                                      ?? ct.GetField("metaData", searchFlags)
                                      ?? ct.GetField("m_metaData", searchFlags);

                        object? metaObj = null;
                        if (f is not null)
                        {
                            metaObj = f.GetValue(concreteTrack);
                        }
                        else
                        {
                            PropertyInfo? p = ct.GetProperty("metaData", searchFlags)
                                               ?? ct.GetProperty("MetaData", searchFlags);
                            if (p is not null && p.CanRead)
                            {
                                metaObj = p.GetValue(concreteTrack);
                            }
                        }

                        // 이름에 'meta'가 포함된 필드/프로퍼티를 찾아봅니다.
                        if (metaObj is null)
                        {
                            foreach (FieldInfo ff in ct.GetFields(searchFlags))
                            {
                                if (ff.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    try
                                    {
                                        metaObj = ff.GetValue(concreteTrack);
                                        if (metaObj is not null) break;
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (metaObj is null)
                        {
                            foreach (PropertyInfo pp in ct.GetProperties(searchFlags))
                            {
                                if (pp.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0 && pp.CanRead)
                                {
                                    try
                                    {
                                        metaObj = pp.GetValue(concreteTrack);
                                        if (metaObj is not null) break;
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (metaObj is not null)
                        {
                            Type mtype = metaObj.GetType();
                            MelonLogger.Msg($"[TrackSelector.Set.Dump] - Meta Object Type: {mtype.FullName}");

                            var memberList = new System.Collections.Generic.List<string>();

                            int pcount = 0;
                            foreach (PropertyInfo prop in mtype.GetProperties(searchFlags))
                            {
                                if (!prop.CanRead) { continue; }
                                try
                                {
                                    object? val = prop.GetValue(metaObj);
                                    string sval = val is null ? "<null>" : val.ToString() ?? "<obj>";
                                    string writable = prop.CanWrite ? "W" : "R";
                                    memberList.Add($"P:{prop.Name}({writable})={sval}");
                                }
                                catch { memberList.Add($"P:{prop.Name}=<error>"); }
                                if (++pcount >= 64) break;
                            }

                            int fcount = 0;
                            foreach (FieldInfo field in mtype.GetFields(searchFlags))
                            {
                                try
                                {
                                    object? val = field.GetValue(metaObj);
                                    string sval = val is null ? "<null>" : val.ToString() ?? "<obj>";
                                    string ro = field.IsInitOnly ? "RO" : "RW";
                                    memberList.Add($"F:{field.Name}({ro})={sval}");
                                }
                                catch { memberList.Add($"F:{field.Name}=<error>"); }
                                if (++fcount >= 128) break;
                            }

                            MelonLogger.Msg($"[TrackSelector.Set.Dump] - INNER_TrackMetaData members:\n  {string.Join("\n  ", memberList)}");
                        }
                        else
                        {
                            MelonLogger.Msg("[TrackSelector.Set.Dump] - 내부에 INNER_TrackMetaData를 찾지 못했습니다.");
                        }
                    }
                    else
                    {
                        MelonLogger.Msg("[TrackSelector.Set.Dump] - INNER_TrackData로 캐스팅할 수 없습니다.");
                    }
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] ================================================================\n");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set.Dump] 메타데이터 덤프 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

    }
}
