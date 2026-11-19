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

        // 플레이 시작 시엔 커스텀으로 판정됐는데 결과의 트랙이 우리 주입 객체로 식별되지 않는 경우
        // (업데이트로 결과 데이터가 트랙을 복사/래핑해 포인터가 달라지는 등). 공식 기록을 지키려고 막는다.
        [Fact]
        public void Evaluate_TrackNotRecognizedDuringCustomPlay_BlocksDefensively()
        {
            Assert.Equal(SaveGuardDecision.BlockDefensiveTrackMismatch,
                SaveGuardLogic.Evaluate(true, true, false, true, false, true));
            Assert.Equal(SaveGuardDecision.BlockDefensiveTrackMismatch,
                SaveGuardLogic.Evaluate(true, true, false, true, true, false));
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
