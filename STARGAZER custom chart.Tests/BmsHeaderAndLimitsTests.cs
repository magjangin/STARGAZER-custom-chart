using System;
using System.IO;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class BmsHeaderAndLimitsTests : IDisposable
    {
        private readonly string _tempDir;

        public BmsHeaderAndLimitsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sgz_test_header_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private string Write(string name, string content)
        {
            string path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void ReadHeaderMetadata_ReadsTitleAndArtistCaseInsensitively()
        {
            string path = Write("a.bms", "#bpm 120\n#title  Moonlight Sonata \n#ARTIST\tBeethoven\n#00111:01");

            (string? title, string? artist) = BmsChart.ReadHeaderMetadata(path);

            Assert.Equal("Moonlight Sonata", title);
            Assert.Equal("Beethoven", artist);
        }

        [Fact]
        public void ReadHeaderMetadata_IgnoresSubtitleAndEmptyValues()
        {
            string path = Write("b.bms", "#SUBTITLE [Hard]\n#TITLE \n#ARTISTNAME x\n#ARTIST Someone");

            (string? title, string? artist) = BmsChart.ReadHeaderMetadata(path);

            Assert.Null(title);
            Assert.Equal("Someone", artist);
        }

        [Fact]
        public void ReadHeaderMetadata_ReadsMultibyteTitleFromUtf8Bom()
        {
            string path = Path.Combine(_tempDir, "c.bms");
            File.WriteAllText(path, "#TITLE 별빛 노래\n#ARTIST 化영\n", new System.Text.UTF8Encoding(true));

            (string? title, string? artist) = BmsChart.ReadHeaderMetadata(path);

            Assert.Equal("별빛 노래", title);
            Assert.Equal("化영", artist);
        }

        [Fact]
        public void ReadHeaderMetadata_StopsEarlyOnMeasureDataLine()
        {
            string path = Write("early_break.bms", string.Join("\n",
                "#TITLE First Title",
                "#00111:010203",
                "#TITLE Ignored Title",
                "#ARTIST Ignored Artist"));

            (string? title, string? artist) = BmsChart.ReadHeaderMetadata(path);

            Assert.Equal("First Title", title);
            Assert.Null(artist);
        }

        [Fact]
        public void Album_UsesBmsHeaderWhenInfoTxtIsMissing()
        {
            Write("chart.bms", "#TITLE BMS Title\n#ARTIST BMS Artist\n#BPM 120");
            Write("music.ogg", "dummy");

            CustomAlbum? album = CustomAlbum.TryLoad(_tempDir);

            Assert.NotNull(album);
            Assert.Equal("BMS Title", album!.DisplayName);
            Assert.Equal("BMS Artist", album.Artist);
        }

        [Fact]
        public void Album_InfoTxtOverridesBmsHeaderPerField()
        {
            Write("chart.bms", "#TITLE BMS Title\n#ARTIST BMS Artist\n#BPM 120");
            Write("info.txt", "title = Info Title");

            CustomAlbum? album = CustomAlbum.TryLoad(_tempDir);

            Assert.NotNull(album);
            Assert.Equal("Info Title", album!.DisplayName);
            // info.txt에 아티스트가 없으므로 BMS #ARTIST가 쓰인다.
            Assert.Equal("BMS Artist", album.Artist);
        }

        [Fact]
        public void Album_FallsBackToFolderNameAndDefaultArtistWithoutAnyMetadata()
        {
            Write("chart.bms", "#BPM 120");

            CustomAlbum? album = CustomAlbum.TryLoad(_tempDir);

            Assert.NotNull(album);
            Assert.Equal(Path.GetFileName(_tempDir), album!.DisplayName);
            Assert.Equal(CustomAlbum.DefaultArtist, album.Artist);
        }

        [Fact]
        public void SafeTextReader_RejectsFilesOverTheSizeLimit()
        {
            string path = Path.Combine(_tempDir, "huge.txt");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                stream.SetLength(SafeTextReader.MaxTextFileBytes + 1);
            }

            Assert.True(SafeTextReader.IsTooLarge(path));
            Assert.Empty(SafeTextReader.ReadAllLines(path));
            Assert.Null(BmsChart.TryParse(path));
        }

        [Fact]
        public void SafeTextReader_StillReadsFilesAtTheLimitBoundary()
        {
            string path = Write("small.txt", "line1\nline2");

            Assert.False(SafeTextReader.IsTooLarge(path));
            Assert.Equal(new[] { "line1", "line2" }, SafeTextReader.ReadAllLines(path));
            Assert.False(SafeTextReader.IsTooLarge(Path.Combine(_tempDir, "missing.txt")));
        }

        [Fact]
        public void Album_WarnsWhenBmsExceedsSizeLimit()
        {
            string path = Path.Combine(_tempDir, "big.bms");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                stream.SetLength(SafeTextReader.MaxTextFileBytes + 1);
            }

            CustomAlbum? album = CustomAlbum.TryLoad(_tempDir);

            Assert.NotNull(album);
            Assert.Contains(album!.Warnings, w => w.Contains("상한"));
        }
    }
}
