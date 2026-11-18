using System.Reflection;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class PatchResolutionLogicTests
    {
        private sealed class Pattern { }
        private sealed class PatternLoader { }
        private sealed class OtherArg { }

        private sealed class FakeLoaderWithUniqueLambda
        {
            private void _Load_b__9_0(Pattern p) { }
            private void _Other_b__1_0(PatternLoader pl) { }
        }

        private sealed class FakeLoaderWithNestedClosure
        {
            private sealed class __c
            {
                public void _Load_b__5_1(Pattern p) { }
            }
        }

        private sealed class FakeLoaderWithAmbiguousClosures
        {
            private void _Load_b__2_0(Pattern p) { }

            private sealed class __c__DisplayClass0_0
            {
                public void _Load_b__2_1(Pattern p) { }
            }
        }

        [Fact]
        public void TryParseLambdaName_SupportsBothIl2CppAndManagedPatterns()
        {
            Assert.True(PatchResolutionLogic.TryParseLambdaName("_Load_b__5_0", out string owner1));
            Assert.Equal("Load", owner1);

            Assert.True(PatchResolutionLogic.TryParseLambdaName("<Load>b__12_3", out string owner2));
            Assert.Equal("Load", owner2);

            Assert.False(PatchResolutionLogic.TryParseLambdaName("Load", out _));
        }

        [Fact]
        public void ResolveLambdaMethod_RequiresExactParameterTypeMatch()
        {
            var (chosen, ambiguous) = PatchResolutionLogic.ResolveLambdaMethod(
                typeof(FakeLoaderWithUniqueLambda),
                "Load",
                1,
                new[] { "Pattern" });

            Assert.NotNull(chosen);
            Assert.Equal("_Load_b__9_0", chosen!.Name);
            Assert.Single(ambiguous);
        }

        [Fact]
        public void ResolveLambdaMethod_FindsLambdaInNestedClosureType()
        {
            var (chosen, _) = PatchResolutionLogic.ResolveLambdaMethod(
                typeof(FakeLoaderWithNestedClosure),
                "Load",
                1,
                new[] { "Pattern" });

            Assert.NotNull(chosen);
            Assert.Equal("_Load_b__5_1", chosen!.Name);
            Assert.Equal(typeof(FakeLoaderWithNestedClosure).GetNestedType("__c", BindingFlags.NonPublic), chosen.DeclaringType);
        }

        [Fact]
        public void ResolveLambdaMethod_PrefersOuterDeclaringTypeWhenMultipleExist()
        {
            var (chosen, _) = PatchResolutionLogic.ResolveLambdaMethod(
                typeof(FakeLoaderWithAmbiguousClosures),
                "Load",
                1,
                new[] { "Pattern" });

            Assert.NotNull(chosen);
            Assert.Equal(typeof(FakeLoaderWithAmbiguousClosures), chosen!.DeclaringType);
            Assert.Equal("_Load_b__2_0", chosen.Name);
        }

        [Fact]
        public void GetShortTypeKey_StripsNamespacePreservingNestedType()
        {
            Assert.Equal("StargazerPlayer+INNER_PatternLoader",
                PatchResolutionLogic.GetShortTypeKey("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader"));
        }
    }
}
