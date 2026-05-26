using System;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void HandleTrackSelectorSetTracks(object tracks)
        {
            try
            {
                MelonLogger.Msg("[TrackSelector.Set] TrackSelector의 Set 메서드가 호출되었습니다!");

                int trackCount = TryGetCollectionCount(tracks) ?? 0;
                int enumerateLimit = Math.Max(8, trackCount > 0 ? trackCount : 256);
                var items = EnumerateCollectionItems(tracks, enumerateLimit).ToList();
                if (trackCount <= 0)
                {
                    trackCount = items.Count;
                }

                MelonLogger.Msg($"[TrackSelector.Set] 트랙 데이터 리스트: count={trackCount}");

                if (items.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set] 트랙 리스트가 비어 있습니다.");
                    return;
                }

                for (int i = 0; i < items.Count; i++)
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

                if (TrackSelectorSetStartingPointDuplicated)
                {
                    return;
                }

                object? source = items.FirstOrDefault(IsStartingPointTrack);
                if (source is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set] startingpoint 트랙을 찾지 못했습니다. 복사 삽입을 건너뜁니다.");
                    return;
                }

                // Attempt to insert two copies of the starting point at the beginning
                int applied = 0;
                try
                {
                    if (TryInsertAtStart(tracks, source)) applied++;
                    if (TryInsertAtStart(tracks, source)) applied++;
                }
                catch { }

                if (applied > 0)
                {
                    TrackSelectorSetStartingPointDuplicated = true;
                    int updatedCount = TryGetCollectionCount(tracks) ?? (trackCount + applied);
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 트랙을 복사 주입했습니다! 적용={applied} 현재 트랙 수: {updatedCount}");
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

        private static bool IsStartingPointTrack(object? track)
        {
            if (track is null)
            {
                return false;
            }

            Type type = track.GetType();
            string? trackId = TryGetMemberValue(track, type, "TrackID")?.ToString()
                ?? TryGetMemberValue(track, type, "TrackId")?.ToString()
                ?? TryGetMemberValue(track, type, "trackId")?.ToString();
            return string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryInsertAtStart(object tracks, object item)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                MethodInfo? insertMethod = tracks
                    .GetType()
                    .GetMethods(Flags)
                    .FirstOrDefault(method => string.Equals(method.Name, "Insert", StringComparison.Ordinal)
                        && method.GetParameters().Length == 2
                        && method.GetParameters()[0].ParameterType == typeof(int));
                if (insertMethod is null)
                {
                    return false;
                }

                insertMethod.Invoke(tracks, new[] { (object)0, item });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}