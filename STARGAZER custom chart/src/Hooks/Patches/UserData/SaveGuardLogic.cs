namespace STARGAZER_custom_chart
{
    internal enum SaveGuardDecision
    {
        Allow,
        BlockCustomTrack,
        BlockDefensiveNullResult,
        BlockDefensiveMissingTrack,
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

            if (allPerfectEnabled && !allPerfectCustomOnly)
            {
                return SaveGuardDecision.BlockOfficialAllPerfect;
            }

            return SaveGuardDecision.Allow;
        }
    }
}
