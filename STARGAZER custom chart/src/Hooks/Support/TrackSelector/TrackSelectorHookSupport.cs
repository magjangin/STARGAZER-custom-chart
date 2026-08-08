using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void HandleTrackSelectorSetTracks(object tracks)
        {
            try
            {
                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Msg("[TrackSelector.Set] TrackSelector의 Set 메서드가 호출되었습니다!");
                }

                int trackCount = TryGetCollectionCount(tracks) ?? 0;
                int enumerateLimit = Math.Max(8, trackCount > 0 ? trackCount : 256);
                var items = EnumerateCollectionItems(tracks, enumerateLimit).ToList();
                if (trackCount <= 0)
                {
                    trackCount = items.Count;
                }

                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] 트랙 데이터 리스트: count={trackCount}");
                }

                if (items.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set] 트랙 리스트가 비어 있습니다.");
                    return;
                }

                if (EnableTrackSelectorVerboseLogging)
                {
                    for (int i = 0; i < Math.Min(5, items.Count); i++)
                    {
                        object? track = items[i];
                        if (track is null)
                        {
                            MelonLogger.Msg($"[TrackSelector.Set] - 트랙 {i}: null");
                            continue;
                        }

                        Type concreteType = track.GetType();
                        string displayName = TryGetMemberValue(track, concreteType, "TrackDisplayName")?.ToString() ?? "?";
                        string displayNameEn = TryGetMemberValue(track, concreteType, "TrackDisplayNameEN")?.ToString() ?? "?";
                        MelonLogger.Msg($"[TrackSelector.Set] - 트랙 {i}: type={concreteType.FullName ?? concreteType.Name}, TrackDisplayName={displayName}, TrackDisplayNameEN={displayNameEn}");
                    }
                }

                // 항상 목록 맨 앞에 주입하므로, 첫 항목이 우리 트랙이면 이미 주입된 것으로 본다.
                // 공식 startingpoint 트랙도 TrackID가 같아서 IsStartingPointTrack으로는 판정할 수 없다.
                object? firstItem = EnumerateCollectionItems(tracks, 1).FirstOrDefault();
                if (IsCustomChartTrack(firstItem))
                {
                    if (EnableTrackSelectorVerboseLogging)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] 이미 커스텀 트랙이 주입되어 있어 추가 주입을 건너뜁니다.");
                    }
                    return;
                }

                IReadOnlyList<CustomAlbum> albums = CustomAlbumRegistry.GetAlbums();
                if (albums.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set] hwa에 앨범이 없어 주입을 건너뜁니다.");
                    return;
                }

                object? source = items.FirstOrDefault(IsStartingPointTrack);
                if (source is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set] startingpoint 트랙을 찾지 못했습니다. 복사 삽입을 건너뜁니다.");
                    return;
                }

                Type? concreteTrackType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                Type? concreteMetaType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackMetaData");
                if (concreteTrackType is null || concreteMetaType is null)
                {
                    MelonLogger.Warning("[TrackSelector.Set] INNER_TrackData/INNER_TrackMetaData 타입을 찾지 못해 주입을 건너뜁니다.");
                    return;
                }

                // 앨범(hwa 하위 폴더)마다 startingpoint를 하나씩 복제해 독립 트랙으로 만든다.
                ResetInjectedCustomTracks();
                var pending = new List<(object Track, CustomAlbum Album)>();

                foreach (CustomAlbum album in albums)
                {
                    object? clone = CloneTrackData(source, concreteTrackType, concreteMetaType);
                    if (clone is null)
                    {
                        MelonLogger.Warning($"[TrackSelector.Set] 트랙 복제 실패로 건너뜁니다: {album.Name}");
                        continue;
                    }

                    // 커스텀 트랙 식별은 표시명도 TrackID도 아닌 객체 동일성으로 하므로,
                    // 표시명에는 "테스트 " 같은 내부용 접두를 붙이지 않는다.
                    ApplyStartingPointMetadataOverrides(clone, concreteTrackType, album.DisplayName, album.Artist, album.Info?.Levels);

                    // 자켓/BGM/차트가 공식 트랙을 건드리지 않도록, 방금 만든 이 객체만 앨범과 함께 등록한다.
                    RegisterInjectedCustomTrack(clone, album);
                    pending.Add((clone, album));
                }

                if (pending.Count == 0)
                {
                    MelonLogger.Warning("[TrackSelector.Set] 독립 트랙 복제에 모두 실패하여 주입을 건너뜁니다.");
                    return;
                }

                // 맨 앞에 하나씩 밀어넣으므로 역순으로 넣어야 앨범 순서대로 보인다.
                int applied = 0;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (TryInsertAtStart(tracks, pending[i].Track))
                        {
                            applied++;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[TrackSelector.Set] Insert 실패({pending[i].Album.Name}): {ex.Message}");
                    }
                }

                if (applied > 0)
                {
                    int updatedCount = TryGetCollectionCount(tracks) ?? (trackCount + applied);
                    string names = string.Join(", ", pending.Select(p => p.Album.DisplayName));
                    MelonLogger.Msg($"[TrackSelector.Set] 커스텀 트랙을 주입했습니다! 적용={applied}/{albums.Count} 현재 트랙 수: {updatedCount} ({names})");
                    if (EnableTrackSelectorMetadataDump)
                    {
                        DumpInjectedTracksMetadata(tracks);
                    }
                }
                else
                {
                    MelonLogger.Warning("[TrackSelector.Set] 커스텀 트랙 Insert 호출에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] 처리 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
