namespace STARGAZER_custom_chart
{
    internal static class ExperimentChartSettings
    {
        public const bool EnableKeepEarliestOnlyChart = true;
        public const bool EnableBeatInfoShiftTest = false;
        public const bool EnableLaneShiftTest = true;
        public const bool EnableShortNoteTest = true;
        public const bool EnableLongNoteTest = true;
        public const bool EnableAreaCreationTest = false;

        // 켜지면 hwa/*.bms를 파싱해서 Layer.Areas 전체를 교체하는 뼈대 테스트를 돈다(숏노트/롱노트 지원,
        // BPM 변경/마디 길이 변경 미지원). 켜져 있으면 아래 오프셋 기반 실험 노트 추가는 건너뛴다.
        public const bool EnableBmsChartTest = true;

        // BMS 파싱 시 동일 레인/동일 틱 위치의 중복/노이즈 노트 억제 및 디버그 로깅 활성화
        public static bool EnableNoiseSuppression => false;
        public static bool EnableNoiseSuppressionDebug => false;

        // info.txt의 난이도(cosmic/stellar/void)를 트랙 메타데이터(levelString)에 반영할지 여부.
        // 켜지 말 것 — 켜면 곡 진입 후 로딩 화면에서 영구 대기한다(2026-08-08 실측 2회).
        //
        // 실험 1 (분리 없음): 공유 Dictionary에 8/8/8 기록 -> 멈춤.
        // 실험 2 (분리 적용): 클론 전용 Dictionary에만 8/8/8 기록, 공식 트랙 Dictionary는 무손상 -> 그래도 멈춤.
        //   [TrackLevel] shared=0x214FBBF1F00 new=0x214FDAED000 / cosmic '1'->'8'
        //   [PatternLoad] trackId=startingpoint level=Cosmic levelString=8 custom=True
        //   -> 직후 AddBundle까지만 진행되고 PlayerBase.Play가 오지 않음.
        // 두 실험의 공통 변수는 "값 변경" 하나뿐이므로, 난이도 문자열은 단순 표시용이 아니라
        // 패턴 에셋 로딩 경로에서 읽힌다고 결론. (에셋 이름에 난이도가 포함돼 존재하지 않는 이름이
        // 조회되고 비동기 콜백이 오지 않는 것으로 추정 — 인터롭에 본문이 없어 코드 확인은 불가.)
        //
        // 난이도 표시를 바꾸려면 메타데이터가 아니라 UI 쪽(LevelItem.levelText 등 TextProvider)을
        // 덮어써야 한다. 그러면 로더가 읽는 값은 원본 그대로 유지된다.
        public const bool EnableTrackLevelOverride = false;

        // BeatValue는 사실상 BeatIndex / BeatSplit이며, 1비트 이동은 BeatIndex에 BeatSplit을 더합니다.
        public const int ShortNoteBeatOffset = 1;
        public const int ShortNoteSplitOffset = 0;
        public const int LongNoteStartBeatOffset = 2;
        public const int LongNoteStartSplitOffset = 0;
        public const int LongNoteEndBeatOffset = 4;
        public const int LongNoteEndSplitOffset = 0;
    }
}
