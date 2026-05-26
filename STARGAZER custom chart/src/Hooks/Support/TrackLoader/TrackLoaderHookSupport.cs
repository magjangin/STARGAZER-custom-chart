using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void TryPatchTrackLoaderOnLoadedCallback(object[] args)
        {
            if (args.Length == 0 || args[0] is null)
            {
                return;
            }

            try
            {
                object callback = args[0];
                Type delegateType = callback.GetType();

                MethodInfo? invokeMethod = delegateType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                if (invokeMethod is null)
                {
                    MelonLogger.Warning("[TrackLoader] callback invoke method not found.");
                    return;
                }

                try
                {
                    string paramTypes = string.Join(",", invokeMethod.GetParameters().Select(p => p.ParameterType.Name));
                    MelonLogger.Msg($"[TrackLoader] callback delegateType={delegateType.FullName} invoke={invokeMethod.Name} params={paramTypes}");
                    if (delegateType.IsGenericType)
                    {
                        var genArgs = delegateType.GetGenericArguments().Select(t => t.FullName ?? t.Name);
                        MelonLogger.Msg($"[TrackLoader] delegate generic args=[{string.Join(",", genArgs)}]");
                    }

                    // Try to inspect delegate Target/Method if available
                    try
                    {
                        if (callback is Delegate del)
                        {
                            MelonLogger.Msg($"[TrackLoader] delegate.Method={del.Method.DeclaringType?.FullName}.{del.Method.Name} targetType={del.Target?.GetType().FullName ?? "<null>"}");
                        }
                        else
                        {
                            var targetProp = callback.GetType().GetProperty("Target", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            var methodProp = callback.GetType().GetProperty("Method", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            object? targetVal = targetProp?.GetValue(callback);
                            object? methodVal = methodProp?.GetValue(callback);
                            if (targetVal is not null)
                                MelonLogger.Msg($"[TrackLoader] delegate.TargetType={targetVal.GetType().FullName}");
                            if (methodVal is MethodInfo mi)
                                MelonLogger.Msg($"[TrackLoader] delegate.MethodInfo={mi.DeclaringType?.FullName}.{mi.Name}");
                        }
                    }
                    catch { }
                }
                catch { }

                string invokeMethodKey = BuildMethodPatchKey(invokeMethod);
                if (!TrackLoaderCallbackPatchedMethods.Add(invokeMethodKey))
                {
                    return;
                }

                RuntimeHarmonyInstance.Patch(
                    invokeMethod,
                    postfix: new HarmonyMethod(typeof(GameTypeEnumeratorMod).GetMethod(nameof(TrackLoaderOnLoadedCallbackPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                MelonLogger.Msg($"[TrackLoader] patched onLoaded callback invoke: {delegateType.FullName ?? delegateType.Name} methodKey={invokeMethodKey}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLoader] failed to patch onLoaded callback: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void TrackLoaderOnLoadedCallbackPostfix(MethodBase __originalMethod, object[]? __args)
        {
            try
            {
                MelonLogger.Msg($"[TrackLoader.Postfix] __originalMethod={__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
                object[] args = __args ?? Array.Empty<object>();
                if (args.Length == 0 || args[0] is null)
                {
                    return;
                }
                try
                {
                    MelonLogger.Msg($"[TrackLoader.Postfix] arg0Type={(args[0]?.GetType().FullName ?? "<null>")}");
                    if (args[0] is System.Collections.IEnumerable && !(args[0] is string))
                    {
                        int loadedCount = EnumerateCollectionItems(args[0], 2048).Count();
                        MelonLogger.Msg($"[TrackLoader.Postfix] loaded collection count={loadedCount}");

                        try
                        {
                            int sampleLimit = Math.Min(10, loadedCount);
                            if (sampleLimit > 0)
                            {
                                MelonLogger.Msg($"[TrackLoader.Postfix] dumping first {sampleLimit} items:");
                                int idx = 0;
                                foreach (object? item in EnumerateCollectionItems(args[0], sampleLimit))
                                {
                                    if (item is null)
                                    {
                                        MelonLogger.Msg($"  [{idx++:D2}] null");
                                        continue;
                                    }

                                    Type t = item.GetType();
                                    string id = TryGetMemberValue(item, t, "TrackID")?.ToString()
                                                ?? TryGetMemberValue(item, t, "trackId")?.ToString()
                                                ?? "?";
                                    string title = TryGetMemberValue(item, t, "TrackDisplayName")?.ToString()
                                                   ?? TryGetMemberValue(item, t, "TrackDisplayNameEN")?.ToString()
                                                   ?? TryGetMemberValue(item, t, "displayName")?.ToString()
                                                   ?? "?";
                                    string artist = TryGetMemberValue(item, t, "ArtistDisplayName")?.ToString() ?? "?";
                                    MelonLogger.Msg($"  [{idx++:D2}] id={id} | {title} / {artist} (type={t.FullName})");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[TrackLoader.Postfix] failed to sample items: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                catch { }

                if (!TrackLoaderListLogged)
                {
                    TrackLoaderListLogged = true;
                    MelonLogger.Msg($"[TrackLoader] onLoaded callback invoked via {__originalMethod.DeclaringType?.FullName ?? __originalMethod.Name}");
                    DumpTrackListViewerTracks(args[0]);
                    return;
                }

                int count = EnumerateCollectionItems(args[0], 2048).Count();
                MelonLogger.Msg($"[TrackLoader] onLoaded invoked again. trackCount={count}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLoader] failed to inspect loaded track list: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}