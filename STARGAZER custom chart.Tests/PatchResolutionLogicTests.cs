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

        private enum FakeJudgement { BLUE = 0 }

        private sealed class FakeOuterWithUniqueFallback
        {
            private sealed class PlayResultBuffer { }
        }

        private sealed class FakeOuterWithAmbiguousFallback
        {
            private sealed class PlayResultA { }
            private sealed class PlayResultB { }
        }

        private sealed class FakeJudgeExtensions
        {
            public static FakeJudgement FromTimeDifference(float a, float b, ref float timing) => FakeJudgement.BLUE;
            public static FakeJudgement RenamedMethod(float a, float b, float nonRef) => FakeJudgement.BLUE;
            public static void ProcessBufferRenamed(OtherArg arg) { }
        }

        [Fact]
        public void ResolveNestedType_FallsBackOnlyWhenUnique()
        {
            var (unique, usedFallback) = PatchResolutionLogic.ResolveNestedType(
                typeof(FakeOuterWithUniqueFallback), "INNER_Result", "Result");
            Assert.NotNull(unique);
            Assert.True(usedFallback);
            Assert.Equal("PlayResultBuffer", unique!.Name);

            var (ambiguous, usedFallback2) = PatchResolutionLogic.ResolveNestedType(
                typeof(FakeOuterWithAmbiguousFallback), "INNER_Result", "Result");
            Assert.Null(ambiguous);
            Assert.False(usedFallback2);
        }

        [Fact]
        public void ResolveMethodByShortSignature_MatchesAlternateNameAndByRefDistinction()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var (method, reason) = PatchResolutionLogic.ResolveMethodByShortSignature(
                typeof(FakeJudgeExtensions),
                flags,
                new[] { "FromTimeDiffernce", "FromTimeDifference" },
                "TimeDiffer",
                "FakeJudgement",
                new[] { ("Single", false), ("Single", false), ("Single", true) });

            Assert.NotNull(method);
            Assert.Equal("FromTimeDifference", method!.Name);
            Assert.NotNull(reason);
        }

        [Fact]
        public void ResolveMethodByShortSignature_FallsBackByUniqueFragmentAndSignature()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var (method, reason) = PatchResolutionLogic.ResolveMethodByShortSignature(
                typeof(FakeJudgeExtensions),
                flags,
                new[] { "BufferProcess" },
                "Buffer",
                "Void",
                new[] { ("OtherArg", false) });

            Assert.NotNull(method);
            Assert.Equal("ProcessBufferRenamed", method!.Name);
            Assert.Equal("이름 조각+시그니처 유일", reason);
        }

        private sealed class FakeLoaderWithTwoOuterLambdas
        {
            private void _Load_b__1_0(Pattern p) { }
            private void _Load_b__2_0(Pattern p) { }
        }

        private sealed class FakeSignatureOverloads
        {
            public void Target(PatternLoader loader) { }
            public void Target(Pattern pattern) { }
        }

        [Fact]
        public void ResolveLambdaMethod_ReturnsNullWhenOuterLambdasAreAmbiguous()
        {
            var (chosen, ambiguous) = PatchResolutionLogic.ResolveLambdaMethod(
                typeof(FakeLoaderWithTwoOuterLambdas),
                "Load",
                1,
                new[] { "Pattern" });

            Assert.Null(chosen);
            Assert.Equal(2, ambiguous.Count);
        }

        [Fact]
        public void PickBestBySignature_PrefersExactTypeMatchAndRespectsExactFlag()
        {
            MethodInfo[] methods = typeof(FakeSignatureOverloads).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            MethodInfo? best = PatchResolutionLogic.PickBestBySignature(methods, 1, new[] { "Pattern" }, exactTypeNames: false);
            Assert.NotNull(best);
            Assert.Equal(typeof(Pattern), best!.GetParameters()[0].ParameterType);

            MethodInfo? loaderOnly = methods[0].GetParameters()[0].ParameterType == typeof(PatternLoader) ? methods[0] : methods[1];
            Assert.Equal(-1, PatchResolutionLogic.ScoreSignature(loaderOnly, 1, new[] { "Pattern" }, exactTypeNames: true));
            Assert.Equal(1, PatchResolutionLogic.ScoreSignature(loaderOnly, 1, new[] { "Pattern" }, exactTypeNames: false));
        }

        [Fact]
        public void FindDispatchTableMismatches_ConsistentTable_ReturnsEmpty()
        {
            var specs = new[] { ("A.Play", true), ("A.Set", false) };
            var handlers = new System.Collections.Generic.Dictionary<string, bool> { ["A.Play"] = true, ["A.Set"] = false };
            Assert.Empty(PatchResolutionLogic.FindDispatchTableMismatches(specs, handlers, new[] { "A.Play" }));
        }

        [Fact]
        public void FindDispatchTableMismatches_ReportsEveryKindOfDrift()
        {
            var specs = new[] { ("A.Play", true), ("A.Set", true), ("A.NoHandler", false), ("A.Play", true) };
            var handlers = new System.Collections.Generic.Dictionary<string, bool>
            {
                ["A.Play"] = true,
                ["A.Set"] = false,
                ["A.Orphan"] = false,
            };

            var problems = PatchResolutionLogic.FindDispatchTableMismatches(specs, handlers, new[] { "A.Ghost" });

            Assert.Contains(problems, p => p.Contains("중복") && p.Contains("A.Play"));
            Assert.Contains(problems, p => p.Contains("단계 불일치") && p.Contains("A.Set"));
            Assert.Contains(problems, p => p.Contains("매핑 누락") && p.Contains("A.NoHandler"));
            Assert.Contains(problems, p => p.Contains("PatchSpec 없는 핸들러") && p.Contains("A.Orphan"));
            Assert.Contains(problems, p => p.Contains("포커스") && p.Contains("A.Ghost"));
            Assert.Equal(5, problems.Count);
        }

        private enum EJudgements { BLUE }

        private sealed class FakeJudgementExtension
        {
            private EJudgements FromTimeDiffernce(float noteTiming, float patternTime, out float judgeTiming)
            {
                judgeTiming = noteTiming - patternTime;
                return EJudgements.BLUE;
            }
        }

        // 회귀 방지(2026-11-19): 실제 게임의 FromTimeDiffernce/TryJudgeHead는 인스턴스 메서드다.
        // Static 전용 플래그로 찾으면 조용히 실패하므로 JudgeAllPerfectPatch는 Instance|Static을 함께 넘긴다.
        [Fact]
        public void ResolveMethodByShortSignature_InstanceJudgeMethod_RequiresInstanceFlag()
        {
            var expected = new[] { ("Single", false), ("Single", false), ("Single", true) };
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;

            var (staticOnly, _) = PatchResolutionLogic.ResolveMethodByShortSignature(
                typeof(FakeJudgementExtension), any | BindingFlags.Static,
                new[] { "FromTimeDiffernce" }, "TimeDiffer", "EJudgements", expected);
            var (both, reason) = PatchResolutionLogic.ResolveMethodByShortSignature(
                typeof(FakeJudgementExtension), any | BindingFlags.Static | BindingFlags.Instance,
                new[] { "FromTimeDiffernce" }, "TimeDiffer", "EJudgements", expected);

            Assert.Null(staticOnly);
            Assert.NotNull(both);
            Assert.Equal("FromTimeDiffernce", both!.Name);
            Assert.Null(reason);
        }
    }
}
