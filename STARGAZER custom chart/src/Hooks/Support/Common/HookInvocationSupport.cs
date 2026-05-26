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
        // Toggle to enable TrackID rewrite PoC
        private static bool EnableTrackIdRewrite = true;
        private static string TrackIdRewriteValue = "custom_new_id_001";

        private static void HookPrefix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                object[] args = __args ?? Array.Empty<object>();

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.TrackLoader", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "LoadTracksAsync", StringComparison.Ordinal))
                {
                    TryPatchTrackLoaderOnLoadedCallback(args);
                }

                // Log calls to Il2CppStargazer.TrackLoader+INNER_TrackMetaData.GetParser()
                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.TrackLoader+INNER_TrackMetaData", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "GetParser", StringComparison.Ordinal))
                {
                    MelonLogger.Msg($"[HookInvoke][GetParser] {BuildInvocationSignature(__originalMethod, __instance, args)}");
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Play.PlayerBase", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "Play", StringComparison.Ordinal))
                {
                    if (EnableForceAutoPlayAtPlayerBasePlay)
                    {
                        TryEnablePlayerBaseAutoPlay(__instance);
                    }

                    LogPlayerBaseJacketSnapshot(__instance);

                    if (args.Length > 0 && args[0] != null)
                    {
                        object travelArgs = args[0];
                        Type tType = travelArgs.GetType();

                        object? playTrack = TryGetMemberValue(travelArgs, tType, "PlayTrack");
                        object? playLevel = TryGetMemberValue(travelArgs, tType, "PlayLevel");

                        string trackInfo = "<null_track>";
                        if (playTrack != null)
                        {
                            Type ptType = playTrack.GetType();
                            object trackId = TryGetMemberValue(playTrack, ptType, "TrackID")
                                             ?? TryGetMemberValue(playTrack, ptType, "TrackId")
                                             ?? TryGetMemberValue(playTrack, ptType, "trackId")
                                             ?? "<unknown_id>";

                            object trackTitle = TryGetMemberValue(playTrack, ptType, "TrackDisplayName")
                                                ?? TryGetMemberValue(playTrack, ptType, "TrackDisplayNameEN")
                                                ?? TryGetMemberValue(playTrack, ptType, "displayName")
                                                ?? TryGetMemberValue(playTrack, ptType, "displayNameEN")
                                                ?? "<unknown_title>";

                            trackInfo = $"TrackID: {trackId}, Title: {trackTitle}";
                        }

                        string levelInfo = playLevel?.ToString() ?? "<null_level>";
                        MelonLogger.Msg($"[AutoPlay] Song playing - {trackInfo}, Level: {levelInfo}");
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
                if (ShouldSkipInvocationLog(__originalMethod, args))
                {
                    return;
                }

                MelonLogger.Msg($"[HookInvoke][POST] {BuildInvocationSignature(__originalMethod, __instance, args)}");

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Play.Widgets.CurrentTrackViewer", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "Listen", StringComparison.Ordinal))
                {
                    ProbeCurrentTrackViewerImageMembers(__instance, args);
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.Result.PlayInfoViewer", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "ShowPlayInfo", StringComparison.Ordinal))
                {
                    ProbeResultPlayInfoJacketMembers(__instance, args);
                    ProbeNoteArrayMembers(args.Length > 0 ? args[0] : null, "Result.PlayInfoViewer.ShowPlayInfo");
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "_Load_b__5_0", StringComparison.Ordinal))
                {
                    ProbeNoteArrayMembers(args.Length > 0 ? args[0] : null, "PatternLoader._Load_b__5_0");
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.TrackSelector.TrackSelector", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "Set", StringComparison.Ordinal)
                    && args.Length > 0
                    && args[0] is not null)
                {
                    HandleTrackSelectorSetTracks(args[0]);

                    // PoC: attempt to rewrite TrackID for provided tracks (non-destructive)
                    try
                    {
                        var target = args[0];
                        int appliedCount = 0;
                        if (target is System.Collections.IEnumerable && !(target is string))
                        {
                            foreach (var item in (System.Collections.IEnumerable)target)
                            {
                                if (item is null) continue;
                                if (TrySetTrackId(item, item.GetType(), "album_override_id_001")) appliedCount++;
                            }
                            MelonLogger.Msg($"[TrackSelector][Set] TrackID rewrite applied to {appliedCount} items");
                        }
                        else
                        {
                            bool applied = TrySetTrackId(target, target.GetType(), "album_override_id_001");
                            MelonLogger.Msg($"[TrackSelector][Set] TrackID rewrite applied={applied} targetType={target.GetType().FullName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[TrackSelector][Set] TrackID rewrite failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.LevelSelector.LevelSelector", StringComparison.Ordinal))
                {
                    if (string.Equals(__originalMethod.Name, "FetchTrackRecord", StringComparison.Ordinal))
                    {
                        if (__instance is not null && !LevelSelectorCatalogLogged && args.Length > 0 && args[0] is not null)
                        {
                            LevelSelectorCatalogLogged = true;
                            EnumerateLevelSelectorLevels(__instance);
                        }

                        if (args.Length > 0 && args[0] is not null)
                        {
                            object record = args[0];
                            List<string> recordDetails = new List<string>();
                            string[] detailNames = { "score", "rate", "perfect", "accuracy", "combo", "grade", "clear", "rank" };
                            foreach (string name in detailNames)
                            {
                                if (TryGetValueByNameCandidates(record, new[] { name }, out object? val) && val is not null)
                                {
                                    recordDetails.Add($"{name}={val}");
                                }
                            }
                            if (recordDetails.Count > 0)
                            {
                                MelonLogger.Msg($"[LevelSelector][FetchTrackRecord] {string.Join(", ", recordDetails)}");
                            }
                        }
                    }

                    if (string.Equals(__originalMethod.Name, "FetchJacektImage", StringComparison.Ordinal)
                        && args.Length > 0
                        && args[0] is not null)
                    {
                        object sprite = args[0];
                        string spriteName = TryGetPropertyValue(sprite, "name")?.ToString() ?? sprite.GetType().Name;
                        MelonLogger.Msg($"[LevelSelector][FetchJacektImage] Sprite Name: {spriteName}");
                    }
                }

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.TrackLoader", StringComparison.Ordinal)
                    && string.Equals(__originalMethod.Name, "LoadTracksAsync", StringComparison.Ordinal)
                    && !TrackLoaderCatalogLogged && __instance is not null)
                {
                    TrackLoaderCatalogLogged = true;
                    MelonLogger.Msg($"[TrackLoader] {BuildObjectMemberCatalog("TrackLoader", __instance)}");
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
                MelonLogger.Msg($"[PlayerBaseAutoPlay] applied={applied} before={beforeText} after={afterText}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlayerBaseAutoPlay] failed: {ex.GetType().Name}: {ex.Message}");
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
                    MelonLogger.Warning("[PlayerBaseJacket] jacket member not found or null.");
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
            string methodFullName = $"{method.DeclaringType?.FullName}.{method.Name}";
            if (SuppressedInvocationMethods.Contains(methodFullName))
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

        private static bool TrySetTrackId(object trackObj, Type trackType, string newId)
        {
            if (trackObj is null || trackType is null) return false;

            string[] candidates = new[] { "TrackID", "TrackId", "trackId", "id" };
            foreach (string name in candidates)
            {
                try
                {
                    FieldInfo? f = trackType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f is not null && f.FieldType == typeof(string))
                    {
                        f.SetValue(trackObj, newId);
                        return true;
                    }

                    PropertyInfo? p = trackType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p is not null && p.CanWrite && p.PropertyType == typeof(string))
                    {
                        p.SetValue(trackObj, newId);
                        return true;
                    }
                }
                catch { }
            }

            // try setter method
            try
            {
                MethodInfo? setter = trackType.GetMethod("set_TrackID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                     ?? trackType.GetMethod("SetTrackID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                     ?? trackType.GetMethod("set_Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setter is not null)
                {
                    setter.Invoke(trackObj, new object[] { newId });
                    return true;
                }
            }
            catch { }

            return false;
        }

    }
}
