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
        BlockOfficialAutoPlay,
    }

    internal static class SaveGuardLogic
    {
        internal static SaveGuardDecision Evaluate(
            bool hasResult,
            bool hasPlayTrack,
            bool isCustomTrack,
            bool isCustomPlayActive,
            bool allPerfectEnabled,
            bool allPerfectCustomOnly,
            bool autoPlayEnabled)
        {
            bool blockOfficialAllPerfect = allPerfectEnabled && !allPerfectCustomOnly;

            if (!hasResult)
            {
                if (isCustomPlayActive)
                {
                    return SaveGuardDecision.BlockDefensiveNullResult;
                }

                return blockOfficialAllPerfect
                    ? SaveGuardDecision.BlockOfficialAllPerfect
                    : (autoPlayEnabled ? SaveGuardDecision.BlockOfficialAutoPlay : SaveGuardDecision.Allow);
            }

            if (!hasPlayTrack)
            {
                if (isCustomPlayActive)
                {
                    return SaveGuardDecision.BlockDefensiveMissingTrack;
                }

                return blockOfficialAllPerfect
                    ? SaveGuardDecision.BlockOfficialAllPerfect
                    : (autoPlayEnabled ? SaveGuardDecision.BlockOfficialAutoPlay : SaveGuardDecision.Allow);
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

            if (blockOfficialAllPerfect)
            {
                return SaveGuardDecision.BlockOfficialAllPerfect;
            }

            // autoplay는 PlayerBase.Play마다 곡 구분 없이 IsAutoPlay를 켠다. 결과 데이터(ITravelResultData)에는
            // 오토플레이 여부가 없어서 게임이 스스로 저장을 거르는지 알 수 없으므로, 공식 기록에 자동 연주 점수가
            // 남지 않도록 모드가 막는다(AllPerfect를 공식 곡에 켰을 때와 같은 취급).
            if (autoPlayEnabled)
            {
                return SaveGuardDecision.BlockOfficialAutoPlay;
            }

            return SaveGuardDecision.Allow;
        }
    }
}
