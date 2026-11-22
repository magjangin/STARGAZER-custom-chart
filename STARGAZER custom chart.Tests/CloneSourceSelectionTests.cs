using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class CloneSourceSelectionTests
    {
        [Fact]
        public void Pick_PrefersExactIdOverEarlierPrefixMatch()
        {
            // 목록 순서상 Jazz Arrange(접두 일치)가 원곡보다 앞에 있어도 원곡을 복제 원본으로 골라야 한다.
            string?[] ids = { "other", "startingpoint_jazzarrange", "startingpoint", "last" };

            string? picked = CloneSourceSelection.Pick(ids, id => id, out bool exact);

            Assert.Equal("startingpoint", picked);
            Assert.True(exact);
        }

        [Fact]
        public void Pick_FallsBackToFirstPrefixMatchWhenNoExactId()
        {
            string?[] ids = { "other", "startingpoint_v2", "startingpoint_jazz" };

            string? picked = CloneSourceSelection.Pick(ids, id => id, out bool exact);

            Assert.Equal("startingpoint_v2", picked);
            Assert.False(exact);
        }

        [Fact]
        public void Pick_IsCaseInsensitiveAndTrimsIds()
        {
            string?[] ids = { "  StartingPoint  " };

            string? picked = CloneSourceSelection.Pick(ids, id => id, out bool exact);

            Assert.Equal("  StartingPoint  ", picked);
            Assert.True(exact);
        }

        [Fact]
        public void Pick_SkipsNullIdsAndReturnsDefaultWhenNothingMatches()
        {
            string?[] ids = { null, "other", "another" };

            string? picked = CloneSourceSelection.Pick(ids, id => id, out bool exact);

            Assert.Null(picked);
            Assert.False(exact);
        }

        [Fact]
        public void Pick_WorksOnLazySequencesAndStopsAtExactMatch()
        {
            int enumerated = 0;
            System.Collections.Generic.IEnumerable<string> Source()
            {
                foreach (string id in new[] { "a", "startingpoint", "b", "c" })
                {
                    enumerated++;
                    yield return id;
                }
            }

            string? picked = CloneSourceSelection.Pick(Source(), id => id, out bool exact);

            Assert.Equal("startingpoint", picked);
            Assert.True(exact);
            Assert.Equal(2, enumerated);
        }
    }
}
