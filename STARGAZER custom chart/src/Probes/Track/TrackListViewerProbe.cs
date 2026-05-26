using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // tracklist setter의 arg0 = List<ITrackData> 를 직접 받아 열거
        private static void DumpTrackListViewerTracks(object tracklist)
        {
            try
            {
                var items = EnumerateCollectionItems(tracklist, 1024).ToList();
                if (items.Count == 0)
                {
                    MelonLogger.Warning("[TrackList] tracklist가 비어 있습니다.");
                    return;
                }

                MelonLogger.Msg($"[TrackList] ===== 전체 트랙 목록 ({items.Count}개) =====");
                int idx = 1;
                foreach (object? item in items)
                {
                    if (item is null) { MelonLogger.Msg($"  [{idx++:D3}] null"); continue; }

                    Type t = item.GetType();
                    string id            = TryGetMemberValue(item, t, "TrackID")?.ToString() ?? "?";
                    string title         = TryGetMemberValue(item, t, "TrackDisplayName")?.ToString()
                                           ?? TryGetMemberValue(item, t, "TrackDisplayNameEN")?.ToString()
                                           ?? "?";
                    string artist        = TryGetMemberValue(item, t, "ArtistDisplayName")?.ToString() ?? "?";
                    string order         = TryGetMemberValue(item, t, "order")?.ToString() ?? "?";
                    string bundle        = TryGetMemberValue(item, t, "BundleID")?.ToString() ?? "?";
                    string episode       = TryGetMemberValue(item, t, "EpisodeID")?.ToString() ?? "?";
                    string isUnlocked    = TryGetMemberValue(item, t, "IsUnlocked")?.ToString() ?? "?";
                    string lockType      = TryGetMemberValue(item, t, "LockType")?.ToString() ?? "?";

                    MelonLogger.Msg($"  [{idx++:D3}] id={id} order={order} unlocked={isUnlocked} lock={lockType} | {title} / {artist} | bundle={bundle} ep={episode}");
                }
                MelonLogger.Msg($"[TrackList] ===== 끝 =====");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackList] DumpTrackListViewerTracks 실패: {ex.Message}");
            }
        }
    }
}
