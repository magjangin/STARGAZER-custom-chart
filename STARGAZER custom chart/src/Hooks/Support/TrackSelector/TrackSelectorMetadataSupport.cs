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

                        // 내부 metaData(INNER_TrackMetaData)를 추출해 덤프합니다. _metaData는 auto-property로 선언되어 있어 프로퍼티 조회를 먼저 시도합니다.
                        BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        PropertyInfo? p0 = ct.GetProperty("_metaData", searchFlags)
                                          ?? ct.GetProperty("metaData", searchFlags)
                                          ?? ct.GetProperty("MetaData", searchFlags);

                        object? metaObj = null;
                        if (p0 is not null && p0.CanRead)
                        {
                            metaObj = p0.GetValue(concreteTrack);
                        }
                        else
                        {
                            FieldInfo? f = ct.GetField("_metaData", searchFlags)
                                          ?? ct.GetField("metaData", searchFlags)
                                          ?? ct.GetField("m_metaData", searchFlags);
                            if (f is not null)
                            {
                                metaObj = f.GetValue(concreteTrack);
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
