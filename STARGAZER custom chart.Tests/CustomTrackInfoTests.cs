using System;
using System.IO;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class CustomTrackInfoTests : IDisposable
    {
        private readonly string _tempDir;

        public CustomTrackInfoTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sgz_test_info_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public void TryParse_SupportsMultilingualKeysAndLevels()
        {
            string path = Path.Combine(_tempDir, "info.txt");
            File.WriteAllText(path, string.Join("\n",
                "# comment",
                "曲名 = 星之门",
                "作曲 : Hwa",
                "宇宙 = 3",
                "恒星 : 8",
                "void = 14+"));

            CustomTrackInfo? info = CustomTrackInfo.TryParse(path);

            Assert.NotNull(info);
            Assert.Equal("星之门", info!.Title);
            Assert.Equal("Hwa", info.Artist);
            Assert.Equal("3", info.Levels["cosmic"]);
            Assert.Equal("8", info.Levels["stellar"]);
            Assert.Equal("14+", info.Levels["void"]);
        }
    }
}
