using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class SaveGuardLogicTests
    {
        [Fact]
        public void Evaluate_NullResult_BlocksOnlyWhenCustomPlayActive()
        {
            Assert.Equal(SaveGuardDecision.BlockDefensiveNullResult,
                SaveGuardLogic.Evaluate(false, false, false, true, false, true));
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(false, false, false, false, false, true));
        }

        [Fact]
        public void Evaluate_MissingPlayTrack_BlocksOnlyWhenCustomPlayActive()
        {
            Assert.Equal(SaveGuardDecision.BlockDefensiveMissingTrack,
                SaveGuardLogic.Evaluate(true, false, false, true, false, true));
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(true, false, false, false, false, true));
        }

        [Fact]
        public void Evaluate_CustomTrack_AlwaysBlocked()
        {
            Assert.Equal(SaveGuardDecision.BlockCustomTrack,
                SaveGuardLogic.Evaluate(true, true, true, true, false, true));
        }

        [Fact]
        public void Evaluate_OfficialTrack_BlocksOnlyWhenAllPerfectAppliesToOfficial()
        {
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(true, true, false, false, true, true));
            Assert.Equal(SaveGuardDecision.BlockOfficialAllPerfect,
                SaveGuardLogic.Evaluate(true, true, false, false, true, false));
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(true, true, false, false, false, false));
        }
    }
}
