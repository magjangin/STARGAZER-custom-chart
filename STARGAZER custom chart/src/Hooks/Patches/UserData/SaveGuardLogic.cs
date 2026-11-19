namespace STARGAZER_custom_chart
{
    internal enum SaveGuardDecision
    {
        Allow,
        BlockCustomTrack,
        BlockDefensiveNullResult,
        BlockDefensiveMissingTrack,
        BlockDefensiveTrackMismatch,
        BlockOfficialAllPerfect,
    }

    internal static class SaveGuardLogic
    {
        internal static SaveGuardDecision Evaluate(
            bool hasResult,
            bool hasPlayTrack,
            bool isCustomTrack,
            bool isCustomPlayActive,
            bool allPerfectEnabled,
            bool allPerfectCustomOnly)
        {
            if (!hasResult)
            {
                return isCustomPlayActive
                    ? SaveGuardDecision.BlockDefensiveNullResult
                    : SaveGuardDecision.Allow;
            }

            if (!hasPlayTrack)
            {
                return isCustomPlayActive
                    ? SaveGuardDecision.BlockDefensiveMissingTrack
                    : SaveGuardDecision.Allow;
            }

            if (isCustomTrack)
            {
                return SaveGuardDecision.BlockCustomTrack;
            }

            // 플레이 시작(PlayerBase.Play)에선 커스텀으로 판정됐는데 결과의 트랙이 우리 주입 객체로 식별되지 않는다.
            // 업데이트로 결과 데이터가 트랙을 복사·래핑하면 포인터 비교가 빗나가 공식 기록을 덮어쓰게 되므로 막는다.
            // (IsCustomChartPlayActive는 Play마다 다시 계산되고 플레이 씬 밖에선 꺼지므로 오래된 값으로 공식 곡을 막을 일은 없다.)
            if (isCustomPlayActive)
            {
                return SaveGuardDecision.BlockDefensiveTrackMismatch;
            }

            if (allPerfectEnabled && !allPerfectCustomOnly)
            {
                return SaveGuardDecision.BlockOfficialAllPerfect;
            }

            return SaveGuardDecision.Allow;
        }
    }
}
