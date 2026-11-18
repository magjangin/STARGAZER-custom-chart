using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace STARGAZER_custom_chart
{
    /// <summary>
    /// MelonLoader/Harmony 의존성 없이 타깃 타입·메서드·람다 해석 규칙만 담당하는 순수 리플렉션 유틸리티.
    /// </summary>
    internal static class PatchResolutionLogic
    {
        private const BindingFlags AllDeclaredMethods =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Il2CppInterop은 <Load>b__5_0 을 _Load_b__5_0 으로, <>c 를 __c 로 바꿔 노출한다. 둘 다 받는다.
        private static readonly Regex LambdaNamePattern = new Regex(
            @"^(?:<|_)(?<owner>[A-Za-z0-9_]+?)(?:>|_)b__(?<ordinal>\d+)(?:_(?<index>\d+))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static string StripGenericArity(string typeName)
        {
            int tick = typeName.IndexOf('`');
            return tick < 0 ? typeName : typeName.Substring(0, tick);
        }

        internal static string GetShortTypeKey(string fullName)
        {
            int nestedIndex = fullName.IndexOf('+');
            string outer = nestedIndex < 0 ? fullName : fullName.Substring(0, nestedIndex);
            string nested = nestedIndex < 0 ? string.Empty : fullName.Substring(nestedIndex);
            int lastDot = outer.LastIndexOf('.');
            return (lastDot < 0 ? outer : outer.Substring(lastDot + 1)) + nested;
        }

        internal static bool TryParseLambdaName(string methodName, out string owner)
        {
            Match match = LambdaNamePattern.Match(methodName);
            owner = match.Success ? match.Groups["owner"].Value : string.Empty;
            return match.Success;
        }

        internal static bool IsCompilerGeneratedClosureType(Type type)
        {
            string name = type.Name;
            return name.StartsWith("__c", StringComparison.Ordinal)
                || name.StartsWith("<>c", StringComparison.Ordinal)
                || name.IndexOf("DisplayClass", StringComparison.Ordinal) >= 0;
        }

        // -1 = 불일치. 파라미터마다 타입 이름이 정확히 같으면(제네릭 arity `1 제외) 2점, 포함만 하면 1점.
        // exactTypeNames면 포함 일치를 허용하지 않는다("Pattern"이 "PatternLoader"에 걸리지 않게).
        internal static int ScoreSignature(MethodInfo method, int expectedParamCount, string[] expectedTypeNames, bool exactTypeNames)
        {
            ParameterInfo[] parameters;
            try
            {
                parameters = method.GetParameters();
            }
            catch
            {
                return -1;
            }

            if (parameters.Length != expectedParamCount)
            {
                return -1;
            }

            int score = 0;
            for (int i = 0; i < expectedTypeNames.Length && i < parameters.Length; i++)
            {
                string required = expectedTypeNames[i];
                string actual = StripGenericArity(parameters[i].ParameterType.Name);
                if (string.Equals(actual, required, StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
                else if (!exactTypeNames && actual.IndexOf(required, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 1;
                }
                else
                {
                    return -1;
                }
            }

            return score;
        }

        internal static MethodInfo? PickBestBySignature(
            IEnumerable<MethodInfo> candidates,
            int expectedParamCount,
            string[] expectedTypeNames,
            bool exactTypeNames)
        {
            MethodInfo? best = null;
            int bestScore = -1;
            foreach (MethodInfo method in candidates)
            {
                int score = ScoreSignature(method, expectedParamCount, expectedTypeNames, exactTypeNames);
                if (score > bestScore)
                {
                    best = method;
                    bestScore = score;
                }
            }

            return best;
        }

        // 여러 후보면 바깥 타입에 직접 선언된(=this 캡처, 현재 원본과 같은 형태) 쪽이 유일할 때 그것을 고른다.
        internal static MethodInfo? ChooseUniqueLambda(List<MethodInfo> candidates, Type ownerType)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                MethodInfo[] onOwner = candidates.Where(m => m.DeclaringType == ownerType).ToArray();
                if (onOwner.Length == 1)
                {
                    return onOwner[0];
                }
            }

            return null;
        }

        internal static (MethodInfo? Chosen, List<MethodInfo> AmbiguousCandidates) ResolveLambdaMethod(
            Type ownerType,
            string lambdaOwner,
            int expectedParamCount,
            string[] expectedTypeNames)
        {
            var scopes = new List<Type> { ownerType };
            try
            {
                scopes.AddRange(ownerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(IsCompilerGeneratedClosureType));
            }
            catch
            {
            }

            var sameOwner = new List<MethodInfo>();
            var anyOwner = new List<MethodInfo>();
            foreach (Type scope in scopes)
            {
                MethodInfo[] declared;
                try
                {
                    declared = scope.GetMethods(AllDeclaredMethods);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in declared)
                {
                    if (!TryParseLambdaName(method.Name, out string owner)
                        || ScoreSignature(method, expectedParamCount, expectedTypeNames, exactTypeNames: true) < 0)
                    {
                        continue;
                    }

                    (string.Equals(owner, lambdaOwner, StringComparison.Ordinal) ? sameOwner : anyOwner).Add(method);
                }
            }

            MethodInfo? chosen = ChooseUniqueLambda(sameOwner, ownerType)
                ?? (sameOwner.Count == 0 ? ChooseUniqueLambda(anyOwner, ownerType) : null);

            List<MethodInfo> all = sameOwner.Count > 0 ? sameOwner : anyOwner;
            return (chosen, all);
        }

        internal static string DescribeMethod(MethodInfo method)
        {
            string parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            return $"{method.DeclaringType?.FullName}.{method.Name}({parameters})";
        }

        internal static (Type? NestedType, bool UsedFallback) ResolveNestedType(
            Type outerType,
            string exactName,
            string fallbackFragment)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
            Type? exact = outerType.GetNestedType(exactName, flags);
            if (exact is not null)
            {
                return (exact, false);
            }

            if (string.IsNullOrEmpty(fallbackFragment))
            {
                return (null, false);
            }

            var matches = new List<Type>();
            foreach (Type candidate in outerType.GetNestedTypes(flags))
            {
                if (candidate.Name.IndexOf(fallbackFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matches.Add(candidate);
                }
            }

            return matches.Count == 1 ? (matches[0], true) : (null, false);
        }

        internal static bool MatchesTypeShortName(Type type, string expectedShortName, bool expectByRef)
        {
            if (type.IsByRef != expectByRef)
            {
                return false;
            }

            Type unwrapped = type.IsByRef ? (type.GetElementType() ?? type) : type;
            return string.Equals(StripGenericArity(unwrapped.Name), expectedShortName, StringComparison.OrdinalIgnoreCase);
        }

        internal static (MethodInfo? Method, string? FallbackReason) ResolveMethodByShortSignature(
            Type targetType,
            BindingFlags flags,
            string[] exactNames,
            string preferredNameFragment,
            string expectedReturnTypeName,
            (string TypeName, bool IsByRef)[] expectedParams)
        {
            var signatureMatches = new List<MethodInfo>();
            var fragmentMatches = new List<MethodInfo>();

            foreach (MethodInfo method in targetType.GetMethods(flags))
            {
                if (method.DeclaringType != targetType || method.IsGenericMethod)
                {
                    continue;
                }

                if (!MatchesTypeShortName(method.ReturnType, expectedReturnTypeName, expectByRef: false))
                {
                    continue;
                }

                ParameterInfo[] parameters;
                try
                {
                    parameters = method.GetParameters();
                }
                catch
                {
                    continue;
                }

                if (parameters.Length != expectedParams.Length)
                {
                    continue;
                }

                bool paramsMatch = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (!MatchesTypeShortName(parameters[i].ParameterType, expectedParams[i].TypeName, expectedParams[i].IsByRef))
                    {
                        paramsMatch = false;
                        break;
                    }
                }

                if (!paramsMatch)
                {
                    continue;
                }

                for (int i = 0; i < exactNames.Length; i++)
                {
                    if (string.Equals(method.Name, exactNames[i], StringComparison.Ordinal))
                    {
                        string? reason = i > 0 ? $"대체 이름({method.Name})" : null;
                        return (method, reason);
                    }
                }

                signatureMatches.Add(method);
                if (!string.IsNullOrEmpty(preferredNameFragment)
                    && method.Name.IndexOf(preferredNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fragmentMatches.Add(method);
                }
            }

            if (fragmentMatches.Count == 1)
            {
                return (fragmentMatches[0], "이름 조각+시그니처 유일");
            }

            if (signatureMatches.Count == 1)
            {
                return (signatureMatches[0], "시그니처 유일");
            }

            return (null, null);
        }
    }
}
