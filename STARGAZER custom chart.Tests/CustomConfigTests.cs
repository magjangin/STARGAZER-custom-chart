using System;
using System.IO;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class CustomConfigTests : IDisposable
    {
        private readonly string _tempDir;

        public CustomConfigTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sgz_test_cfg_" + Guid.NewGuid().ToString("N"));
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
        public void LoadFromFolder_CreatesDefaultFileWithSafeDefaults()
        {
            CustomConfig.Settings settings = CustomConfig.LoadFromFolder(_tempDir);

            Assert.True(File.Exists(Path.Combine(_tempDir, "config.txt")));
            Assert.False(settings.AutoPlay);
            Assert.False(settings.AllPerfect);
            Assert.True(settings.AllPerfectCustomOnly);
            Assert.True(settings.UnlockAll);
            Assert.False(settings.VerboseInvocationLogging);
            Assert.True(settings.FocusedHarmonyInvocationLogging);
            Assert.Equal(24, settings.BgmCacheMaxEntries);
            Assert.Equal(32, settings.JacketCacheMaxEntries);
        }

        [Fact]
        public void LoadFromFolder_ParsesJacketCacheMaxEntries()
        {
            File.WriteAllText(Path.Combine(_tempDir, "config.txt"), "JacketCacheMaxEntries = 64\nBgmCacheMaxEntries = 12");

            CustomConfig.Settings settings = CustomConfig.LoadFromFolder(_tempDir);

            Assert.Equal(64, settings.JacketCacheMaxEntries);
            Assert.Equal(12, settings.BgmCacheMaxEntries);
        }

        [Fact]
        public void LoadFromFolder_ParsesLocalizedBoolsAndNormalizesChaosRange()
        {
            File.WriteAllText(Path.Combine(_tempDir, "config.txt"), string.Join("\n",
                "autoplay = 켜기",
                "AllPerfect : 是",
                "NoteSpeedChaos = 1",
                "NoteSpeedChaosMin = 2.5",
                "NoteSpeedChaosMax = 0.01"));

            CustomConfig.Settings settings = CustomConfig.LoadFromFolder(_tempDir);

            Assert.True(settings.AutoPlay);
            Assert.True(settings.AllPerfect);
            Assert.True(settings.NoteSpeedChaos);
            Assert.Equal(0.05f, settings.NoteSpeedChaosMin, 3);
            Assert.Equal(2.5f, settings.NoteSpeedChaosMax, 3);
        }
    
        [Fact]
        public void FindUnknownKeys_SuggestsNearestKnownKeyForTypos()
        {
            var unknown = CustomConfig.FindUnknownKeys(new[] { "AllPerfet", "auto_play", "UnlockAll", "autoplay", "Note Sway", "TotallyUnrelatedSetting" });

            Assert.Equal(4, unknown.Count);
            Assert.Contains(("AllPerfet", (string?)"AllPerfect"), unknown);
            Assert.Contains(("auto_play", (string?)"autoplay"), unknown);
            Assert.Contains(("Note Sway", (string?)"NoteSway"), unknown);
            Assert.Contains(("TotallyUnrelatedSetting", (string?)null), unknown);
        }

        [Fact]
        public void FindUnknownKeys_KnownKeysAreCaseInsensitive()
        {
            Assert.Empty(CustomConfig.FindUnknownKeys(new[] { "AUTOPLAY", "allperfect", "bgmcachemaxentries", "jacketcachemaxentries" }));
            // 예전 버전이 만들던 설정은 오타로 보지 않는다.
            Assert.Empty(CustomConfig.FindUnknownKeys(new[] { "EnableNoiseSuppression" }));
        }
    }
}
