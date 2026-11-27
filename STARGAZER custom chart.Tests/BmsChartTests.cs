using System;
using System.IO;
using System.Linq;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class BmsChartTests : IDisposable
    {
        private readonly string _tempDir;

        public BmsChartTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sgz_test_bms_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private string WriteBms(string name, string content)
        {
            string path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void TryParse_ThreeDigitSoundIdsAndHoldClassification_Works()
        {
            string path = WriteBms("extended.bms", string.Join("\n",
                "#BPM 180",
                "#BPM01 240",
                "#WAV001 normal.wav",
                "#WAV002 hold 시작.wav",
                "#WAV003 hold 끝.wav",
                "#WAV004 hold 开始.wav",
                "#WAV005 hold 结束.wav",
                "#WAV006 hold 開始.wav",
                "#WAV007 hold 終了.wav",
                "#00016:001000000",
                "#00112:002003",
                "#00113:004005",
                "#00114:006007"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Equal(180d, chart!.Bpm);
            Assert.Equal(BmsNoteKind.Normal, chart.SoundKinds["01"]);
            Assert.Equal(BmsNoteKind.HoldStart, chart.SoundKinds["02"]);
            Assert.Equal(BmsNoteKind.HoldEnd, chart.SoundKinds["03"]);
            Assert.Equal(BmsNoteKind.HoldStart, chart.SoundKinds["04"]);
            Assert.Equal(BmsNoteKind.HoldEnd, chart.SoundKinds["05"]);
            Assert.Equal(BmsNoteKind.HoldStart, chart.SoundKinds["06"]);
            Assert.Equal(BmsNoteKind.HoldEnd, chart.SoundKinds["07"]);

            Assert.Equal(2, chart.Measures.Count);
            Assert.Single(chart.Measures[0].Notes);
            Assert.Equal("01", chart.Measures[0].Notes[0].SoundId);
            Assert.Equal(0, chart.Measures[0].Notes[0].BeatNumerator);
            Assert.Equal(6, chart.Measures[1].Notes.Count);
        }

        [Fact]
        public void TryParse_FillsEmptyMeasuresBetweenZeroAndMax()
        {
            string path = WriteBms("sparse.bms", string.Join("\n",
                "#BPM 140",
                "#WAV01 tap.wav",
                "#00316:0100"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Equal(4, chart!.Measures.Count);
            Assert.Empty(chart.Measures[0].Notes);
            Assert.Empty(chart.Measures[1].Notes);
            Assert.Empty(chart.Measures[2].Notes);
            Assert.Single(chart.Measures[3].Notes);
        }

        [Fact]
        public void TryParse_Channel02MeasureLength_ScalesLengthRatioAndBeatPositions()
        {
            string path = WriteBms("time_sig.bms", string.Join("\n",
                "#BPM 120",
                "#WAV01 tap.wav",
                "#00002:0.75",
                "#00016:010101"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Empty(chart!.Warnings);
            Assert.Single(chart.Measures);
            Assert.Equal(0.75d, chart.Measures[0].LengthRatio, 6);
            Assert.Equal(3, chart.Measures[0].Notes.Count);
            // 3/4 마디(3비트)를 3등분하면 박자 위치는 0, 1, 2
            Assert.Equal(0, chart.Measures[0].Notes[0].BeatNumerator);
            Assert.Equal(1, chart.Measures[0].Notes[0].BeatDenominator);
            Assert.Equal(1, chart.Measures[0].Notes[1].BeatNumerator);
            Assert.Equal(1, chart.Measures[0].Notes[1].BeatDenominator);
            Assert.Equal(2, chart.Measures[0].Notes[2].BeatNumerator);
            Assert.Equal(1, chart.Measures[0].Notes[2].BeatDenominator);
        }

        [Fact]
        public void TryParse_UnsupportedChannelsAndSpacelessBpm_RecordWarnings()
        {
            string path = WriteBms("warn.bms", string.Join("\n",
                "#BPM140",
                "#WAV01 tap.wav",
                "#00009:01",
                "#00016:0100"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Equal(120d, chart!.Bpm);
            Assert.Equal(2, chart.Warnings.Count);
        }

        [Fact]
        public void TryParse_LnObj_ConvertsToHoldStartAndEnd()
        {
            string path = WriteBms("lnobj.bms", string.Join("\n",
                "#BPM 150",
                "#WAV01 normal.wav",
                "#WAV02 dummy.wav",
                "#LNOBJ 02",
                "#00016:01000200"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Single(chart!.Measures);
            Assert.Equal(2, chart.Measures[0].Notes.Count);

            BmsNoteEvent startNote = chart.Measures[0].Notes[0];
            BmsNoteEvent endNote = chart.Measures[0].Notes[1];

            Assert.Equal(BmsNoteKind.HoldStart, startNote.Kind);
            Assert.Equal("01", startNote.SoundId);
            Assert.Equal(BmsNoteKind.HoldEnd, endNote.Kind);
            Assert.Equal("02", endNote.SoundId);
        }

        [Fact]
        public void TryParse_LnChannels_ConvertsToMappedChannelHoldPairs()
        {
            string path = WriteBms("lnchannels.bms", string.Join("\n",
                "#BPM 160",
                "#WAV01 tap.wav",
                "#00056:01000100")); // 56 -> 16 (4번 레인)

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Single(chart!.Measures);
            Assert.Equal(2, chart.Measures[0].Notes.Count);

            BmsNoteEvent startNote = chart.Measures[0].Notes[0];
            BmsNoteEvent endNote = chart.Measures[0].Notes[1];

            Assert.Equal(16, startNote.Channel);
            Assert.Equal(BmsNoteKind.HoldStart, startNote.Kind);
            Assert.Equal(16, endNote.Channel);
            Assert.Equal(BmsNoteKind.HoldEnd, endNote.Kind);
        }

        [Fact]
        public void TryParse_7KeyAnd2PChannels_NormalizesToCoreGameLanes()
        {
            string path = WriteBms("channels_7k_2p.bms", string.Join("\n",
                "#BPM 160",
                "#WAV01 tap.wav",
                "#00014:01",  // 1P 4번 건반 -> 16 (위)
                "#00015:01",  // 1P 5번 건반 -> 12 (오른쪽)
                "#00018:01",  // 1P 6번 건반 -> 11 (왼쪽)
                "#00019:01",  // 1P 7번 건반 -> 13 (아래)
                "#00026:01",  // 2P 스크래치 -> 16 (위)
                "#00022:01",  // 2P 2번 건반 -> 12 (오른쪽)
                "#00066:01000100")); // 2P LN 26 -> 16 홀드 페어

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Single(chart!.Measures);
            Assert.Equal(8, chart.Measures[0].Notes.Count);

            // 1P 확장 건반 검증
            Assert.Contains(chart.Measures[0].Notes, n => n.Channel == 16 && n.Kind == BmsNoteKind.Normal);
            Assert.Contains(chart.Measures[0].Notes, n => n.Channel == 12 && n.Kind == BmsNoteKind.Normal);
            Assert.Contains(chart.Measures[0].Notes, n => n.Channel == 11 && n.Kind == BmsNoteKind.Normal);
            Assert.Contains(chart.Measures[0].Notes, n => n.Channel == 13 && n.Kind == BmsNoteKind.Normal);

            // 2P LN 검증 (66 -> 26 -> 16)
            var lnNotes = chart.Measures[0].Notes.Where(n => n.Channel == 16 && n.Kind != BmsNoteKind.Normal).ToList();
            Assert.Equal(2, lnNotes.Count);
            Assert.Equal(BmsNoteKind.HoldStart, lnNotes[0].Kind);
            Assert.Equal(BmsNoteKind.HoldEnd, lnNotes[1].Kind);
        }

        [Fact]
        public void TryParse_Channel03And08_AppliesMeasureBpm()
        {
            string path = WriteBms("bpm_changes.bms", string.Join("\n",
                "#BPM 120",
                "#BPM01 200",
                "#WAV01 tap.wav",
                "#00016:0100",
                "#00103:96",       // 0x96 = 150 BPM
                "#00116:0100",
                "#00208:01",       // #BPM01 = 200 BPM
                "#00216:0100",
                "#00316:0100"));    // 3번 마디는 2번 마디의 200 BPM 지속

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Equal(4, chart!.Measures.Count);
            Assert.Equal(120.0, chart.Measures[0].Bpm);
            Assert.Equal(150.0, chart.Measures[1].Bpm);
            Assert.Equal(200.0, chart.Measures[2].Bpm);
            Assert.Equal(200.0, chart.Measures[3].Bpm);
        }

        [Fact]
        public void TryGetOrParse_HotReloadsWhenFileTimestampChanges()
        {
            string path = WriteBms("reload.bms", "#BPM 120\n#WAV01 a.wav\n#00016:01");
            BmsChart? first = BmsChart.TryGetOrParse(path);
            Assert.NotNull(first);
            Assert.Equal(120d, first!.Bpm);

            File.WriteAllText(path, "#BPM 175\n#WAV01 a.wav\n#00016:01");
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));

            BmsChart? second = BmsChart.TryGetOrParse(path);
            Assert.NotNull(second);
            Assert.Equal(175d, second!.Bpm);
        }

        [Fact]
        public void TryParse_EmptyOrHeaderOnlyChart_ReturnsNoMeasures()
        {
            string path = WriteBms("empty.bms", string.Join("\n",
                "#BPM 140",
                "#WAV01 tap.wav",
                "#00001:01"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Empty(chart!.Measures);
        }
    }
}
