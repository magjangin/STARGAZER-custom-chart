using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class SaveGuardLogicTests
    {
        [Fact]
        public void Evaluate_NullResult_BlocksOnlyWhenCustomPlayActive()
        {
            Assert.Equal(SaveGuardDecision.BlockDefensiveNullResult,
                SaveGuardLogic.Evaluate(false, false, false, true, false, true, false));
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(false, false, false, false, false, true, false));
        }

        [Fact]
        public void Evaluate_MissingPlayTrack_BlocksOnlyWhenCustomPlayActive()
        {
            Assert.Equal(SaveGuardDecision.BlockDefensiveMissingTrack,
                SaveGuardLogic.Evaluate(true, false, false, true, false, true, false));
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(true, false, false, false, false, true, false));
        }

        [Fact]
        public void Evaluate_CustomTrack_AlwaysBlocked()
        {
            Assert.Equal(SaveGuardDecision.BlockCustomTrack,
                SaveGuardLogic.Evaluate(true, true, true, true, false, true, false));
        }

        // 플레이 시작 시엔 커스텀으로 판정됐는데 결과의 트랙이 우리 주입 객체로 식별되지 않는 경우
        // (업데이트로 결과 데이터가 트랙을 복사/래핑해 포인터가 달라지는 등). 공식 기록을 지키려고 막는다.
        [Fact]
        public void Evaluate_TrackNotRecognizedDuringCustomPlay_BlocksDefensively()
        {
            Assert.Equal(SaveGuardDecision.BlockDefensiveTrackMismatch,
                SaveGuardLogic.Evaluate(true, true, false, true, false, true, false));
            Assert.Equal(SaveGuardDecision.BlockDefensiveTrackMismatch,
                SaveGuardLogic.Evaluate(true, true, false, true, true, false, false));
        }

        [Fact]
        public void Evaluate_OfficialTrack_BlocksOnlyWhenAllPerfectAppliesToOfficial()
        {
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(true, true, false, false, true, true, false));
            Assert.Equal(SaveGuardDecision.BlockOfficialAllPerfect,
                SaveGuardLogic.Evaluate(true, true, false, false, true, false, false));
            Assert.Equal(SaveGuardDecision.Allow,
                SaveGuardLogic.Evaluate(true, true, false, false, false, false, false));
        }
    
        // autoplay는 곡 구분 없이 켜지므로 공식 곡 자동 연주 결과도 기록에 남지 않게 막는다.
        [Fact]
        public void Evaluate_OfficialTrack_BlockedWhenAutoPlayEnabled()
        {
            Assert.Equal(SaveGuardDecision.BlockOfficialAutoPlay,
                SaveGuardLogic.Evaluate(true, true, false, false, false, true, true));
            // 공식 곡 AllPerfect 차단이 먼저 판정된다(둘 다 막는 결과는 같음).
            Assert.Equal(SaveGuardDecision.BlockOfficialAllPerfect,
                SaveGuardLogic.Evaluate(true, true, false, false, true, false, true));
            // 커스텀 트랙/커스텀 플레이 판정은 autoplay와 무관하게 그대로.
            Assert.Equal(SaveGuardDecision.BlockCustomTrack,
                SaveGuardLogic.Evaluate(true, true, true, true, false, true, true));
            // 결과·트랙 추출에 실패해도 autoplay면 어느 곡이든 자동 연주 결과이므로 막는다.
            Assert.Equal(SaveGuardDecision.BlockOfficialAutoPlay,
                SaveGuardLogic.Evaluate(true, false, false, false, false, true, true));
            Assert.Equal(SaveGuardDecision.BlockOfficialAutoPlay,
                SaveGuardLogic.Evaluate(false, false, false, false, false, true, true));
        }
    }
}
