using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        private static void HandleTrackSelectorSetTracks(object tracks)
        {
            try
            {
                ResetPlaySessionState();
                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Msg("[TrackSelector.Set] TrackSelector의 Set 메서드가 호출되었습니다!");
                }

                IReadOnlyList<CustomAlbum> albums = CustomAlbumRegistry.GetAlbums();
                if (albums.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set] hwa에 앨범이 없어 주입을 건너뜁니다.");
                    return;
                }

                // 항상 목록 맨 앞에 주입하므로, 첫 항목이 우리 트랙이면 이미 주입된 것으로 본다.
                // 공식 startingpoint 트랙도 TrackID가 같아서 ID로는 판정할 수 없다(포인터 등록표로만 판정).
                object? firstItem = EnumerateCollectionItems(tracks, 1).FirstOrDefault();
                if (firstItem is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set] 트랙 리스트가 비어 있습니다.");
                    return;
                }

                if (IsCustomChartTrack(firstItem))
                {
                    if (EnableTrackSelectorVerboseLogging)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] 이미 커스텀 트랙이 주입되어 있어 추가 주입을 건너뜁니다.");
                    }
                    return;
                }

                int trackCount = TryGetCollectionCount(tracks) ?? 0;
                int enumerateLimit = Math.Max(8, trackCount > 0 ? trackCount : 256);

                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] 트랙 데이터 리스트: count={trackCount}");
                    int i = 0;
                    foreach (object? track in EnumerateCollectionItems(tracks, 5))
                    {
                        if (track is null)
                        {
                            MelonLogger.Msg($"[TrackSelector.Set] - 트랙 {i}: null");
                        }
                        else
                        {
                            Type concreteType = track.GetType();
                            string displayName = TryGetMemberValue(track, concreteType, "TrackDisplayName")?.ToString() ?? "?";
                            string displayNameEn = TryGetMemberValue(track, concreteType, "TrackDisplayNameEN")?.ToString() ?? "?";
                            MelonLogger.Msg($"[TrackSelector.Set] - 트랙 {i}: type={concreteType.FullName ?? concreteType.Name}, TrackDisplayName={displayName}, TrackDisplayNameEN={displayNameEn}");
                        }
                        i++;
                    }
                }

                // 앨범마다 기존 복제본을 재사용한다. 목록(정렬·필터로 새로 만든 목록 포함)에 이미 들어 있는 복제본은
                // 다시 넣지 않는다 — 첫 항목 검사만으로는 정렬된 목록 중간의 복제본을 못 보고 중복 삽입할 수 있다.
                var presentClonePtrs = new HashSet<IntPtr>();
                if (InjectedCustomTrackAlbums.Count > 0)
                {
                    foreach (object? item in EnumerateCollectionItems(tracks, enumerateLimit))
                    {
                        IntPtr itemPtr = item is null ? IntPtr.Zero : TryGetIl2CppPointer(item);
                        if (itemPtr != IntPtr.Zero && InjectedCustomTrackAlbums.ContainsKey(itemPtr))
                        {
                            presentClonePtrs.Add(itemPtr);
                        }
                    }
                }

                var clonePtrs = new IntPtr?[albums.Count];
                for (int i = 0; i < albums.Count; i++)
                {
                    clonePtrs[i] = TryGetInjectedClone(albums[i], out IntPtr clonePtr, out _) ? clonePtr : null;
                }

                List<int> missingAlbums = CustomTrackInjectionLogic.FindAlbumsMissingFromList(clonePtrs, presentClonePtrs);
                if (missingAlbums.Count == 0)
                {
                    if (EnableTrackSelectorVerboseLogging)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] 모든 커스텀 트랙이 이미 목록에 있어 추가 주입을 건너뜁니다.");
                    }
                    return;
                }

                // 새로 복제해야 하는 앨범이 있을 때만 복제 원본을 찾는다(재사용만 하면 원본 탐색 불필요).
                object? source = null;
                Type? concreteTrackType = null;
                Type? concreteMetaType = null;
                if (missingAlbums.Any(i => clonePtrs[i] is null))
                {
                    // ID가 정확히 "startingpoint"인 트랙을 우선한다(Jazz Arrange 등 접두 일치 변형이 먼저 나와도 원곡을 복제).
                    // 이미 주입한 복제본도 같은 TrackID라 후보에서 빼야 복제본을 다시 복제하지 않는다.
                    source = CloneSourceSelection.Pick(
                        EnumerateCollectionItems(tracks, enumerateLimit).Where(item => !IsCustomChartTrack(item)),
                        GetTrackIdText,
                        out bool exactSourceMatch);
                    if (source is null)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] startingpoint 트랙을 찾지 못했습니다. 새 복제는 건너뛰고 기존 복제본만 다시 넣습니다.");
                    }
                    else if (!exactSourceMatch && LogOnce("TrackSelector.CloneSource.Prefix"))
                    {
                        MelonLogger.Warning("[TrackSelector.Set] ID가 정확히 'startingpoint'인 트랙이 없어 접두 일치 트랙을 복제 원본으로 씁니다. 게임 업데이트로 ID가 바뀌었는지 확인하세요.");
                    }

                    concreteTrackType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                    concreteMetaType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackMetaData");
                    if (source is not null && (concreteTrackType is null || concreteMetaType is null))
                    {
                        MelonLogger.Warning("[TrackSelector.Set] INNER_TrackData/INNER_TrackMetaData 타입을 찾지 못해 새 복제를 건너뜁니다.");
                        source = null;
                    }
                }

                // 앨범(hwa 하위 폴더)마다 startingpoint를 한 번만 복제해 독립 트랙으로 만들고, 이후에는 그 객체를 재사용한다.
                var pending = new List<(object Track, CustomAlbum Album)>();
                int reused = 0;

                foreach (int albumIndex in missingAlbums)
                {
                    CustomAlbum album = albums[albumIndex];
                    if (TryGetInjectedClone(album, out _, out object? existing) && existing is not null)
                    {
                        pending.Add((existing, album));
                        reused++;
                        continue;
                    }

                    if (source is null)
                    {
                        continue;
                    }

                    object? clone = CloneTrackData(source, concreteTrackType!, concreteMetaType!);
                    if (clone is null)
                    {
                        MelonLogger.Warning($"[TrackSelector.Set] 트랙 복제 실패로 건너뜁니다: {album.Name}");
                        continue;
                    }

                    // 커스텀 트랙 식별은 표시명도 TrackID도 아닌 객체 동일성으로 하므로,
                    // 표시명에는 "테스트 " 같은 내부용 접두를 붙이지 않는다.
                    ApplyStartingPointMetadataOverrides(clone, concreteTrackType!, album.DisplayName, album.Artist);

                    // 자켓/BGM/차트가 공식 트랙을 건드리지 않도록, 방금 만든 이 객체만 앨범과 함께 등록한다.
                    // 포인터를 못 얻어 식별 등록이 안 된 복제본은 공식 곡으로 오인되므로 목록에 넣지 않는다.
                    if (RegisterInjectedCustomTrack(clone, album))
                    {
                        pending.Add((clone, album));
                    }
                }

                if (pending.Count == 0)
                {
                    MelonLogger.Warning("[TrackSelector.Set] 넣을 커스텀 트랙이 없어(복제 실패/원본 없음) 주입을 건너뜁니다.");
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
                    MelonLogger.Msg($"[TrackSelector.Set] 커스텀 트랙을 주입했습니다! 적용={applied}/{albums.Count} (재사용 {reused}) 현재 트랙 수: {updatedCount} ({names})");
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
