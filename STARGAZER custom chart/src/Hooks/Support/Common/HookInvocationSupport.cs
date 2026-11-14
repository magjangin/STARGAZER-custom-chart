using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // ─────────────────────────────────────────────────────────────────────────────
        // 공용 Prefix/Postfix 디스패치
        //
        // MethodBase마다 HookSite를 한 번만 계산해 두고(역할 enum + 로그 자격 + 스로틀 상태),
        // 이후 호출은 사전 조회 1회 + switch로 끝난다.
        // 역할은 PatchSpec 논리 이름으로 정하므로(RegisterResolvedTarget), 업데이트로 대체 해석된
        // 대상(이름이 바뀐 람다, 네임스페이스가 바뀐 타입, 오타 수정 이름)도 같은 핸들러로 연결된다.
        // ─────────────────────────────────────────────────────────────────────────────

        private enum HookKind
        {
            None,
            PlayerBasePlay,
            PatternLoaderLoad,
            PatternLoaderLambda,
            CurrentTrackViewerListen,
            PlayInfoViewerShowPlayInfo,
            TrackSelectorSet,
            LevelSelectorFetchTrackRecord,
            LevelSelectorFetchJacektImage,
            LevelSelectorSetTrack,
        }

        private static readonly Dictionary<string, HookKind> HookKindsByLogicalName = new Dictionary<string, HookKind>(StringComparer.Ordinal)
        {
            ["Il2CppStargazer.Play.PlayerBase.Play"] = HookKind.PlayerBasePlay,
            [PatternLoaderTypeName + ".Load"] = HookKind.PatternLoaderLoad,
            [PatternLoaderTypeName + "." + PatternLoaderLambdaName] = HookKind.PatternLoaderLambda,
            ["Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen"] = HookKind.CurrentTrackViewerListen,
            ["Il2CppStargazer.Travel.Result.PlayInfoViewer.ShowPlayInfo"] = HookKind.PlayInfoViewerShowPlayInfo,
            ["Il2CppStargazer.Travel.TrackSelector.TrackSelector.Set"] = HookKind.TrackSelectorSet,
            ["Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchTrackRecord"] = HookKind.LevelSelectorFetchTrackRecord,
            ["Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchJacektImage"] = HookKind.LevelSelectorFetchJacektImage,
            ["Il2CppStargazer.Travel.LevelSelector.LevelSelector.SetTrack"] = HookKind.LevelSelectorSetTrack,
        };

        private sealed class HookSite
        {
            public HookSite(HookKind kind, string logicalName, bool focused)
            {
                Kind = kind;
                LogicalName = logicalName;
                Focused = focused;
            }

            public HookKind Kind { get; }
            public string LogicalName { get; }
            public bool Focused { get; }
            public long ThrottleWindowStartMs;
            public int ThrottleCount;
        }

        private static readonly Dictionary<MethodBase, HookSite> HookSites = new Dictionary<MethodBase, HookSite>();
        private static readonly object HookSitesLock = new object();

        private static HookSite GetHookSite(MethodBase method)
        {
            lock (HookSitesLock)
            {
                if (HookSites.TryGetValue(method, out HookSite? site))
                {
                    return site;
                }
            }

            string logicalName = TryGetResolvedTargetLogicalName(method) ?? $"{method.DeclaringType?.FullName}.{method.Name}";
            HookKind kind = HookKindsByLogicalName.TryGetValue(logicalName, out HookKind mapped) ? mapped : HookKind.None;
            var created = new HookSite(kind, logicalName, FocusedHarmonyInvocationMethods.Contains(logicalName));

            lock (HookSitesLock)
            {
                if (HookSites.TryGetValue(method, out HookSite? existing))
                {
                    return existing;
                }

                HookSites[method] = created;
                return created;
            }
        }

        private static void HookPrefix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                HookSite site = GetHookSite(__originalMethod);
                object[] args = __args ?? Array.Empty<object>();

                switch (site.Kind)
                {
                    case HookKind.PatternLoaderLoad:
                        TryWrapPatternLoaderOnCompleted(__instance);
                        break;

                    case HookKind.PlayerBasePlay:
                        HandlePlayerBasePlayPrefix(__instance, args);
                        break;
                }

                if (EnableVerboseInvocationLogging && !ShouldSkipInvocationLog(site))
                {
                    MelonLogger.Msg($"[HookInvoke][PRE] {BuildInvocationSignature(__originalMethod, __instance, args)}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[HookInvoke][PRE] logging failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void HandlePlayerBasePlayPrefix(object? playerBase, object[] args)
        {
            UpdateCustomChartPlayState(args.Length > 0 ? args[0] : null);
            ResetNoteMotionState();
            MelonLogger.Msg("[PlayScene] 플레이씬 진입");

            if (EnableForceAutoPlayAtPlayerBasePlay)
            {
                TryEnablePlayerBaseAutoPlay(playerBase);
            }
        }

        private static void HookPostfix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                HookSite site = GetHookSite(__originalMethod);
                object[] args = __args ?? Array.Empty<object>();
                if (EnableVerboseInvocationLogging && !ShouldSkipInvocationLog(site))
                {
                    MelonLogger.Msg($"[HookInvoke][POST] {BuildInvocationSignature(__originalMethod, __instance, args)}");
                }

                switch (site.Kind)
                {
                    case HookKind.CurrentTrackViewerListen:
                        ApplyCustomLevelDisplayToPlayScene(__instance);
                        break;

                    case HookKind.PlayInfoViewerShowPlayInfo:
                        MelonLogger.Msg("[PlayScene] 결과 화면 진입");
                        HandleResultPlayInfoLevelDisplay(__instance, args);
                        break;

                    case HookKind.PatternLoaderLambda:
                        if (IsCustomChartPlayActive)
                        {
                            ProbeNoteArrayMembers(args.Length > 0 ? args[0] : null, PatternLoaderLambdaProbeSource);
                        }
                        break;

                    case HookKind.TrackSelectorSet:
                        if (args.Length > 0 && args[0] is not null)
                        {
                            HandleTrackSelectorSetTracks(args[0]);
                        }
                        break;

                    // Refresh는 패치하면 NRE가 나므로, 게임이 표시를 다시 채운 뒤 불리는 이 두 훅에서 재적용한다.
                    case HookKind.LevelSelectorFetchTrackRecord:
                    case HookKind.LevelSelectorFetchJacektImage:
                        HandleLevelSelectorRefresh(__instance);
                        break;

                    case HookKind.LevelSelectorSetTrack:
                        HandleLevelSelectorSetTrack(__instance, args);
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[HookInvoke][POST] logging failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Type? _autoPlayOwnerType;
        private static PropertyInfo? _autoPlayProp;
        private static FieldInfo? _autoPlayField;
        private static MethodInfo? _autoPlaySetter;

        private static void TryEnablePlayerBaseAutoPlay(object? playerBaseInstance)
        {
            if (playerBaseInstance is null)
            {
                return;
            }

            try
            {
                Type type = playerBaseInstance.GetType();
                if (_autoPlayOwnerType != type)
                {
                    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    PropertyInfo? prop = type.GetProperty("IsAutoPlay", Flags);
                    _autoPlayProp = (prop is not null && prop.CanWrite && prop.PropertyType == typeof(bool)) ? prop : null;

                    FieldInfo? field = null;
                    if (_autoPlayProp is null)
                    {
                        FieldInfo? candidate = type.GetField("IsAutoPlay", Flags) ?? type.GetField("isAutoPlay", Flags);
                        if (candidate is not null && candidate.FieldType == typeof(bool))
                        {
                            field = candidate;
                        }
                    }
                    _autoPlayField = field;

                    _autoPlaySetter = (_autoPlayProp is null && _autoPlayField is null)
                        ? (type.GetMethod("set_IsAutoPlay", Flags, null, new[] { typeof(bool) }, null)
                            ?? type.GetMethod("SetAutoPlay", Flags, null, new[] { typeof(bool) }, null))
                        : null;

                    _autoPlayOwnerType = type;
                }

                bool applied = false;
                if (_autoPlayProp is not null)
                {
                    _autoPlayProp.SetValue(playerBaseInstance, true);
                    applied = true;
                }
                else if (_autoPlayField is not null)
                {
                    _autoPlayField.SetValue(playerBaseInstance, true);
                    applied = true;
                }
                else if (_autoPlaySetter is not null)
                {
                    _autoPlaySetter.Invoke(playerBaseInstance, new object[] { true });
                    applied = true;
                }

                if (EnableRuntimeProbeLogging)
                {
                    object? afterObj = TryGetMemberValue(playerBaseInstance, type, "IsAutoPlay")
                        ?? TryGetMemberValue(playerBaseInstance, type, "isAutoPlay");
                    MelonLogger.Msg($"[PlayerBaseAutoPlay] applied={applied} after={afterObj?.ToString() ?? "<unknown>"}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlayerBaseAutoPlay] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool ShouldSkipInvocationLog(HookSite site)
        {
            if (!EnableVerboseInvocationLogging)
            {
                return true;
            }

            if (EnableFocusedHarmonyInvocationLogging && !site.Focused)
            {
                return true;
            }

            long now = Environment.TickCount64;
            lock (site)
            {
                if (site.ThrottleCount == 0 || now - site.ThrottleWindowStartMs > InvocationLogWindowMs)
                {
                    site.ThrottleWindowStartMs = now;
                    site.ThrottleCount = 1;
                    return false;
                }

                site.ThrottleCount++;
                return site.ThrottleCount > InvocationLogMaxPerWindow;
            }
        }

        private static string BuildInvocationSignature(MethodBase method, object? instance, object[] args)
        {
            var builder = new StringBuilder();
            builder.Append(method.DeclaringType?.FullName ?? "<unknown-type>");
            builder.Append('.');
            builder.Append(method.Name);
            builder.Append(" instance=");
            builder.Append(instance is null ? "<static>" : instance.GetType().FullName ?? instance.GetType().Name);
            builder.Append(" args=[");
            builder.Append(string.Join(", ", args.Select((arg, index) => $"arg{index}={FormatInvocationValue(arg)}")));
            builder.Append(']');
            return builder.ToString();
        }

        private static string FormatInvocationValue(object? value)
        {
            if (value is null)
            {
                return "null";
            }

            Type type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal || type.IsEnum)
            {
                return $"{type.Name}:{value}";
            }

            return type.FullName ?? type.Name;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // 2차 경로: INNER_PatternLoader.onCompleted 이중래퍼(Dual-Wrapper)
        //
        // 1차 경로(람다 _Load_b__5_0 Postfix)는 람다 이름에 의존한다. 이름이 바뀌어 1차 해석이 실패해도
        // 공개 프로퍼티 onCompleted(Action<Pattern>)는 남아 있을 가능성이 높으므로, Load 시점에 그것을
        // 감싸 패턴을 받는다. 둘 다 살아 있으면 람다 안에서 onCompleted(=래퍼)가 먼저 불리고 그 뒤에
        // 람다 Postfix가 불리므로, ProbeNoteArrayMembers가 패턴 포인터로 중복을 거른다.
        // ─────────────────────────────────────────────────────────────────────────────

        private const string PatternLoaderLambdaProbeSource = "PatternLoader.Lambda";
        private const string PatternLoaderDualWrapperProbeSource = "PatternLoader.DualWrapper";

        // PatternLoader 인스턴스 포인터 -> 우리가 onCompleted에 넣은 래퍼 델리게이트 포인터.
        // PlayerBase.Play마다 비운다(UpdateCustomChartPlayState).
        private static readonly Dictionary<IntPtr, IntPtr> WrappedPatternLoaders = new Dictionary<IntPtr, IntPtr>();
        private static PropertyInfo? _patternLoaderOnCompletedProperty;
        private static FieldInfo? _patternLoaderOnCompletedField;
        private static Type? _patternLoaderOnCompletedOwner;

        private static void ResolvePatternLoaderOnCompletedMember(Type loaderType)
        {
            if (_patternLoaderOnCompletedOwner == loaderType)
            {
                return;
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? prop = loaderType.GetProperty("onCompleted", Flags);
            FieldInfo? field = null;

            if (prop is null || !prop.CanRead || !prop.CanWrite)
            {
                prop = null;
                field = loaderType.GetField("onCompleted", Flags);
            }

            // 업데이트로 onCompleted 이름이 바뀐 경우 타입(Action<Pattern>)으로 유일 멤버를 찾는다.
            if (prop is null && field is null)
            {
                Type expectedType = typeof(Il2CppSystem.Action<Il2CppMikazuki.Pattern>);
                var matchingProps = loaderType.GetProperties(Flags)
                    .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !IsCompilerBackingMember(p.Name)
                        && (p.PropertyType == expectedType || MatchesShortTypeName(p.PropertyType, "Action<Pattern>", matchGenericArgs: true)))
                    .ToArray();

                if (matchingProps.Length == 1)
                {
                    prop = matchingProps[0];
                    if (LogOnce($"dualwrapper.resolve.{loaderType.FullName}"))
                    {
                        MelonLogger.Warning($"[HookPatch][resolve] {loaderType.FullName}.onCompleted 대신 타입 일치 프로퍼티 '{prop.Name}' 로 연결합니다.");
                    }
                }
                else
                {
                    var matchingFields = loaderType.GetFields(Flags)
                        .Where(f => !f.IsInitOnly && !IsCompilerBackingMember(f.Name)
                            && (f.FieldType == expectedType || MatchesShortTypeName(f.FieldType, "Action<Pattern>", matchGenericArgs: true)))
                        .ToArray();
                    if (matchingFields.Length == 1)
                    {
                        field = matchingFields[0];
                        if (LogOnce($"dualwrapper.resolve.{loaderType.FullName}"))
                        {
                            MelonLogger.Warning($"[HookPatch][resolve] {loaderType.FullName}.onCompleted 대신 타입 일치 필드 '{field.Name}' 로 연결합니다.");
                        }
                    }
                }
            }

            _patternLoaderOnCompletedProperty = prop;
            _patternLoaderOnCompletedField = field;
            _patternLoaderOnCompletedOwner = loaderType;
        }

        private static void TryWrapPatternLoaderOnCompleted(object? instance)
        {
            if (!IsCustomChartPlayActive || instance is null)
            {
                return;
            }

            try
            {
                Type loaderType = instance.GetType();
                ResolvePatternLoaderOnCompletedMember(loaderType);

                if (_patternLoaderOnCompletedProperty is null && _patternLoaderOnCompletedField is null)
                {
                    if (LogOnce("dualwrapper.no-oncompleted"))
                    {
                        MelonLogger.Warning($"[DualWrapper] {loaderType.FullName}.onCompleted 를 찾지 못해 2차 경로를 설치하지 못했습니다.");
                    }
                    return;
                }

                object? rawAction = _patternLoaderOnCompletedProperty is not null
                    ? _patternLoaderOnCompletedProperty.GetValue(instance)
                    : _patternLoaderOnCompletedField!.GetValue(instance);

                if (rawAction is not Il2CppSystem.Action<Il2CppMikazuki.Pattern> origAction)
                {
                    return;
                }

                IntPtr loaderPtr = TryGetIl2CppPointer(instance);
                if (loaderPtr != IntPtr.Zero
                    && WrappedPatternLoaders.TryGetValue(loaderPtr, out IntPtr wrapperPtr)
                    && wrapperPtr == origAction.Pointer)
                {
                    return;
                }

                Action<Il2CppMikazuki.Pattern> managedDelegate = pattern =>
                {
                    try
                    {
                        if (pattern != null)
                        {
                            ProbeNoteArrayMembers(pattern, PatternLoaderDualWrapperProbeSource);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[DualWrapper] Exception in pattern probe: {ex.Message}");
                    }

                    origAction.Invoke(pattern!);
                };

                Il2CppSystem.Action<Il2CppMikazuki.Pattern> wrapper = managedDelegate;
                if (_patternLoaderOnCompletedProperty is not null)
                {
                    _patternLoaderOnCompletedProperty.SetValue(instance, wrapper);
                }
                else
                {
                    _patternLoaderOnCompletedField!.SetValue(instance, wrapper);
                }

                if (loaderPtr != IntPtr.Zero)
                {
                    WrappedPatternLoaders[loaderPtr] = wrapper.Pointer;
                }

                if (LogOnce("dualwrapper.installed"))
                {
                    MelonLogger.Msg("[DualWrapper] INNER_PatternLoader.onCompleted 이중래퍼(Dual-Wrapper) 설치 완료 (이후 설치는 로그 생략)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[DualWrapper] 이중래퍼 설치 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
