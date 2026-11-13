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
        // 예전에는 호출마다 DeclaringType.FullName/Name 문자열 비교를 십수 번 하고, 로그 스로틀용
        // "타입.메서드" 문자열을 매번 새로 만들었다. 이제 MethodBase마다 HookSite를 한 번만 계산해 두고
        // (역할 enum + 로그 자격 + 스로틀 상태), 이후 호출은 사전 조회 1회 + switch로 끝난다.
        // 역할은 PatchSpec 논리 이름으로 정하므로(RegisterResolvedTarget), 업데이트로 대체 해석된
        // 대상(이름이 바뀐 람다, 네임스페이스가 바뀐 타입)도 같은 핸들러로 연결된다.
        // ─────────────────────────────────────────────────────────────────────────────

        private enum HookKind
        {
            None,
            PlayerBasePlay,
            PatternLoaderLoad,
            PatternLoaderLambda,
            TrackDataLoadPattern,
            TrackDataLoadBgmClip,
            SoundPlayBgm,
            SoundStopBgm,
            CurrentTrackViewerListen,
            PlayInfoViewerShowPlayInfo,
            TrackSelectorSet,
            TrackListViewerMoveCursor,
            LevelSelectorFetchTrackRecord,
            LevelSelectorFetchJacektImage,
            LevelSelectorSetTrack,
        }

        private static readonly Dictionary<string, HookKind> HookKindsByLogicalName = new Dictionary<string, HookKind>(StringComparer.Ordinal)
        {
            ["Il2CppStargazer.Play.PlayerBase.Play"] = HookKind.PlayerBasePlay,
            [PatternLoaderTypeName + ".Load"] = HookKind.PatternLoaderLoad,
            [PatternLoaderTypeName + "." + PatternLoaderLambdaName] = HookKind.PatternLoaderLambda,
            ["Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPattern"] = HookKind.TrackDataLoadPattern,
            ["Il2CppStargazer.TrackLoader+INNER_TrackData.LoadBGMClip"] = HookKind.TrackDataLoadBgmClip,
            ["Il2CppStarlike.Sound.SoundPlayer.PlayBGM"] = HookKind.SoundPlayBgm,
            ["Il2CppStarlike.Sound.SoundPlayer.StopBGM"] = HookKind.SoundStopBgm,
            ["Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen"] = HookKind.CurrentTrackViewerListen,
            ["Il2CppStargazer.Travel.Result.PlayInfoViewer.ShowPlayInfo"] = HookKind.PlayInfoViewerShowPlayInfo,
            ["Il2CppStargazer.Travel.TrackSelector.TrackSelector.Set"] = HookKind.TrackSelectorSet,
            ["Il2CppStargazer.Travel.TrackSelector.TrackListViewer.MoveCursor"] = HookKind.TrackListViewerMoveCursor,
            ["Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchTrackRecord"] = HookKind.LevelSelectorFetchTrackRecord,
            ["Il2CppStargazer.Travel.LevelSelector.LevelSelector.FetchJacektImage"] = HookKind.LevelSelectorFetchJacektImage,
            ["Il2CppStargazer.Travel.LevelSelector.LevelSelector.SetTrack"] = HookKind.LevelSelectorSetTrack,
        };

        private sealed class HookSite
        {
            public HookSite(HookKind kind, string logicalName, bool suppressed, bool focused)
            {
                Kind = kind;
                LogicalName = logicalName;
                Suppressed = suppressed;
                Focused = focused;
            }

            public HookKind Kind { get; }
            public string LogicalName { get; }
            public bool Suppressed { get; }
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
            var created = new HookSite(kind, logicalName, SuppressedInvocationMethods.Contains(logicalName), FocusedHarmonyInvocationMethods.Contains(logicalName));

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
                    case HookKind.SoundPlayBgm:
                    case HookKind.SoundStopBgm:
                    case HookKind.TrackDataLoadBgmClip:
                        LogBgmDebugInvocation(__originalMethod, __instance, args);
                        break;

                    case HookKind.PatternLoaderLoad:
                        TryWrapPatternLoaderOnCompleted(__instance);
                        break;

                    // 난이도 문자열이 패턴 에셋 로딩에 영향을 주는지 추적하기 위한 진단 로그.
                    // LoadPattern 직후 로딩이 멈추는 현상이 난이도 값과만 상관관계를 보였다.
                    case HookKind.TrackDataLoadPattern:
                        LogPatternLoadDiagnostics(__instance, args);
                        break;

                    case HookKind.PlayerBasePlay:
                        HandlePlayerBasePlayPrefix(__instance, args);
                        break;
                }

                if (ShouldSkipInvocationLog(site))
                {
                    return;
                }

                MelonLogger.Msg($"[HookInvoke][PRE] {BuildInvocationSignature(__originalMethod, __instance, args)}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[HookInvoke][PRE] logging failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void HandlePlayerBasePlayPrefix(object? playerBase, object[] args)
        {
            IsInPlayScene = true;
            UpdateCustomChartPlayState(args.Length > 0 ? args[0] : null);
            ResetNoteMotionState();
            MelonLogger.Msg("[PlayScene] 플레이씬 진입 — IsInPlayScene=true");

            if (EnableForceAutoPlayAtPlayerBasePlay)
            {
                TryEnablePlayerBaseAutoPlay(playerBase);
            }

            if (EnableRuntimeProbeLogging || EnablePlaySceneJacketLogging)
            {
                LogPlayerBaseJacketSnapshot(playerBase);
            }

            if (EnablePlaySceneTrackLogging && args.Length > 0 && args[0] != null)
            {
                LogPlaySceneTrackInfo(args[0]);
            }
        }

        private static void HookPostfix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                HookSite site = GetHookSite(__originalMethod);
                object[] args = __args ?? Array.Empty<object>();
                if (!ShouldSkipInvocationLog(site))
                {
                    MelonLogger.Msg($"[HookInvoke][POST] {BuildInvocationSignature(__originalMethod, __instance, args)}");
                }

                switch (site.Kind)
                {
                    case HookKind.CurrentTrackViewerListen:
                        ApplyCustomLevelDisplayToPlayScene(__instance);
                        if (EnableRuntimeProbeLogging || EnablePlaySceneJacketLogging)
                        {
                            ProbeCurrentTrackViewerImageMembers(__instance, args);
                        }
                        break;

                    case HookKind.PlayInfoViewerShowPlayInfo:
                        IsInPlayScene = false;
                        MelonLogger.Msg("[PlayScene] 결과 화면 진입 — IsInPlayScene=false");
                        HandleResultPlayInfoLevelDisplay(__instance, args);
                        if (EnableResultSceneJacketLogging)
                        {
                            ProbeResultPlayInfoJacketMembers(__instance, args);
                        }
                        if (EnableRuntimeProbeLogging)
                        {
                            ProbeNoteArrayMembers(args.Length > 0 ? args[0] : null, "Result.PlayInfoViewer.ShowPlayInfo");
                        }
                        break;

                    case HookKind.PatternLoaderLambda:
                        // BMS 차트 주입도 ProbeNoteArrayMembers 안에서 일어난다. 로그 플래그만 보고 막으면
                        // 로그를 끄는 순간 커스텀 곡이 조용히 원본 차트로 나오므로, 커스텀 곡일 때는 항상 부른다.
                        if (EnableRuntimeProbeLogging || IsCustomChartPlayActive)
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

                    case HookKind.TrackListViewerMoveCursor:
                        HandleTrackListViewerMoveCursor(__instance, args);
                        break;

                    // Refresh는 패치하면 NRE가 나므로, 게임이 표시를 다시 채운 뒤 불리는 이 두 훅에서 재적용한다.
                    case HookKind.LevelSelectorFetchTrackRecord:
                        HandleLevelSelectorFetchTrackRecord(__instance, args);
                        HandleLevelSelectorRefresh(__instance);
                        break;

                    case HookKind.LevelSelectorFetchJacektImage:
                        HandleLevelSelectorFetchJacektImage(__instance, args);
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

        private static void TryEnablePlayerBaseAutoPlay(object? playerBaseInstance)
        {
            if (playerBaseInstance is null)
            {
                return;
            }

            try
            {
                Type type = playerBaseInstance.GetType();
                object? beforeObj = TryGetMemberValue(playerBaseInstance, type, "IsAutoPlay")
                    ?? TryGetMemberValue(playerBaseInstance, type, "isAutoPlay");
                string beforeText = beforeObj?.ToString() ?? "<unknown>";

                bool applied = false;
                PropertyInfo? autoPlayProp = type.GetProperty("IsAutoPlay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (autoPlayProp is not null && autoPlayProp.CanWrite && autoPlayProp.PropertyType == typeof(bool))
                {
                    autoPlayProp.SetValue(playerBaseInstance, true);
                    applied = true;
                }

                if (!applied)
                {
                    FieldInfo? autoPlayField = type.GetField("IsAutoPlay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? type.GetField("isAutoPlay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (autoPlayField is not null && autoPlayField.FieldType == typeof(bool))
                    {
                        autoPlayField.SetValue(playerBaseInstance, true);
                        applied = true;
                    }
                }

                if (!applied)
                {
                    MethodInfo? setter = type.GetMethod("set_IsAutoPlay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null)
                        ?? type.GetMethod("SetAutoPlay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                    if (setter is not null)
                    {
                        setter.Invoke(playerBaseInstance, new object[] { true });
                        applied = true;
                    }
                }

                object? afterObj = TryGetMemberValue(playerBaseInstance, type, "IsAutoPlay")
                    ?? TryGetMemberValue(playerBaseInstance, type, "isAutoPlay");
                string afterText = afterObj?.ToString() ?? "<unknown>";
                if (EnableRuntimeProbeLogging)
                {
                    MelonLogger.Msg($"[PlayerBaseAutoPlay] applied={applied} before={beforeText} after={afterText}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlayerBaseAutoPlay] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void LogPlaySceneTrackInfo(object travelArgs)
        {
            try
            {
                Type travelArgsType = travelArgs.GetType();
                object? playTrack = TryGetMemberValue(travelArgs, travelArgsType, "PlayTrack");
                object? playLevel = TryGetMemberValue(travelArgs, travelArgsType, "PlayLevel");

                if (playTrack is null)
                {
                    MelonLogger.Msg("[PlaySceneTrack] track=<null>");
                    return;
                }

                Type trackType = playTrack.GetType();
                object trackId = TryGetMemberValue(playTrack, trackType, "TrackID")
                                 ?? TryGetMemberValue(playTrack, trackType, "TrackId")
                                 ?? TryGetMemberValue(playTrack, trackType, "trackId")
                                 ?? "<unknown_id>";

                object title = TryGetMemberValue(playTrack, trackType, "TrackDisplayName")
                               ?? TryGetMemberValue(playTrack, trackType, "TrackDisplayNameEN")
                               ?? TryGetMemberValue(playTrack, trackType, "displayName")
                               ?? TryGetMemberValue(playTrack, trackType, "displayNameEN")
                               ?? "<unknown_title>";

                string levelInfo = playLevel?.ToString() ?? "<null_level>";
                MelonLogger.Msg($"[PlaySceneTrack] id={trackId} title={title} level={levelInfo}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlaySceneTrack] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void LogPlayerBaseJacketSnapshot(object? playerBaseInstance)
        {
            if (PlayerBaseJacketLogged || playerBaseInstance is null)
            {
                return;
            }

            PlayerBaseJacketLogged = true;
            try
            {
                Type type = playerBaseInstance.GetType();
                object? jacketObj = TryGetMemberValue(playerBaseInstance, type, "jacket")
                    ?? TryGetMemberValue(playerBaseInstance, type, "Jacket");
                if (jacketObj is null)
                {
                    MelonLogger.Warning("[PlayerBaseJacket] 재킷 멤버를 찾지 못했거나 null입니다.");
                    return;
                }

                string jacketType = jacketObj.GetType().FullName ?? jacketObj.GetType().Name;
                object? gameObject = TryGetMemberValue(jacketObj, jacketObj.GetType(), "gameObject")
                    ?? TryGetMemberValue(jacketObj, jacketObj.GetType(), "GameObject");
                string sceneName = TryGetSceneName(gameObject);
                object? transform = TryGetMemberValue(jacketObj, jacketObj.GetType(), "transform")
                    ?? (gameObject is null ? null : TryGetMemberValue(gameObject, gameObject.GetType(), "transform"));
                string path = transform is null ? "<unknown>" : BuildTransformPath(transform);
                object? isAutoPlayObj = TryGetMemberValue(playerBaseInstance, type, "IsAutoPlay")
                    ?? TryGetMemberValue(playerBaseInstance, type, "isAutoPlay");
                string isAutoPlayText = isAutoPlayObj?.ToString() ?? "<unknown>";

                MelonLogger.Msg($"[PlayerBaseJacket] type={jacketType} scene={sceneName} path={path} IsAutoPlay={isAutoPlayText}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlayerBaseJacket] snapshot failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool ShouldSkipInvocationLog(HookSite site)
        {
            if (!EnableVerboseInvocationLogging || site.Suppressed)
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
        // 같은 로더에 다시 Load가 불려도 현재 onCompleted가 우리 래퍼 그대로면 다시 감싸지 않고,
        // 게임이 onCompleted를 새 델리게이트로 바꿔 놨으면(포인터 불일치) 그 새 델리게이트를 다시 감싼다.
        // 예전에는 로더 포인터만 HashSet에 넣어 "한 번 감쌌으면 끝"이라, 해제된 주소가 재사용되거나
        // onCompleted가 교체되면 2차 경로가 조용히 빠졌다. PlayerBase.Play마다 비운다(UpdateCustomChartPlayState).
        private static readonly Dictionary<IntPtr, IntPtr> WrappedPatternLoaders = new Dictionary<IntPtr, IntPtr>();
        private static PropertyInfo? _patternLoaderOnCompletedProperty;
        private static Type? _patternLoaderOnCompletedOwner;

        private static void TryWrapPatternLoaderOnCompleted(object? instance)
        {
            if (instance is null) return;
            try
            {
                Type loaderType = instance.GetType();
                if (_patternLoaderOnCompletedOwner != loaderType)
                {
                    _patternLoaderOnCompletedProperty = loaderType.GetProperty("onCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _patternLoaderOnCompletedOwner = loaderType;
                }

                PropertyInfo? prop = _patternLoaderOnCompletedProperty;
                if (prop is null)
                {
                    if (LogOnce("dualwrapper.no-oncompleted"))
                    {
                        MelonLogger.Warning($"[DualWrapper] {loaderType.FullName}.onCompleted 를 찾지 못해 2차 경로를 설치하지 못했습니다.");
                    }
                    return;
                }

                if (prop.GetValue(instance) is not Il2CppSystem.Action<Il2CppMikazuki.Pattern> origAction)
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
                prop.SetValue(instance, wrapper);
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
