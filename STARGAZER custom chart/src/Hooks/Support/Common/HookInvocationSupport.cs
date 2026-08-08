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
        private static void HookPrefix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                object[] args = __args ?? Array.Empty<object>();
                LogBgmDebugInvocation(__originalMethod, __instance, args);

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.TrackLoader", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "LoadTracksAsync", StringComparison.Ordinal))
                {
                    TryPatchTrackLoaderOnLoadedCallback(args);
                }

                // 난이도 문자열이 패턴 에셋 로딩에 영향을 주는지 추적하기 위한 진단 로그.
                // LoadPattern 직후 로딩이 멈추는 현상이 난이도 값과만 상관관계를 보였다.
                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.TrackLoader+INNER_TrackData", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "LoadPattern", StringComparison.Ordinal))
                {
                    LogPatternLoadDiagnostics(__instance, args);
                }

                // Il2CppStargazer.TrackLoader+INNER_TrackMetaData.GetParser() 호출을 로그로 남깁니다.
                if (EnableVerboseInvocationLogging
                    && string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.TrackLoader+INNER_TrackMetaData", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "GetParser", StringComparison.Ordinal))
                {
                    MelonLogger.Msg($"[HookInvoke][GetParser] {BuildInvocationSignature(__originalMethod, __instance, args)}");
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Play.PlayerBase", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "Play", StringComparison.Ordinal))
                {
                    IsInPlayScene = true;
                    UpdateCustomChartPlayState(args.Length > 0 ? args[0] : null);
                    MelonLogger.Msg("[PlayScene] 플레이씬 진입 — IsInPlayScene=true");

                    if (EnableForceAutoPlayAtPlayerBasePlay)
                    {
                        TryEnablePlayerBaseAutoPlay(__instance);
                    }

                    if (EnableRuntimeProbeLogging || EnablePlaySceneJacketLogging)
                    {
                        LogPlayerBaseJacketSnapshot(__instance);
                    }

                    if (EnablePlaySceneTrackLogging && args.Length > 0 && args[0] != null)
                    {
                        LogPlaySceneTrackInfo(args[0]);
                    }
                }

                if (ShouldSkipInvocationLog(__originalMethod, args))
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

        private static void HookPostfix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                object[] args = __args ?? Array.Empty<object>();
                if (!ShouldSkipInvocationLog(__originalMethod, args))
                {
                    MelonLogger.Msg($"[HookInvoke][POST] {BuildInvocationSignature(__originalMethod, __instance, args)}");
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Play.Widgets.CurrentTrackViewer", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "Listen", StringComparison.Ordinal))
                {
                    ApplyCustomLevelDisplayToPlayScene(__instance);

                    if (EnableRuntimeProbeLogging || EnablePlaySceneJacketLogging)
                    {
                        ProbeCurrentTrackViewerImageMembers(__instance, args);
                    }
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.Result.PlayInfoViewer", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "ShowPlayInfo", StringComparison.Ordinal))
                {
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
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "_Load_b__5_0", StringComparison.Ordinal))
                {
                    if (EnableRuntimeProbeLogging)
                    {
                        ProbeNoteArrayMembers(args.Length > 0 ? args[0] : null, "PatternLoader._Load_b__5_0");
                    }
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.TrackSelector.TrackSelector", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "Set", StringComparison.Ordinal)
                    && args.Length > 0
                    && args[0] is not null)
                {
                    HandleTrackSelectorSetTracks(args[0]);
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.TrackSelector.TrackListViewer", StringComparison.Ordinal))
                {
                    if (string.Equals(__originalMethod.Name, "MoveCursor", StringComparison.Ordinal))
                    {
                        HandleTrackListViewerMoveCursor(__instance, args);
                    }
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.LevelSelector.LevelSelector", StringComparison.Ordinal))
                {
                    if (string.Equals(__originalMethod.Name, "FetchTrackRecord", StringComparison.Ordinal))
                    {
                        HandleLevelSelectorFetchTrackRecord(__instance, args);
                    }

                    if (string.Equals(__originalMethod.Name, "FetchJacektImage", StringComparison.Ordinal))
                    {
                        HandleLevelSelectorFetchJacektImage(__instance, args);
                    }

                    if (string.Equals(__originalMethod.Name, "SetTrack", StringComparison.Ordinal))
                    {
                        HandleLevelSelectorSetTrack(__instance, args);
                    }

                    // Refresh는 패치하면 NRE가 나므로, 게임이 표시를 다시 채운 뒤 불리는 이 두 훅에서 재적용한다.
                    if (string.Equals(__originalMethod.Name, "FetchTrackRecord", StringComparison.Ordinal)
                        || string.Equals(__originalMethod.Name, "FetchJacektImage", StringComparison.Ordinal))
                    {
                        HandleLevelSelectorRefresh(__instance);
                    }
                }


                // PlaySFX 오버로드 자동 덤프: PlayBGM이 처음 호출되는 시점에 SoundPlayer 타입에서 PlaySFX 시그니처를 전부 출력
                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStarlike.Sound.SoundPlayer", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "PlayBGM", StringComparison.Ordinal)
                    && LogOnce("SoundPlayer.PlaySFX.overload.dump"))
                {
                    DumpPlaySFXOverloads(__originalMethod.DeclaringType!);
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStarlike.Sound.SoundPlayer", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "PlaySFX", StringComparison.Ordinal))
                {
                    HandlePlaySFX(__originalMethod, args);
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

        private static bool ShouldSkipInvocationLog(MethodBase method, object[] args)
        {
            if (!EnableVerboseInvocationLogging)
            {
                return true;
            }

            string methodFullName = $"{method.DeclaringType?.FullName}.{method.Name}";
            if (SuppressedInvocationMethods.Contains(methodFullName))
            {
                return true;
            }

            if (EnableFocusedHarmonyInvocationLogging && !FocusedHarmonyInvocationMethods.Contains(methodFullName))
            {
                return true;
            }

            long now = Environment.TickCount64;
            lock (InvocationLogThrottleLock)
            {
                if (!InvocationLogThrottleMap.TryGetValue(methodFullName, out InvocationLogThrottleEntry? throttleEntry))
                {
                    InvocationLogThrottleMap[methodFullName] = new InvocationLogThrottleEntry(now, 1);
                    return false;
                }

                if (now - throttleEntry.WindowStartMs > InvocationLogWindowMs)
                {
                    throttleEntry.WindowStartMs = now;
                    throttleEntry.Count = 1;
                    return false;
                }

                throttleEntry.Count++;
                return throttleEntry.Count > InvocationLogMaxPerWindow;
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

    }
}
