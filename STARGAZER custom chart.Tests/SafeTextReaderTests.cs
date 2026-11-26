using System;
using System.IO;
using System.Text;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class SafeTextReaderTests : IDisposable
    {
        private readonly string _tempDir;

        public SafeTextReaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sgz_test_str_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            SafeTextReader.EnsureInitialized();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public void ReadAllLines_Utf8Bom_StripsBomFromFirstLine()
        {
            string path = Path.Combine(_tempDir, "bom.bms");
            byte[] utf8Bytes = Encoding.UTF8.GetBytes("#BPM 150\n#WAV01 kick.wav");
            byte[] withBom = new byte[3 + utf8Bytes.Length];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(utf8Bytes, 0, withBom, 3, utf8Bytes.Length);
            File.WriteAllBytes(path, withBom);

            string[] lines = SafeTextReader.ReadAllLines(path);

            Assert.Equal(2, lines.Length);
            Assert.Equal("#BPM 150", lines[0]);
            Assert.Equal("#WAV01 kick.wav", lines[1]);
        }

        [Fact]
        public void ReadAllLines_Utf16LeBom_DecodesCorrectly()
        {
            string path = Path.Combine(_tempDir, "utf16le.txt");
            File.WriteAllText(path, "제목 : 별빛\nartist = Star", Encoding.Unicode);

            string[] lines = SafeTextReader.ReadAllLines(path);

            Assert.Equal(2, lines.Length);
            Assert.Equal("제목 : 별빛", lines[0]);
            Assert.Equal("artist = Star", lines[1]);
        }

        [Fact]
        public void ReadAllLines_Gb18030Fallback_DecodesSimplifiedChinese()
        {
            string path = Path.Combine(_tempDir, "gb18030.txt");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding gb18030 = Encoding.GetEncoding("GB18030");
            File.WriteAllBytes(path, gb18030.GetBytes("曲名 : 星海\n艺术家 : 华英王"));

            string[] lines = SafeTextReader.ReadAllLines(path);

            Assert.Equal(2, lines.Length);
            Assert.Equal("曲名 : 星海", lines[0]);
            Assert.Equal("艺术家 : 华英王", lines[1]);
        }

        [Fact]
        public void ReadAllLines_MissingFile_ReturnsEmpty()
        {
            string[] lines = SafeTextReader.ReadAllLines(Path.Combine(_tempDir, "missing.txt"));
            Assert.Empty(lines);
        }

        [Fact]
        public void EnumerateLines_YieldsLinesLazilyAndStopsEarly()
        {
            string path = Path.Combine(_tempDir, "lazy.txt");
            File.WriteAllText(path, "line1\nline2\nline3\nline4\nline5");

            var read = new System.Collections.Generic.List<string>();
            foreach (string line in SafeTextReader.EnumerateLines(path))
            {
                read.Add(line);
                if (read.Count >= 2)
                {
                    break;
                }
            }

            Assert.Equal(2, read.Count);
            Assert.Equal("line1", read[0]);
            Assert.Equal("line2", read[1]);
        }

        [Fact]
        public void EnumerateLines_Utf8Bom_StripsBomFromFirstLine()
        {
            string path = Path.Combine(_tempDir, "lazy_bom.bms");
            byte[] utf8Bytes = Encoding.UTF8.GetBytes("#TITLE Star\n#ARTIST Hwa");
            byte[] withBom = new byte[3 + utf8Bytes.Length];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(utf8Bytes, 0, withBom, 3, utf8Bytes.Length);
            File.WriteAllBytes(path, withBom);

            var lines = new System.Collections.Generic.List<string>(SafeTextReader.EnumerateLines(path));

            Assert.Equal(2, lines.Count);
            Assert.Equal("#TITLE Star", lines[0]);
            Assert.Equal("#ARTIST Hwa", lines[1]);
        }
    }
}
