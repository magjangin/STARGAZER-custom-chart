using System;
using System.IO;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class CustomAlbumTests : IDisposable
    {
        private readonly string _tempDir;

        public CustomAlbumTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sgz_test_album_" + Guid.NewGuid().ToString("N"));
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
        public void TryLoad_NormalizesPathsOnce()
        {
            File.WriteAllText(Path.Combine(_tempDir, "chart.bms"), "#BPM 120");
            File.WriteAllText(Path.Combine(_tempDir, "music.ogg"), "dummy");
            File.WriteAllText(Path.Combine(_tempDir, "jacket.png"), "dummy");

            // 작업 디렉터리를 바꾸면 병렬 테스트에 영향을 주므로 ".." 세그먼트로 정규화 여부를 본다.
            string unnormalized = Path.Combine(_tempDir, "..", Path.GetFileName(_tempDir));
            CustomAlbum? album = CustomAlbum.TryLoad(unnormalized);

            Assert.NotNull(album);
            Assert.Equal(Path.GetFullPath(_tempDir), album!.DirectoryPath);
            foreach (string? path in new[] { album.MusicPath, album.PreviewPath, album.JacketPath, album.BmsPath })
            {
                Assert.NotNull(path);
                Assert.Equal(Path.GetFullPath(path!), path);
                Assert.DoesNotContain("..", path!);
            }
        }

        [Fact]
        public void TryLoad_ResolvesDifficultySpecificBmsWithFallback()
        {
            File.WriteAllText(Path.Combine(_tempDir, "default.bms"), "#BPM 120");
            File.WriteAllText(Path.Combine(_tempDir, "cosmic.bms"), "#BPM 100");
            File.WriteAllText(Path.Combine(_tempDir, "song_void.bms"), "#BPM 200");
            File.WriteAllText(Path.Combine(_tempDir, "music.ogg"), "dummy");

            CustomAlbum? album = CustomAlbum.TryLoad(_tempDir);

            Assert.NotNull(album);
            Assert.EndsWith("cosmic.bms", album!.GetBmsPath("Cosmic"));
            Assert.EndsWith("song_void.bms", album.GetBmsPath("Void"));
            Assert.EndsWith("default.bms", album.GetBmsPath("Stellar"));
        }

        [Fact]
        public void TryLoad_ExcludesReadmeTxtAndBackgroundImagesFromFallbacks()
        {
            File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "not info");
            File.WriteAllText(Path.Combine(_tempDir, "bg.png"), "bg");
            File.WriteAllText(Path.Combine(_tempDir, "art.png"), "art");
            File.WriteAllText(Path.Combine(_tempDir, "chart.bms"), "#BPM 120");

            CustomAlbum? album = CustomAlbum.TryLoad(_tempDir);

            Assert.NotNull(album);
            Assert.Null(album!.Info);
            Assert.EndsWith("art.png", album.JacketPath);
            Assert.Single(album.Warnings);
        }
    }
}
