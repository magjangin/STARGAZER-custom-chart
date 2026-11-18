using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // 커스텀 트랙은 공식 "Starting Point"의 복제본이라 TrackID가 똑같다
        // (TrackSelectorCollectionSupport.IsStartingPointTrack 주석 참고).
        // 저장 기록은 trackID로 조회/저장되므로(UserDataLoader.PlayRecord[trackid]),
        // 막지 않으면 커스텀 곡을 플레이할 때마다 진짜 "Starting Point" 기록을 덮어쓴다.
        [HarmonyPatch]
        private static class TrackRecordSaveGuardPatch
        {
            private static readonly List<MethodBase> Targets = new List<MethodBase>();

            private static bool Prepare() => PrepareTargets(Targets, new[]
            {
                new PatchSpec("Il2CppStargazer.UserDataLoader+INNER_TrackRecordModule", "SaveTrackRecord", 1, "ITravelResultData")
                    .WithAlternates("SaveRecord", "UpdateTrackRecord", "RecordTrackResult"),
            }, "기록 보호(SaveGuard)");

            private static IEnumerable<MethodBase> TargetMethods() => Targets;

            // false를 반환하면 원본 저장 로직을 건너뛴다. 공식 트랙으로 확실히 판정되면 항상 저장하고,
            // 트랙 객체 추출에 실패했지만 현재 커스텀 곡 플레이 중이면 기록 보호를 위해 방어적으로 차단한다.
            private static bool Prefix(object[]? __args)
            {
                try
                {
                    object? result = __args is { Length: > 0 } ? __args[0] : null;
                    object? playData = result is null ? null : TryGetMemberByType(result, "TravelArgs", "PlayData", "playData");
                    object? playTrack = playData is null ? null : TryGetMemberByType(playData, "ITrackData", "PlayTrack", "playTrack");
                    CustomAlbum? album = playTrack is null ? null : TryGetAlbumForTrack(playTrack);

                    SaveGuardDecision decision = SaveGuardLogic.Evaluate(
                        hasResult: result is not null,
                        hasPlayTrack: playTrack is not null,
                        isCustomTrack: album is not null,
                        isCustomPlayActive: IsCustomChartPlayActive && CurrentPlayAlbum is not null,
                        allPerfectEnabled: CustomConfig.AllPerfect,
                        allPerfectCustomOnly: CustomConfig.AllPerfectCustomOnly);

                    switch (decision)
                    {
                        case SaveGuardDecision.BlockDefensiveNullResult:
                            MelonLogger.Warning($"[SaveGuard] 결과 인자가 비어 있지만 커스텀 곡({CurrentPlayAlbum!.Name}) 플레이 중이므로 저장을 방어적으로 차단합니다.");
                            return false;
                        case SaveGuardDecision.BlockDefensiveMissingTrack:
                            MelonLogger.Warning($"[SaveGuard] PlayTrack 추출에 실패했으나 커스텀 곡({CurrentPlayAlbum!.Name}) 플레이 중이므로 저장을 방어적으로 차단합니다.");
                            return false;
                        case SaveGuardDecision.BlockCustomTrack:
                            MelonLogger.Msg($"[SaveGuard] 커스텀 트랙({album!.Name}) 저장을 막았습니다 — 공식 Starting Point 기록 보호.");
                            return false;
                        case SaveGuardDecision.BlockOfficialAllPerfect:
                            if (LogOnce("SaveGuard.OfficialAllPerfect"))
                            {
                                MelonLogger.Msg("[SaveGuard] AllPerfect가 공식 곡에도 활성화되어 있으므로 공식 기록 저장을 차단합니다 (이후 같은 로그는 생략).");
                            }
                            return false;
                        default:
                            return true;
                    }
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
