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
    }
}
