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

                // 동적 주입 확인. 공식 startingpoint 트랙도 TrackID가 같으므로 IsStartingPointTrack이 아니라
                // 우리가 등록해둔 커스텀 트랙 식별(IsCustomChartTrack)로 판정해야 한다.
                var firstTwo = EnumerateCollectionItems(tracks, 2).ToList();
                bool alreadyInjected = firstTwo.Count >= 2
                    && IsCustomChartTrack(firstTwo[0])
                    && IsCustomChartTrack(firstTwo[1]);

                if (alreadyInjected)
                {
                    if (EnableTrackSelectorVerboseLogging)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] 이미 커스텀 트랙이 주입되어 있어 추가 주입을 건너뜁니다.");
                    }
                    return;
                }

                object? source = items.FirstOrDefault(IsStartingPointTrack);
                if (source is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set] startingpoint 트랙을 찾지 못했습니다. 복사 삽입을 건너뜁니다.");
                    return;
                }

                // 시작 지점을 두 번 복제해 앞쪽에 삽입하려고 시도합니다.
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
                    // hwa/info.txt에 곡 제목/아티스트/난이도가 있으면 그걸로 덮어쓴다. 커스텀 트랙 식별은
                    // 표시명도 TrackID도 아닌 객체 동일성(IsCustomChartTrack)으로 하므로,
                    // 표시명에는 "테스트 " 같은 내부용 접두를 붙이지 않아도 된다.
                    string hwaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwa");
                    string? infoPath = FindCustomTrackInfoFile(hwaPath);
                    CustomTrackInfo? info = infoPath is null ? null : CustomTrackInfo.TryParse(infoPath);

                    string displayName = !string.IsNullOrEmpty(info?.Title) ? info!.Title! : "Custom Track";
                    string composer = !string.IsNullOrEmpty(info?.Artist) ? info!.Artist! : "화영왕";

                    Type metadataOverrideTrackType = concreteTrackType ?? track1.GetType();
                    ApplyStartingPointMetadataOverrides(track1, metadataOverrideTrackType, displayName, composer, info?.Levels);
                    ApplyStartingPointMetadataOverrides(track2, metadataOverrideTrackType, displayName, composer, info?.Levels);

                    // BGM/자켓/BMS 주입이 공식 트랙을 건드리지 않도록, 방금 만든 이 두 객체만 커스텀으로 등록한다.
                    ResetInjectedCustomTracks();
                    RegisterInjectedCustomTrack(track1);
                    RegisterInjectedCustomTrack(track2);
                }
                else
                {
                    MelonLogger.Warning("[TrackSelector.Set] 독립 트랙 복제에 실패하여 주입을 건너뜁니다.");
                    return;
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
