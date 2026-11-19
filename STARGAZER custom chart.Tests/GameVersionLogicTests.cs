using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class GameVersionLogicTests
    {
        private const string SampleAcf = "\"AppState\"\n{\n\t\"appid\"\t\t\"3446490\"\n\t\"buildid\"\t\t\"21205351\"\n"
            + "\t\"TargetBuildID\"\t\t\"21300000\"\n\t\"AutoUpdateBehavior\"\t\t\"0\"\n}\n";

        [Fact]
        public void TryReadAcfValue_ReadsExactKey_NotTargetBuildId()
        {
            Assert.Equal("21205351", GameVersionLogic.TryReadAcfValue(SampleAcf, "buildid"));
            Assert.Equal("21300000", GameVersionLogic.TryReadAcfValue(SampleAcf, "TargetBuildID"));
            Assert.Equal("0", GameVersionLogic.TryReadAcfValue(SampleAcf, "autoupdatebehavior"));
            Assert.Null(GameVersionLogic.TryReadAcfValue(SampleAcf, "missing"));
            Assert.Null(GameVersionLogic.TryReadAcfValue(null, "buildid"));
        }

        [Fact]
        public void ClassifyBuild_ComparesWithPinnedBuild()
        {
            Assert.Equal(SteamBuildState.Pinned, GameVersionLogic.ClassifyBuild(GameVersionLogic.PinnedSteamBuildId));
            Assert.Equal(SteamBuildState.Different, GameVersionLogic.ClassifyBuild("99999999"));
            Assert.Equal(SteamBuildState.Unknown, GameVersionLogic.ClassifyBuild(" "));
        }

        [Fact]
        public void IsAutoUpdateGuarded_RequiresBehaviorOneOrReadOnly()
        {
            Assert.True(GameVersionLogic.IsAutoUpdateGuarded("1", acfReadOnly: false));
            Assert.True(GameVersionLogic.IsAutoUpdateGuarded("0", acfReadOnly: true));
            Assert.False(GameVersionLogic.IsAutoUpdateGuarded("0", acfReadOnly: false));
            Assert.False(GameVersionLogic.IsAutoUpdateGuarded(null, acfReadOnly: false));
        }

        [Fact]
        public void Compare_DetectsFirstRunSameAndChanged()
        {
            string current = GameVersionLogic.BuildFingerprint(100, 200, 300, 400);
            Assert.Equal("v2|ga=100:200|meta=300:400", current);
            Assert.Equal(FingerprintComparison.FirstRun, GameVersionLogic.Compare(null, current));
            Assert.Equal(FingerprintComparison.Same, GameVersionLogic.Compare(current + "\n", current));
            Assert.Equal(FingerprintComparison.Changed,
                GameVersionLogic.Compare(GameVersionLogic.BuildFingerprint(100, 200, 300, 999), current));
        }

        [Fact]
        public void Compare_LegacyV1_ComparesOnlyGameAssembly()
        {
            string current = GameVersionLogic.BuildFingerprint(100, 200, 300, 400);
            Assert.Equal(FingerprintComparison.Same, GameVersionLogic.Compare("100:200", current));
            Assert.Equal(FingerprintComparison.Changed, GameVersionLogic.Compare("100:201", current));
        }

        [Fact]
        public void HasPartChanged_NamesTheChangedFile()
        {
            string previous = GameVersionLogic.BuildFingerprint(100, 200, 300, 400);
            string current = GameVersionLogic.BuildFingerprint(100, 200, 300, 401);
            Assert.False(GameVersionLogic.HasPartChanged(previous, current, "ga"));
            Assert.True(GameVersionLogic.HasPartChanged(previous, current, "meta"));

            // v1에는 meta 정보가 없으므로 바뀌었다고 단정하지 않는다.
            Assert.True(GameVersionLogic.HasPartChanged("100:199", current, "ga"));
            Assert.False(GameVersionLogic.HasPartChanged("100:199", current, "meta"));
        }

        [Fact]
        public void BuildFingerprint_MissingMetadata_UsesNone()
        {
            string fingerprint = GameVersionLogic.BuildFingerprint(1, 2, null, null);
            Assert.Equal("none", GameVersionLogic.TryGetPart(fingerprint, "meta"));
            Assert.Equal("1:2", GameVersionLogic.TryGetPart(fingerprint, "ga"));
        }

        [Fact]
        public void Compare_MissingMetadataOnEitherSide_IsNotTreatedAsUpdate()
        {
            string withMeta = GameVersionLogic.BuildFingerprint(100, 200, 300, 400);
            string withoutMeta = GameVersionLogic.BuildFingerprint(100, 200, null, null);
            Assert.Equal(FingerprintComparison.Same, GameVersionLogic.Compare(withoutMeta, withMeta));
            Assert.Equal(FingerprintComparison.Same, GameVersionLogic.Compare(withMeta, withoutMeta));
            Assert.Equal(FingerprintComparison.Changed,
                GameVersionLogic.Compare(withoutMeta, GameVersionLogic.BuildFingerprint(101, 200, 300, 400)));
        }
    }
}
