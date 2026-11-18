using System;
using System.IO;
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
                "#00016:001000000",
                "#00112:002003",
                "#00113:004005"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Equal(180d, chart!.Bpm);
            Assert.Equal(BmsNoteKind.Normal, chart.SoundKinds["01"]);
            Assert.Equal(BmsNoteKind.HoldStart, chart.SoundKinds["02"]);
            Assert.Equal(BmsNoteKind.HoldEnd, chart.SoundKinds["03"]);
            Assert.Equal(BmsNoteKind.HoldStart, chart.SoundKinds["04"]);
            Assert.Equal(BmsNoteKind.HoldEnd, chart.SoundKinds["05"]);

            Assert.Equal(2, chart.Measures.Count);
            Assert.Single(chart.Measures[0].Notes);
            Assert.Equal("01", chart.Measures[0].Notes[0].SoundId);
            Assert.Equal(0, chart.Measures[0].Notes[0].BeatNumerator);
            Assert.Equal(4, chart.Measures[1].Notes.Count);
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
                "#00003:80",
                "#00009:01",
                "#00016:0100"));

            BmsChart? chart = BmsChart.TryParse(path);

            Assert.NotNull(chart);
            Assert.Equal(120d, chart!.Bpm);
            Assert.Equal(3, chart.Warnings.Count);
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
    }
}
