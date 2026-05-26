using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static readonly HashSet<string> LoggedCurrentTrackViewerImageHits = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedResultImageHits = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> SuppressedInvocationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Il2CppStargazer.Play.PlayerBase.SetupPlay",
            "Il2CppStargazer.Play.TravelPlayer.SetupPlay",
            "Il2CppStargazer.Starlike.Sound.SoundPlayer.GetBGMHandler",
            "Il2CppStarlike.Sound.SoundPlayer.GetBGMHandler",
            "Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler.BGMPlayChecker"
        };
        private static readonly object InvocationLogThrottleLock = new object();
        private static readonly Dictionary<string, InvocationLogThrottleEntry> InvocationLogThrottleMap = new Dictionary<string, InvocationLogThrottleEntry>(StringComparer.Ordinal);
        private const int InvocationLogWindowMs = 1000;
        private const int InvocationLogMaxPerWindow = 6;
        private const bool EnableForceAutoPlayAtPlayerBasePlay = true;
        private static bool PlayerBaseJacketLogged;
        private static bool PlayerBaseAutoPlaySetAttempted;

        private void TryApplyRuntimeHookPatches(string phase)
        {
            if (_hooksPatchAttempted)
            {
                LoggerInstance.Msg($"[HookPatch][{phase}] skip patching (already attempted, appliedAll={_hooksApplied}).");
                return;
            }

            _hooksPatchAttempted = true;
            MelonLogger.Msg($"[HookPatch][{phase}] begin patching. mode=startingpoint-id-suffix");

            PatchSpec[] specs =
            {
                // Play entry (safe-first set; SetupPlay hooks intentionally excluded)
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "Play", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.PlayerBase", "PlayStart", 1, "IPlayHandler"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer", "Load", 2, "TravelArgs", "Action"),

                // Chart loading
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPattern", 2, "ELevels", "Action"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", "Load", 1, "TravelArgs"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader", "_Load_b__5_0", 1, "Pattern"),

                // Audio/BGM
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadBGMClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "LoadPreviewClip", 1, "Action"),
                new PatchSpec("Il2CppStargazer.Starlike.Sound.SoundPlayer", "PlayBGM", 2, "AudioClip", "ESoundType")
                    .WithTypeFallback("Il2CppStarlike.Sound.SoundPlayer"),
                new PatchSpec("Il2CppStargazer.Starlike.Sound.SoundPlayer", "GetBGMHandler", 0)
                    .WithTypeFallback("Il2CppStarlike.Sound.SoundPlayer"),
                new PatchSpec("Il2CppStargazer.Starlike.Sound.SoundPlayer", "StopBGM", 0)
                    .WithTypeFallback("Il2CppStarlike.Sound.SoundPlayer"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler", "BGMPlayChecker", 1, "Single"),
                new PatchSpec("Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler", "Play", 1, "Single"),

                // Play widget/UI probe
                new PatchSpec("Il2CppStargazer.Play.Widgets.CurrentTrackViewer", "Listen", 1),

                // Result flow
                new PatchSpec("Il2CppStargazer.Travel.Result.PlayInfoViewer", "ShowPlayInfo", 1, "ITravelResultData"),

                // LevelSelector hooks
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchTrackRecord", 1, "ITrackRecord"),
                new PatchSpec("Il2CppStargazer.Travel.LevelSelector.LevelSelector", "FetchJacektImage", 1, "Sprite")
            };

            int patchedCount = 0;
            int missingCount = 0;
            var patchedMethodKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (PatchSpec spec in specs)
            {
                MethodInfo? target = ResolveTargetMethod(spec);
                if (target is null)
                {
                    LoggerInstance.Warning($"[HookPatch][{phase}] target not found: {spec.TypeName}.{spec.MethodName}");
                    missingCount++;
                    continue;
                }

                if (TryPatchMethod(target, phase, patchedMethodKeys))
                {
                    patchedCount++;
                }
            }

            patchedCount += PatchTrackDisplayNameOverrides(phase, ref missingCount);
            _hooksApplied = patchedCount > 0 && missingCount == 0;
            LoggerInstance.Msg($"[HookPatch][{phase}] patch summary: patched={patchedCount}, missing={missingCount}, appliedAll={_hooksApplied}");
        }

        private bool TryPatchMethod(MethodInfo target, string phase, HashSet<string> patchedMethodKeys)
        {
            string methodKey = BuildMethodPatchKey(target);
            if (!patchedMethodKeys.Add(methodKey))
            {
                return false;
            }

            RuntimeHarmonyInstance.Patch(
                target,
                prefix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(HookPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(HookPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

            LoggerInstance.Msg($"[HookPatch][{phase}] patched: {target.DeclaringType?.FullName}.{target.Name}");
            return true;
        }

        private static string BuildMethodPatchKey(MethodInfo method)
        {
            string parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
            return $"{method.DeclaringType?.FullName}.{method.Name}({parameters})";
        }

        private int PatchTrackDisplayNameOverrides(string phase, ref int missingCount)
        {
            int patched = 0;

            patched += PatchGetterPostfix(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackDisplayName", 0), nameof(TrackDisplayNamePostfix), phase, ref missingCount);
            patched += PatchGetterPostfix(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackDisplayNameEN", 0), nameof(TrackDisplayNameEnPostfix), phase, ref missingCount);
            patched += PatchGetterPostfix(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackMetaData", "get_displayName", 0), nameof(MetaDisplayNamePostfix), phase, ref missingCount);
            patched += PatchGetterPostfix(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackMetaData", "get_displayNameEN", 0), nameof(MetaDisplayNameEnPostfix), phase, ref missingCount);
            patched += PatchGetterPostfix(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_ArtistDisplayName", 0), nameof(ArtistDisplayNamePostfix), phase, ref missingCount);
            patched += PatchGetterPostfix(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackID", 0), nameof(TrackIDPostfix), phase, ref missingCount);

            return patched;
        }

        private int PatchGetterPostfix(PatchSpec spec, string postfixMethodName, string phase, ref int missingCount)
        {
            MethodInfo? getter = ResolveTargetMethod(spec);
            if (getter is null)
            {
                LoggerInstance.Warning($"[HookPatch][{phase}] target not found: {spec.TypeName}.{spec.MethodName}");
                missingCount++;
                return 0;
            }

            RuntimeHarmonyInstance.Patch(
                getter,
                postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(postfixMethodName, BindingFlags.Static | BindingFlags.NonPublic)));
            LoggerInstance.Msg($"[HookPatch][{phase}] patched: {getter.DeclaringType?.FullName}.{getter.Name}");
            return 1;
        }

        private static MethodInfo? ResolveTargetMethod(PatchSpec spec)
        {
            Type? type = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly => spec.TypeNames.Select(typeName => assembly.GetType(typeName, false)))
                .FirstOrDefault(candidate => candidate is not null);

            if (type is null)
            {
                return null;
            }

            MethodInfo[] candidates = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, spec.MethodName, StringComparison.Ordinal))
                .ToArray();

            foreach (MethodInfo method in candidates)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != spec.ParameterCount)
                {
                    continue;
                }

                bool signatureMatches = true;
                for (int i = 0; i < spec.ParameterTypeNameContains.Length; i++)
                {
                    string required = spec.ParameterTypeNameContains[i];
                    string actual = parameters[i].ParameterType.Name;
                    if (actual.IndexOf(required, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        signatureMatches = false;
                        break;
                    }
                }

                if (signatureMatches)
                {
                    return method;
                }
            }

            return candidates.FirstOrDefault(method => method.GetParameters().Length == spec.ParameterCount);
        }

        private static void HookPrefix(MethodBase __originalMethod, object? __instance, object[]? __args)
        {
            try
            {
                object[] args = __args ?? Array.Empty<object>();
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
                        
                        string levelInfo = playLevel != null ? playLevel.ToString() : "<null_level>";
                        
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
                string isAutoPlayText = isAutoPlayObj is null ? "<unknown>" : isAutoPlayObj.ToString() ?? "<null>";

                MelonLogger.Msg($"[PlayerBaseJacket] type={jacketType} scene={sceneName} path={path} IsAutoPlay={isAutoPlayText}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PlayerBaseJacket] snapshot failed: {ex.GetType().Name}: {ex.Message}");
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

                if (string.Equals(__originalMethod.DeclaringType?.FullName, "Il2CppStargazer.Travel.LevelSelector.LevelSelector", StringComparison.Ordinal))
                {
                    if (string.Equals(__originalMethod.Name, "FetchTrackRecord", StringComparison.Ordinal))
                    {
                        if (__instance is not null)
                        {
                            string instanceCatalog = BuildObjectMemberCatalog("LevelSelectorInstance", __instance);
                            MelonLogger.Msg($"[LevelSelectorInstance] Catalog: {instanceCatalog}");

                            EnumerateLevelSelectorLevels(__instance);
                        }

                        if (args.Length > 0 && args[0] is not null)
                        {
                            object record = args[0];
                            string recordCatalog = BuildObjectMemberCatalog("FetchTrackRecord.record", record);
                            MelonLogger.Msg($"[LevelSelector][FetchTrackRecord] Record Catalog: {recordCatalog}");

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
                                MelonLogger.Msg($"[LevelSelector][FetchTrackRecord] Record Details: {string.Join(", ", recordDetails)}");
                            }
                        }
                    }
                    
                    if (string.Equals(__originalMethod.Name, "FetchJacektImage", StringComparison.Ordinal))
                    {
                        if (args.Length > 0 && args[0] is not null)
                        {
                            object sprite = args[0];
                            string spriteName = TryGetPropertyValue(sprite, "name")?.ToString() ?? sprite.GetType().Name;
                            MelonLogger.Msg($"[LevelSelector][FetchJacektImage] Sprite Name: {spriteName}");
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[HookInvoke][POST] logging failed: {ex.GetType().Name}: {ex.Message}");
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

        private static object? TryGetMemberValue(object instance, Type type, string memberName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                PropertyInfo? property = type.GetProperty(memberName, Flags);
                if (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(instance);
                }
            }
            catch
            {
            }

            try
            {
                FieldInfo? field = type.GetField(memberName, Flags);
                if (field is not null)
                {
                    return field.GetValue(instance);
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo? getter = type.GetMethod($"get_{memberName}", Flags, null, Type.EmptyTypes, null);
                if (getter is not null)
                {
                    return getter.Invoke(instance, Array.Empty<object>());
                }
            }
            catch
            {
            }

            return null;
        }

        private sealed class InvocationLogThrottleEntry
        {
            public InvocationLogThrottleEntry(long windowStartMs, int count)
            {
                WindowStartMs = windowStartMs;
                Count = count;
            }

            public long WindowStartMs { get; set; }
            public int Count { get; set; }
        }

        private sealed class PatchSpec
        {
            public PatchSpec(string typeName, string methodName, int parameterCount, params string[] parameterTypeNameContains)
            {
                TypeName = typeName;
                TypeNames = new[] { typeName };
                MethodName = methodName;
                ParameterCount = parameterCount;
                ParameterTypeNameContains = parameterTypeNameContains;
            }

            public string TypeName { get; }
            public string[] TypeNames { get; private set; }
            public string MethodName { get; }
            public int ParameterCount { get; }
            public string[] ParameterTypeNameContains { get; }

            public PatchSpec WithTypeFallback(params string[] fallbackTypeNames)
            {
                TypeNames = new[] { TypeName }.Concat(fallbackTypeNames).ToArray();
                return this;
            }
        }

        private static void EnumerateLevelSelectorLevels(object instance)
        {
            try
            {
                object? levels = TryGetMemberValue(instance, instance.GetType(), "levels");
                if (levels is null)
                {
                    MelonLogger.Warning("[LevelSelector][Enumerate] 'levels' collection is null.");
                    return;
                }

                var items = EnumerateCollectionItems(levels, 12).ToList();
                MelonLogger.Msg($"[LevelSelector][Enumerate] Found 'levels' collection with {items.Count} items. Enumerating elements:");

                for (int i = 0; i < items.Count; i++)
                {
                    object? item = items[i];
                    if (item is null)
                    {
                        MelonLogger.Msg($"  [{i}] null");
                        continue;
                    }

                    string itemType = item.GetType().FullName ?? item.GetType().Name;

                    // Log the Member Catalog of SelectionLevel for the first item
                    if (i == 0)
                    {
                        string selectionLevelCatalog = BuildObjectMemberCatalog("SelectionLevel", item);
                        MelonLogger.Msg($"[LevelSelector][Enumerate] SelectionLevel Catalog: {selectionLevelCatalog}");
                    }

                    // Extract details using candidates
                    string details = "";
                    try
                    {
                        string[] detailNames = { "name", "id", "level", "title", "text", "value", "lv", "difficulty", "num", "score", "rate", "number", "index" };
                        List<string> foundDetails = new List<string>();
                        foreach (string detailName in detailNames)
                        {
                            if (TryGetValueByNameCandidates(item, new[] { detailName }, out object? detailVal) && detailVal is not null)
                            {
                                foundDetails.Add($"{detailName}={detailVal}");
                            }
                        }
                        if (foundDetails.Count > 0)
                        {
                            details = $" ({string.Join(", ", foundDetails)})";
                        }
                    }
                    catch { }

                    // Deeper inspection of sub-items inside SelectionLevel
                    try
                    {
                        object? textProvider = TryGetMemberValue(item, item.GetType(), "levelText")
                            ?? TryGetMemberValue(item, item.GetType(), "LevelText")
                            ?? TryGetMemberValue(item, item.GetType(), "text")
                            ?? TryGetMemberValue(item, item.GetType(), "Text")
                            ?? TryGetMemberValue(item, item.GetType(), "levelItem")
                            ?? TryGetMemberValue(item, item.GetType(), "LevelItem")
                            ?? TryGetMemberValue(item, item.GetType(), "_levelText_k__BackingField");

                        if (textProvider is not null)
                        {
                            string subTypeName = textProvider.GetType().FullName ?? textProvider.GetType().Name;
                            
                            if (i == 0)
                            {
                                string subCatalog = BuildObjectMemberCatalog("SelectionLevel.SubItem", textProvider);
                                MelonLogger.Msg($"[LevelSelector][Enumerate] Sub-item Catalog: {subCatalog}");
                            }

                            List<string> subDetails = new List<string>();
                            if (TryGetExactPropertyValue(textProvider, "Text", out object? exactTextVal) && exactTextVal is not null)
                            {
                                subDetails.Add($"Text=\"{exactTextVal}\"");
                            }

                            string[] subDetailNames = { "level", "lv", "number", "index", "value", "title", "text", "name", "id", "difficulty" };
                            foreach (string subName in subDetailNames)
                            {
                                if (TryGetValueByNameCandidates(textProvider, new[] { subName }, out object? subVal) && subVal is not null)
                                {
                                    subDetails.Add($"{subName}={subVal}");
                                }
                            }
                            if (subDetails.Count > 0)
                            {
                                details += $" | subItem[{subTypeName}]: [{string.Join(", ", subDetails)}]";
                            }

                            // Nested TextProvider probe
                            object? nestedTextProvider = TryGetMemberValue(textProvider, textProvider.GetType(), "levelText")
                                ?? TryGetMemberValue(textProvider, textProvider.GetType(), "LevelText")
                                ?? TryGetMemberValue(textProvider, textProvider.GetType(), "_levelText_k__BackingField");
                            if (nestedTextProvider is not null)
                            {
                                List<string> nestedDetails = new List<string>();
                                if (TryGetExactPropertyValue(nestedTextProvider, "Text", out object? nestedExactText) && nestedExactText is not null)
                                {
                                    nestedDetails.Add($"Text=\"{nestedExactText}\"");
                                }
                                if (nestedDetails.Count > 0)
                                {
                                    details += $" | nestedTextProvider: [{string.Join(", ", nestedDetails)}]";
                                }
                            }
                        }
                    }
                    catch { }

                    MelonLogger.Msg($"  [{i}] Type={itemType}{details}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LevelSelector][Enumerate] Failed to enumerate selection levels: {ex.Message}");
            }
        }
    }
}
