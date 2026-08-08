using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 커스텀 트랙은 공식 "Starting Point"의 복제본이라 TrackID가 똑같다
        // (TrackSelectorCollectionSupport.IsStartingPointTrack 주석 참고).
        // 저장 기록은 trackID로 조회/저장되므로(UserDataLoader.PlayRecord[trackid]),
        // 막지 않으면 커스텀 곡을 플레이할 때마다 진짜 "Starting Point" 기록을 덮어쓴다.
        [HarmonyPatch]
        private static class TrackRecordSaveGuardPatch
        {
            private static MethodBase TargetMethod() => ResolveRequiredTargetMethod(
                new PatchSpec("Il2CppStargazer.UserDataLoader+INNER_TrackRecordModule", "SaveTrackRecord", 1, "ITravelResultData"));

            // false를 반환하면 원본 저장 로직을 건너뛴다. 판정이 애매하면(예외 등) 항상 true로
            // 원본 동작(=저장)을 그대로 둔다 — 공식 트랙 저장까지 막아버리는 쪽이 더 위험하다.
            private static bool Prefix(object[]? __args)
            {
                try
                {
                    object? result = __args is { Length: > 0 } ? __args[0] : null;
                    if (result is null)
                    {
                        return true;
                    }

                    object? playData = TryGetMemberValue(result, result.GetType(), "PlayData");
                    object? playTrack = playData is null ? null : TryGetMemberValue(playData, playData.GetType(), "PlayTrack");
                    CustomAlbum? album = TryGetAlbumForTrack(playTrack);
                    if (album is null)
                    {
                        return true;
                    }

                    MelonLogger.Msg($"[SaveGuard] 커스텀 트랙({album.Name}) 저장을 막았습니다 — 공식 Starting Point 기록 보호.");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[SaveGuard] 저장 차단 판정 실패, 원본 동작을 따릅니다: {ex.GetType().Name}: {ex.Message}");
                    return true;
                }
            }
        }
    }
}
