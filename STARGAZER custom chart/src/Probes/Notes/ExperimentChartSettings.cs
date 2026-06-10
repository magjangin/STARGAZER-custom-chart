namespace STARGAZER_custom_chart
{
    internal static class ExperimentChartSettings
    {
        public const bool EnableKeepEarliestOnlyChart = true;
        public const bool EnableBeatInfoShiftTest = false;
        public const bool EnableLaneShiftTest = true;
        public const bool EnableShortNoteTest = true;
        public const bool EnableLongNoteTest = true;

        // BeatValue는 사실상 BeatIndex / BeatSplit이며, 1비트 이동은 BeatIndex에 BeatSplit을 더합니다.
        public const int ShortNoteBeatOffset = 1;
        public const int ShortNoteSplitOffset = 0;
        public const int LongNoteStartBeatOffset = 2;
        public const int LongNoteStartSplitOffset = 0;
        public const int LongNoteEndBeatOffset = 4;
        public const int LongNoteEndSplitOffset = 0;
    }
}
