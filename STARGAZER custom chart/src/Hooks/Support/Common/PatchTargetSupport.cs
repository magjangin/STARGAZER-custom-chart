using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // ─────────────────────────────────────────────────────────────────────────────
        // 패치 대상 목록
        //
        // InvocationPatchSpecs 항목은 Phase에 따라 HookPrefix 또는 HookPostfix(HookInvocationSupport.cs) 한쪽으로만 들어가고, 디스패치는
        // 런타임 FullName이 아니라 PatchSpec의 "논리 이름"(TypeName.MethodName)으로 한다.
        // 그래서 게임 업데이트로 타입 네임스페이스나 람다 이름이 바뀌어 대체 대상으로 해석돼도
        // 핸들러는 그대로 연결된다(RegisterResolvedTarget 참고).
        //
        // 핸들러가 없는 로그 전용 훅은 두지 않는다. Il2Cpp Harmony 훅은 호출마다 트램펄린 + 인자 박싱이
        // 붙으므로, 아무 일도 하지 않는 훅도 그 자체가 비용이다.
        // ─────────────────────────────────────────────────────────────────────────────

        private const string PatternLoaderTypeName = "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader";
        private const string PatternLoaderLambdaName = "_Load_b__5_0";

        private enum HookPhase
        {
            Prefix,
            Postfix,
        }

        // 각 항목은 핸들러가 실제로 필요한 쪽(Prefix 또는 Postfix) 하나에만 걸린다(InvocationPatches.cs).
        private static readonly PatchSpec[] InvocationPatchSpecs =
        {
            // ── Prefix ──
            // 곡 시작: 커스텀 플레이 상태 확정 + 오토플레이 강제(원본 Play 전에 해야 함).
            new PatchSpec("Il2CppStargazer.Play.PlayerBase", "Play", 1, "TravelArgs")
                .WithAlternates("StartPlay", "BeginPlay")
                .InPhase(HookPhase.Prefix),
            // 2차 경로(Dual-Wrapper): 원본 Load가 onCompleted를 쓰기 전에 래퍼로 감싼다.
            new PatchSpec(PatternLoaderTypeName, "Load", 1, "TravelArgs")
                .WithAlternates("LoadPattern", "BeginLoad")
                .InPhase(HookPhase.Prefix),

            // ── Postfix ──
            // 컴파일러 생성 람다. 이름이 바뀌어도 ResolveLambdaMethod가 같은 소유 메서드(Load)의
            // 람다 중 시그니처가 정확히 (Pattern)인 것을 찾아 준다. 그래도 못 찾으면
            // INNER_PatternLoader.Load 훅이 onCompleted 델리게이트를 감싸는 2차 경로(Dual-Wrapper)가 받는다.
            // BGMPlayChecker / Play는 매 프레임 호출되므로 성능상 후킹하지 않는다.
            new PatchSpec(PatternLoaderTypeName, PatternLoaderLambdaName, 1, "Pattern").InPhase(HookPhase.Postfix),
            new PatchSpec("Il2CppStargazer.Play.Widgets.CurrentTrackViewer", "Listen", 1)
                .WithAlternates("Initialize", "Setup", "Bind", "SetTrack")
                .InPhase(HookPhase.Postfix),
            new PatchSpec("Il2CppStargazer.Travel.TrackSelector.TrackSelector", "Set", 1, "List")
                .WithAlternates("SetTracks", "SetTrackList", "Bind")
                .InPhase(HookPhase.Postfix),
            new PatchSpec("Il2CppStargazer.Travel.Result.PlayInfoViewer", "ShowPlayInfo", 1, "ITravelResultData")
                .WithAlternates("SetPlayInfo", "RefreshPlayInfo", "Bind")
                .InPhase(HookPhase.Postfix),
            // Refresh는 패치하지 않는다: 호출 빈도가 매우 높고, 패치하면 LevelSelector::Refresh에서
            // NullReferenceException이 계속 발생했다(2026-08-08). 재적용은 이 두 훅에서 처리한다.
            // LevelSelector.SetTrack은 실측상 호출되지 않아(2026-08-08) 훅에서 뺐다 — 커스텀 트랙 판별은
            // FocusedTrackViewer 쪽(ApplyCustomLevelDisplayToFocusedViewer)이 맡는다.
            new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchTrackRecord", 1, "ITrackRecord")
                .WithAlternates("SetTrackRecord", "UpdateTrackRecord", "RefreshTrackRecord")
                .InPhase(HookPhase.Postfix),
            // 게임 원본 메서드의 오타(FetchJacektImage)가 향후 업데이트에서 FetchJacketImage로 수정돼도
            // 같은 논리 이름(HookKind.LevelSelectorFetchJacektImage)으로 연결되도록 대체 이름을 둔다.
            new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchJacektImage", 1, "Sprite")
                .WithAlternates("FetchJacketImage", "SetJacketImage", "UpdateJacketImage")
                .InPhase(HookPhase.Postfix),
        };

        private static IEnumerable<PatchSpec> GetInvocationPatchSpecs(HookPhase phase)
        {
            return InvocationPatchSpecs.Where(spec => spec.Phase == phase);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 타입 해석 (캐시 + 네임스페이스 변경 대비 폴백)
        // ─────────────────────────────────────────────────────────────────────────────

        private static readonly object TypeLookupLock = new object();
        private static readonly Dictionary<string, Type> TypeLookupCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static Dictionary<string, List<Type>>? _shortTypeNameIndex;
        private static int _shortTypeNameIndexAssemblyCount = -1;

        private static readonly string[] NonGameAssemblyPrefixes =
        {
            "System", "Microsoft", "mscorlib", "netstandard", "MelonLoader", "0Harmony", "Harmony",
            "Il2CppInterop", "Il2Cppmscorlib", "Il2CppSystem", "Mono.", "UnityEngine", "Unity.",
            "AssetRipper", "Iced", "Tomlet", "WebSocket", "bHaptics", "STARGAZER custom chart",
        };

        // 정확한 FullName으로 먼저 찾고, 없으면 "외부타입+중첩타입" 꼴의 짧은 이름이 게임 어셈블리 안에서
        // 유일할 때만 그 타입을 쓴다(인터롭 재생성으로 네임스페이스 접두사가 바뀌는 업데이트 대비).
        // 찾은 결과만 캐시한다 — 못 찾은 이름을 캐시하면 늦게 로드되는 어셈블리를 영영 못 본다.
        internal static Type? ResolveGameType(string fullName)
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

        private static string GetShortTypeKey(string fullName) => PatchResolutionLogic.GetShortTypeKey(fullName);

        private static Type? ResolveGameTypeByShortName(string fullName)
        {
            string shortKey = GetShortTypeKey(fullName);
            Dictionary<string, List<Type>> index = GetGameTypeIndex();

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

        // 게임 어셈블리 타입의 짧은 이름 색인. 로드된 어셈블리 수가 바뀌면(지연 로드) 다시 만든다.
        // 반환된 사전은 교체만 되고 수정되지 않으므로 락 밖에서 읽어도 안전하다.
        private static Dictionary<string, List<Type>> GetGameTypeIndex()
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            lock (TypeLookupLock)
            {
                if (_shortTypeNameIndex is null || _shortTypeNameIndexAssemblyCount != loadedAssemblies.Length)
                {
                    _shortTypeNameIndex = BuildShortTypeNameIndex(loadedAssemblies);
                    _shortTypeNameIndexAssemblyCount = loadedAssemblies.Length;
                }

                return _shortTypeNameIndex;
            }
        }

        private static Dictionary<string, List<Type>> BuildShortTypeNameIndex(Assembly[] assemblies)
        {
            var index = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (Assembly assembly in assemblies)
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

        private static MethodInfo? ResolveTargetMethod(PatchSpec spec)
        {
            MethodInfo? resolved = ResolveTargetMethodInType(spec);
            if (resolved is null && spec.AllowTypeScanFallback)
            {
                resolved = ResolveTargetMethodByTypeScan(spec);
            }

            return resolved;
        }

        // 4단계(옵트인, PatchSpec.WithTypeScanFallback): 대상 타입 이름이 통째로 바뀐 경우.
        // 게임 어셈블리 전체에서 이름(대체 이름 포함) + 파라미터 타입명 정확 일치 + IL2CPP 구체 클래스인 메서드가
        // 유일할 때만 쓴다. 타입 전체를 훑으므로 해석 실패 시 한 번만 도는 경로이며, 핵심 기능에만 켠다.
        private static MethodInfo? ResolveTargetMethodByTypeScan(PatchSpec spec)
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { spec.MethodName };
            names.UnionWith(spec.AlternateMethodNames);

            IEnumerable<Type> allGameTypes = GetGameTypeIndex().Values.SelectMany(list => list);
            var (chosen, candidates) = PatchResolutionLogic.FindUniqueMethodAcrossTypes(
                allGameTypes,
                names,
                spec.ParameterCount,
                spec.ParameterTypeNameContains,
                IsConcreteIl2CppClass);

            if (chosen is not null)
            {
                MelonLogger.Warning($"[HookPatch][resolve] {spec.LogicalName} 의 타입을 찾지 못해 게임 전체 시그니처 탐색으로 유일 후보 {DescribeMethod(chosen)} 에 연결합니다(타입 개명 의심 — dump로 확인 권장).");
            }
            else if (candidates.Count > 1)
            {
                MelonLogger.Warning($"[HookPatch][resolve] {spec.LogicalName} 시그니처 탐색 후보가 {candidates.Count}개라 모호해서 걸지 않습니다: {string.Join(" | ", candidates.Select(DescribeMethod))}");
            }

            return chosen;
        }

        // Il2CppInterop은 IL2CPP 인터페이스를 일반 클래스로 생성하므로(Type.IsInterface=false) 네이티브 클래스 플래그로 거른다.
        // 인터페이스/추상 메서드는 네이티브 구현이 없어 패치해도 아무 일도 하지 않거나 실패한다.
        private static bool IsConcreteIl2CppClass(Type type)
        {
            if (!typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(type))
            {
                return false;
            }

            Type store = typeof(Il2CppInterop.Runtime.Il2CppClassPointerStore<>).MakeGenericType(type);
            object? raw = store.GetField("NativeClassPtr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? store.GetProperty("NativeClassPtr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (raw is not IntPtr classPtr || classPtr == IntPtr.Zero)
            {
                return false;
            }

            return !Il2CppInterop.Runtime.IL2CPP.il2cpp_class_is_interface(classPtr)
                && !Il2CppInterop.Runtime.IL2CPP.il2cpp_class_is_abstract(classPtr);
        }

        private static MethodInfo? ResolveTargetMethodInType(PatchSpec spec)
        {
            Type? type = ResolveGameType(spec.TypeName);
            if (type is null)
            {
                return null;
            }

            MethodInfo[] allMethods = GetAllMethods(type);

            MethodInfo? exact = PatchResolutionLogic.PickBestBySignature(
                allMethods.Where(m => string.Equals(m.Name, spec.MethodName, StringComparison.Ordinal)),
                spec.ParameterCount,
                spec.ParameterTypeNameContains,
                exactTypeNames: false);
            if (exact is not null)
            {
                return exact;
            }

            foreach (string alternateName in spec.AlternateMethodNames)
            {
                MethodInfo? alternate = PatchResolutionLogic.PickBestBySignature(
                    allMethods.Where(m => string.Equals(m.Name, alternateName, StringComparison.Ordinal)),
                    spec.ParameterCount,
                    spec.ParameterTypeNameContains,
                    exactTypeNames: false);
                if (alternate is not null)
                {
                    MelonLogger.Warning($"[HookPatch][resolve] {spec.TypeName}.{spec.MethodName} 대신 대체 이름 {DescribeMethod(alternate)} 로 연결합니다.");
                    return alternate;
                }
            }

            if (PatchResolutionLogic.TryParseLambdaName(spec.MethodName, out string lambdaOwner))
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

            foreach (string alternateName in spec.AlternateMethodNames)
            {
                MethodInfo[] altSameArity = allMethods
                    .Where(m => string.Equals(m.Name, alternateName, StringComparison.Ordinal) && m.GetParameters().Length == spec.ParameterCount)
                    .ToArray();
                if (altSameArity.Length == 1)
                {
                    MelonLogger.Warning($"[HookPatch][resolve] {spec.TypeName}.{spec.MethodName} 대체 이름({alternateName}) 개수 일치 유일 후보로 연결합니다: {DescribeMethod(altSameArity[0])}");
                    return altSameArity[0];
                }
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

        private static MethodInfo? ResolveLambdaMethod(Type ownerType, PatchSpec spec, string lambdaOwner)
        {
            var (chosen, ambiguous) = PatchResolutionLogic.ResolveLambdaMethod(
                ownerType,
                lambdaOwner,
                spec.ParameterCount,
                spec.ParameterTypeNameContains);

            if (chosen is not null)
            {
                MelonLogger.Warning($"[HookPatch][resolve] 람다 {spec.TypeName}.{spec.MethodName} 이름이 바뀐 것으로 보고 {DescribeMethod(chosen)} 로 대체합니다.");
                return chosen;
            }

            if (ambiguous.Count > 1)
            {
                MelonLogger.Warning($"[HookPatch][resolve] 람다 {spec.TypeName}.{spec.MethodName} 후보가 {ambiguous.Count}개라 모호해서 걸지 않습니다(2차 Dual-Wrapper 경로가 대신 받습니다): {string.Join(" | ", ambiguous.Select(DescribeMethod))}");
            }

            return null;
        }

        private static string DescribeMethod(MethodInfo method) => PatchResolutionLogic.DescribeMethod(method);

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
        private static bool PrepareTargets(List<MethodBase> resolved, IEnumerable<PatchSpec> specs, string feature)
        {
            if (resolved.Count > 0)
            {
                return true;
            }

            PatchSpec[] specList = specs as PatchSpec[] ?? specs.ToArray();
            foreach (PatchSpec spec in specList)
            {
                MethodInfo? target = ResolveTargetMethod(spec);
                if (target is null)
                {
                    MelonLogger.Warning($"[HookPatch][attr] target not found: {spec.LogicalName}");
                    continue;
                }

                // 같은 클래스 안의 중복뿐 아니라, 다른 패치 클래스가 이미 다른 역할(논리 이름)로 등록한 대상도 거른다.
                // (예: 람다 대체 해석이 Load로 잘못 떨어지면 Prefix 클래스의 Load와 Postfix 클래스의 람다가 같은 메서드가 된다.)
                string? existingRole = TryGetResolvedTargetLogicalName(target);
                if (resolved.Contains(target) || (existingRole is not null && !string.Equals(existingRole, spec.LogicalName, StringComparison.Ordinal)))
                {
                    MelonLogger.Warning($"[HookPatch][attr] {spec.LogicalName} 가 이미 등록된 대상 {DescribeMethod(target)}(역할: {existingRole ?? spec.LogicalName}) 로 해석돼 중복 등록을 건너뜁니다.");
                    continue;
                }

                MelonLogger.Msg($"[HookPatch][attr] target: {target.DeclaringType?.FullName}.{target.Name}");
                RegisterResolvedTarget(target, spec);
                resolved.Add(target);
            }

            // 기능별 적용 수를 [Health] 요약에 남긴다(중복으로 건너뛴 항목은 적용으로 세지 않는다).
            HookHealth.Report(feature, resolved.Count, specList.Length);
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
            public string[] AlternateMethodNames { get; private set; } = Array.Empty<string>();
            public string LogicalName { get; }

            // 공용 디스패처 훅(InvocationPatches)에서만 쓴다. 전용 패치 클래스의 항목은 기본값 그대로 둔다.
            public HookPhase Phase { get; private set; } = HookPhase.Postfix;

            public PatchSpec InPhase(HookPhase phase)
            {
                Phase = phase;
                return this;
            }

            public PatchSpec WithAlternates(params string[] alternateMethodNames)
            {
                AlternateMethodNames = alternateMethodNames ?? Array.Empty<string>();
                return this;
            }

            // 타입 이름이 통째로 바뀌었을 때 게임 전체 시그니처 탐색(ResolveTargetMethodByTypeScan)을 허용한다.
            // 파라미터 타입명이 충분히 고유한 핵심 기능에만 켤 것(흔한 시그니처면 엉뚱한 메서드에 걸릴 수 있음).
            public bool AllowTypeScanFallback { get; private set; }

            public PatchSpec WithTypeScanFallback()
            {
                AllowTypeScanFallback = true;
                return this;
            }
        }
    }
}
