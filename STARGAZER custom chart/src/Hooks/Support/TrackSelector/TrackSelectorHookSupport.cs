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

                // Dynamic injection check
                var firstTwo = EnumerateCollectionItems(tracks, 2).ToList();
                bool alreadyInjected = firstTwo.Count >= 2
                    && IsStartingPointTrack(firstTwo[0])
                    && IsStartingPointTrack(firstTwo[1]);

                if (alreadyInjected)
                {
                    if (EnableTrackSelectorVerboseLogging)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] 이미 startingpoint 트랙이 주입되어 있어 추가 주입을 건너뜁니다.");
                    }
                    return;
                }

                object? source = items.FirstOrDefault(IsStartingPointTrack);
                if (source is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set] startingpoint 트랙을 찾지 못했습니다. 복사 삽입을 건너뜁니다.");
                    return;
                }

                // Attempt to clone and insert two copies of the starting point at the beginning
                int applied = 0;
                object? track1 = null;
                object? track2 = null;

                Type? concreteTrackType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                Type? concreteMetaType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackMetaData");

                if (concreteTrackType is not null && concreteMetaType is not null)
                {
                    track1 = CloneTrackData(source, concreteTrackType, concreteMetaType);
                    track2 = CloneTrackData(source, concreteTrackType, concreteMetaType);
                }

                if (track1 is not null && track2 is not null)
                {
                    Type metadataOverrideTrackType = concreteTrackType ?? track1.GetType();
                    ApplyStartingPointMetadataOverrides(track1, metadataOverrideTrackType, "테스트 1", "화영왕");
                    ApplyStartingPointMetadataOverrides(track2, metadataOverrideTrackType, "테스트 2", "화영왕");
                }
                else
                {
                    track1 = source;
                    track2 = source;
                }

                try
                {
                    if (TryInsertAtStart(tracks, track2)) applied++;
                    if (TryInsertAtStart(tracks, track1)) applied++;
                }
                catch { }

                if (applied > 0)
                {
                    int updatedCount = TryGetCollectionCount(tracks) ?? (trackCount + applied);
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 트랙을 복사 주입했습니다! 적용={applied} 현재 트랙 수: {updatedCount}");
                    if (EnableTrackSelectorMetadataDump)
                    {
                        DumpInjectedTracksMetadata(tracks);
                    }
                }
                else
                {
                    MelonLogger.Warning("[TrackSelector.Set] startingpoint 트랙 Insert 호출에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] 처리 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
