using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // ─────────────────────────────────────────────────────────────────────────────
        // 패치 대상 목록
        //
        // 여기 있는 항목은 모두 HookPrefix/HookPostfix(HookInvocationSupport.cs)로 들어가고, 디스패치는
        // 런타임 FullName이 아니라 PatchSpec의 "논리 이름"(TypeName.MethodName)으로 한다.
        // 그래서 게임 업데이트로 타입 네임스페이스나 람다 이름이 바뀌어 대체 대상으로 해석돼도
        // 핸들러는 그대로 연결된다(RegisterResolvedTarget 참고).
        //
        // 핸들러가 없는 로그 전용 훅은 두지 않는다. Il2Cpp Harmony 훅은 호출마다 트램펄린 + 인자 박싱이
        // 붙으므로, 아무 일도 하지 않는 훅도 그 자체가 비용이다(2026-11-14 정리: TrackSelectLogic.*,
        // TrackSelector.OpenStandby/ChangeTrackCursor, SoundPlayer.PlaySFX×4, TrackLoader.LoadTracksAsync 제거).
        // ─────────────────────────────────────────────────────────────────────────────

        private const string PatternLoaderTypeName = "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader";
        private const string PatternLoaderLambdaName = "_Load_b__5_0";

        private static IReadOnlyList<PatchSpec> GetPlayInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "Play", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "PlayStart", 1, "IPlayHandler"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer", "Load", 2, "TravelArgs", "Action"),
                new PatchSpec(PatternLoaderTypeName, "Load", 1, "TravelArgs"),
                // 컴파일러 생성 람다. 이름이 바뀌어도 ResolveLambdaMethod가 같은 소유 메서드(Load)의
                // 람다 중 시그니처가 정확히 (Pattern)인 것을 찾아 준다. 그래도 못 찾으면
                // INNER_PatternLoader.Load 훅이 onCompleted 델리게이트를 감싸는 2차 경로(Dual-Wrapper)가 받는다.
                new PatchSpec(PatternLoaderTypeName, PatternLoaderLambdaName, 1, "Pattern"),
                // BGMPlayChecker / Play는 매 프레임 호출되므로 성능상 후킹하지 않는다.

                new PatchSpec("Il2CppStargazer.Play.Widgets.CurrentTrackViewer", "Listen", 1),
            };
        }

        private static IReadOnlyList<PatchSpec> GetTrackLoaderInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPattern", 2, "ELevels", "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadBGMClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPreviewClip", 1, "Action"),
            };
        }

        private static IReadOnlyList<PatchSpec> GetSoundInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "PlayBGM", 2, "AudioClip", "ESoundType"),
                new PatchSpec("Il2CppStarlike.Sound.SoundPlayer", "StopBGM", 0),
                // PlaySFX(4개 오버로드)는 걸지 않는다. 노트 타격음마다 불리는 고빈도 지점인데
                // 하던 일은 "어느 오버로드가 쓰이는지" 1회 진단 로그뿐이었다(진단은 2026-08-09에 끝남).
            };
        }

        private static IReadOnlyList<PatchSpec> GetTrackSelectorInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackSelector", "Set", 1, "List"),
                new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackListViewer", "MoveCursor", 1, "Int32"),
            };
        }

        private static IReadOnlyList<PatchSpec> GetTravelInvocationPatchSpecs()
        {
            return new[]
            {
                new PatchSpec("Il2CppStargazer.Travel.Result.PlayInfoViewer", "ShowPlayInfo", 1, "ITravelResultData"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchTrackRecord", 1, "ITrackRecord"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchJacektImage", 1, "Sprite"),
                // 난이도 표시 덮어쓰기용으로 등록했지만, 실측상 SetTrack은 호출되지 않는다(2026-08-08).
                // 커스텀 트랙 판별은 FocusedTrackViewer 쪽(ApplyCustomLevelDisplayToFocusedViewer)이 맡는다.
                // Refresh는 패치하지 않는다: 호출 빈도가 매우 높고, 패치하면 LevelSelector::Refresh에서
                // NullReferenceException이 계속 발생했다(2026-08-08). 재적용은 FetchTrackRecord/
                // FetchJacektImage 훅에서 처리한다.
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "SetTrack", 1, "ITrackData"),
            };
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 타입 해석 (캐시 + 네임스페이스 변경 대비 폴백)
        // ─────────────────────────────────────────────────────────────────────────────

        private static readonly object TypeLookupLock = new object();
        private static readonly Dictionary<string, Type> TypeLookupCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static Dictionary<string, List<Type>>? _shortTypeNameIndex;

        private static readonly string[] NonGameAssemblyPrefixes =
        {
            "System", "Microsoft", "mscorlib", "netstandard", "MelonLoader", "0Harmony", "Harmony",
            "Il2CppInterop", "Il2Cppmscorlib", "Il2CppSystem", "Mono.", "UnityEngine", "Unity.",
            "AssetRipper", "Iced", "Tomlet", "WebSocket", "bHaptics", "STARGAZER custom chart",
        };

        // 정확한 FullName으로 먼저 찾고, 없으면 "외부타입+중첩타입" 꼴의 짧은 이름이 게임 어셈블리 안에서
        // 유일할 때만 그 타입을 쓴다(인터롭 재생성으로 네임스페이스 접두사가 바뀌는 업데이트 대비).
        // 찾은 결과만 캐시한다 — 못 찾은 이름을 캐시하면 늦게 로드되는 어셈블리를 영영 못 본다.
        private static Type? ResolveGameType(string fullName)
        {
            lock (TypeLookupLock)
            {
                if (TypeLookupCache.TryGetValue(fullName, out Type? cached))
                {
                    return cached;
                }
            }

            Type? found = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                try
                {
                    found = assembly.GetType(fullName, false);
                }
                catch
                {
                    found = null;
                }

                if (found is not null)
                {
                    break;
                }
            }

            if (found is null)
            {
                found = ResolveGameTypeByShortName(fullName);
            }

            if (found is not null)
            {
                lock (TypeLookupLock)
                {
                    TypeLookupCache[fullName] = found;
                }
            }

            return found;
        }

        private static string GetShortTypeKey(string fullName)
        {
            int nestedIndex = fullName.IndexOf('+');
            string outer = nestedIndex < 0 ? fullName : fullName.Substring(0, nestedIndex);
            string nested = nestedIndex < 0 ? string.Empty : fullName.Substring(nestedIndex);
            int lastDot = outer.LastIndexOf('.');
            return (lastDot < 0 ? outer : outer.Substring(lastDot + 1)) + nested;
        }

        private static Type? ResolveGameTypeByShortName(string fullName)
        {
            string shortKey = GetShortTypeKey(fullName);
            Dictionary<string, List<Type>> index;
            lock (TypeLookupLock)
            {
                index = _shortTypeNameIndex ??= BuildShortTypeNameIndex();
            }

            if (!index.TryGetValue(shortKey, out List<Type>? matches) || matches.Count == 0)
            {
                return null;
            }

            if (matches.Count > 1)
            {
                if (LogOnce($"type.ambiguous.{fullName}"))
                {
                    MelonLogger.Warning($"[HookPatch][resolve] {fullName} 정확한 이름이 없고, 짧은 이름 '{shortKey}' 후보가 {matches.Count}개라 모호해서 쓰지 않습니다: {string.Join(", ", matches.Select(t => t.FullName))}");
                }

                return null;
            }

            MelonLogger.Warning($"[HookPatch][resolve] {fullName} → {matches[0].FullName} (네임스페이스 변경으로 판단해 짧은 이름으로 대체 해석)");
            return matches[0];
        }

        private static Dictionary<string, List<Type>> BuildShortTypeNameIndex()
        {
            var index = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || IsNonGameAssembly(assembly))
                {
                    continue;
                }

                Type?[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                foreach (Type? type in types)
                {
                    if (type?.FullName is null)
                    {
                        continue;
                    }

                    string key = GetShortTypeKey(type.FullName);
                    if (!index.TryGetValue(key, out List<Type>? list))
                    {
                        list = new List<Type>(1);
                        index[key] = list;
                    }

                    list.Add(type);
                }
            }

            return index;
        }

        private static bool IsNonGameAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;
            foreach (string prefix in NonGameAssemblyPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 메서드 해석
        //
        // 1단계: 이름 정확히 일치 + 파라미터 시그니처 점수 최고.
        // 2단계: 컴파일러 생성 람다(_Owner_b__N_M / <Owner>b__N_M)면 ResolveLambdaMethod.
        // 3단계: 이름 일치 + 파라미터 개수만 맞는 후보가 "유일"할 때만 경고와 함께 사용.
        //
        // 예전 구현은 람다 이름이 없으면 "b__ 또는 Load를 포함한 아무 메서드"로 넓힌 뒤, 그래도 시그니처가
        // 안 맞으면 파라미터 개수만 같은 첫 후보를 돌려줬다. INNER_PatternLoader에서는 그 첫 후보가
        // Load(TravelArgs)라, 람다 이름만 바뀌어도 Load가 람다 자리에 이중으로 걸릴 수 있었다.
        // ─────────────────────────────────────────────────────────────────────────────

        private const BindingFlags AllDeclaredMethods = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Il2CppInterop은 <Load>b__5_0 을 _Load_b__5_0 으로, <>c 를 __c 로 바꿔 노출한다. 둘 다 받는다.
        private static readonly Regex LambdaNamePattern = new Regex(@"^(?:<|_)(?<owner>[A-Za-z0-9_]+?)(?:>|_)b__(?<ordinal>\d+)(?:_(?<index>\d+))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static MethodInfo? ResolveTargetMethod(PatchSpec spec)
        {
            Type? type = ResolveGameType(spec.TypeName);
            if (type is null)
            {
                return null;
            }

            MethodInfo[] allMethods = GetAllMethods(type);

            MethodInfo? exact = PickBestBySignature(allMethods.Where(m => string.Equals(m.Name, spec.MethodName, StringComparison.Ordinal)), spec, exactTypeNames: false);
            if (exact is not null)
            {
                return exact;
            }

            if (TryParseLambdaName(spec.MethodName, out string lambdaOwner))
            {
                return ResolveLambdaMethod(type, spec, lambdaOwner);
            }

            MethodInfo[] sameNameSameArity = allMethods
                .Where(m => string.Equals(m.Name, spec.MethodName, StringComparison.Ordinal) && m.GetParameters().Length == spec.ParameterCount)
                .ToArray();
            if (sameNameSameArity.Length == 1)
            {
                MelonLogger.Warning($"[HookPatch][resolve] {spec.TypeName}.{spec.MethodName} 파라미터 타입이 기대와 달라 개수만 맞는 유일 후보로 대체합니다: {DescribeMethod(sameNameSameArity[0])}");
                return sameNameSameArity[0];
            }

            return null;
        }

        private static MethodInfo[] GetAllMethods(Type type)
        {
            try
            {
                return type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch
            {
                return Array.Empty<MethodInfo>();
            }
        }

        private static bool TryParseLambdaName(string methodName, out string owner)
        {
            Match match = LambdaNamePattern.Match(methodName);
            owner = match.Success ? match.Groups["owner"].Value : string.Empty;
            return match.Success;
        }

        // 람다는 캡처 방식에 따라 사는 곳이 달라진다: this를 캡처하면 바깥 타입(지금의 INNER_PatternLoader),
        // 아무것도 캡처하지 않으면 중첩 __c, 지역 변수를 캡처하면 __c__DisplayClassN_M.
        // 컴파일러/게임 업데이트로 위치와 번호가 바뀔 수 있으므로 셋 다 본다. 단, 같은 이름의 람다가
        // 다른 메서드에도 있을 수 있으므로(INNER_TableLoader.__c._Load_b__5_0(KVData,string) 실재)
        // 바깥 타입과 그 중첩 타입으로만 범위를 제한하고, 파라미터 타입명은 정확히 일치해야 한다.
        private static MethodInfo? ResolveLambdaMethod(Type ownerType, PatchSpec spec, string lambdaOwner)
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
                    if (!TryParseLambdaName(method.Name, out string owner) || ScoreSignature(method, spec, exactTypeNames: true) < 0)
                    {
                        continue;
                    }

                    (string.Equals(owner, lambdaOwner, StringComparison.Ordinal) ? sameOwner : anyOwner).Add(method);
                }
            }

            MethodInfo? chosen = ChooseUniqueLambda(sameOwner, ownerType) ?? (sameOwner.Count == 0 ? ChooseUniqueLambda(anyOwner, ownerType) : null);
            if (chosen is not null)
            {
                MelonLogger.Warning($"[HookPatch][resolve] 람다 {spec.TypeName}.{spec.MethodName} 이름이 바뀐 것으로 보고 {DescribeMethod(chosen)} 로 대체합니다.");
                return chosen;
            }

            List<MethodInfo> all = sameOwner.Count > 0 ? sameOwner : anyOwner;
            if (all.Count > 1)
            {
                MelonLogger.Warning($"[HookPatch][resolve] 람다 {spec.TypeName}.{spec.MethodName} 후보가 {all.Count}개라 모호해서 걸지 않습니다(2차 Dual-Wrapper 경로가 대신 받습니다): {string.Join(" | ", all.Select(DescribeMethod))}");
            }

            return null;
        }

        // 여러 후보면 바깥 타입에 직접 선언된(=this 캡처, 현재 원본과 같은 형태) 쪽이 유일할 때 그것을 고른다.
        private static MethodInfo? ChooseUniqueLambda(List<MethodInfo> candidates, Type ownerType)
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

        private static bool IsCompilerGeneratedClosureType(Type type)
        {
            string name = type.Name;
            return name.StartsWith("__c", StringComparison.Ordinal)
                || name.StartsWith("<>c", StringComparison.Ordinal)
                || name.IndexOf("DisplayClass", StringComparison.Ordinal) >= 0;
        }

        private static MethodInfo? PickBestBySignature(IEnumerable<MethodInfo> candidates, PatchSpec spec, bool exactTypeNames)
        {
            MethodInfo? best = null;
            int bestScore = -1;
            foreach (MethodInfo method in candidates)
            {
                int score = ScoreSignature(method, spec, exactTypeNames);
                if (score > bestScore)
                {
                    best = method;
                    bestScore = score;
                }
            }

            return best;
        }

        // -1 = 불일치. 파라미터마다 타입 이름이 정확히 같으면(제네릭 arity `1 제외) 2점, 포함만 하면 1점.
        // exactTypeNames면 포함 일치를 허용하지 않는다("Pattern"이 "PatternLoader"에 걸리지 않게).
        private static int ScoreSignature(MethodInfo method, PatchSpec spec, bool exactTypeNames)
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

            if (parameters.Length != spec.ParameterCount)
            {
                return -1;
            }

            int score = 0;
            for (int i = 0; i < spec.ParameterTypeNameContains.Length && i < parameters.Length; i++)
            {
                string required = spec.ParameterTypeNameContains[i];
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

        private static string StripGenericArity(string typeName)
        {
            int tick = typeName.IndexOf('`');
            return tick < 0 ? typeName : typeName.Substring(0, tick);
        }

        private static string DescribeMethod(MethodInfo method)
        {
            string parameters;
            try
            {
                parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
            }
            catch
            {
                parameters = "?";
            }

            return $"{method.DeclaringType?.FullName}.{method.Name}({parameters})";
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Prepare 공용 진입점 + 논리 이름 등록
        // ─────────────────────────────────────────────────────────────────────────────

        // 실제로 패치된 MethodBase -> PatchSpec 논리 이름(TypeName.MethodName).
        // HookPrefix/HookPostfix는 이 이름으로 핸들러를 고르므로, 대체 해석된 대상도 원래 역할을 유지한다.
        private static readonly Dictionary<MethodBase, string> ResolvedTargetLogicalNames = new Dictionary<MethodBase, string>();
        private static readonly object ResolvedTargetLock = new object();

        private static void RegisterResolvedTarget(MethodBase method, PatchSpec spec)
        {
            lock (ResolvedTargetLock)
            {
                ResolvedTargetLogicalNames[method] = spec.LogicalName;
            }
        }

        private static string? TryGetResolvedTargetLogicalName(MethodBase method)
        {
            lock (ResolvedTargetLock)
            {
                return ResolvedTargetLogicalNames.TryGetValue(method, out string? name) ? name : null;
            }
        }

        // 패치 클래스의 Prepare()에서 부른다. 대상을 하나도 못 찾으면 false를 돌려줘 그 클래스만 건너뛴다.
        //
        // TargetMethods()가 빈 목록을 돌려주는 것으로는 막을 수 없다. Harmony는 그때 어트리뷰트에서
        // 대상을 찾으려다 "Undefined target method"로 던지고(NoteMotionPatches 주석 참고), PatchAll이
        // 그 자리에서 멈춰 나머지 훅까지 전부 안 걸린다(모드 전체 미기동). 게임 업데이트로 로그용 훅
        // 하나만 사라져도 그렇게 되므로, 모든 패치 클래스가 이 함수를 거친다.
        // Prepare()는 클래스 단위로 한 번, 대상마다 한 번씩 더 불리므로 결과를 resolved에 담아 재사용한다.
        private static bool PrepareTargets(List<MethodBase> resolved, IEnumerable<PatchSpec> specs)
        {
            if (resolved.Count > 0)
            {
                return true;
            }

            foreach (PatchSpec spec in specs)
            {
                MethodInfo? target = ResolveTargetMethod(spec);
                if (target is null)
                {
                    MelonLogger.Warning($"[HookPatch][attr] target not found: {spec.LogicalName}");
                    continue;
                }

                if (resolved.Contains(target))
                {
                    MelonLogger.Warning($"[HookPatch][attr] {spec.LogicalName} 가 이미 등록된 대상 {DescribeMethod(target)} 로 해석돼 중복 등록을 건너뜁니다.");
                    continue;
                }

                MelonLogger.Msg($"[HookPatch][attr] target: {target.DeclaringType?.FullName}.{target.Name}");
                RegisterResolvedTarget(target, spec);
                resolved.Add(target);
            }

            return resolved.Count > 0;
        }

        private sealed class PatchSpec
        {
            public PatchSpec(string typeName, string methodName, int parameterCount, params string[] parameterTypeNameContains)
            {
                TypeName = typeName;
                MethodName = methodName;
                ParameterCount = parameterCount;
                ParameterTypeNameContains = parameterTypeNameContains;
                LogicalName = $"{typeName}.{methodName}";
            }

            public string TypeName { get; }
            public string MethodName { get; }
            public int ParameterCount { get; }
            public string[] ParameterTypeNameContains { get; }
            public string LogicalName { get; }
        }
    }
}
