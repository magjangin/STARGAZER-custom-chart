using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        [HarmonyPatch]
        private static class TrackDataMeta_Postfixes
        {
            private static MethodBase TargetMethod1() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackDisplayName", 0));
            private static void Postfix1(object __instance, ref string __result)
            {
                DumpInnerTrackMetaDataSafe(__instance, "get_TrackDisplayName");
            }

            private static MethodBase TargetMethod2() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackDisplayNameEN", 0));
            private static void Postfix2(object __instance, ref string __result)
            {
                DumpInnerTrackMetaDataSafe(__instance, "get_TrackDisplayNameEN");
            }

            private static MethodBase TargetMethod3() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_ArtistDisplayName", 0));
            private static void Postfix3(object __instance, ref string __result)
            {
                DumpInnerTrackMetaDataSafe(__instance, "get_ArtistDisplayName");
            }

            private static MethodBase TargetMethod4() => ResolveRequiredTargetMethod(new PatchSpec("Il2CppStargazer.TrackLoader+INNER_TrackData", "get_TrackID", 0));
            private static void Postfix4(object __instance, ref string __result)
            {
                DumpInnerTrackMetaDataSafe(__instance, "get_TrackID");
            }
        }

        private static void DumpInnerTrackMetaDataSafe(object? instance, string caller)
        {
            try
            {
                DumpInnerTrackMetaData(instance, caller);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackMetaDump] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void DumpInnerTrackMetaData(object? instance, string caller)
        {
            if (instance is null) return;

            Type t = instance.GetType();
            BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? f = t.GetField("_metaData", searchFlags)
                          ?? t.GetField("metaData", searchFlags)
                          ?? t.GetField("m_metaData", searchFlags);

            object? metaObj = null;
            if (f is not null)
            {
                metaObj = f.GetValue(instance);
            }
            else
            {
                PropertyInfo? p = t.GetProperty("metaData", searchFlags)
                                   ?? t.GetProperty("MetaData", searchFlags);
                if (p is not null && p.CanRead)
                {
                    metaObj = p.GetValue(instance);
                }
            }

            // Fallback: try any field/property whose name contains 'meta' (covers obfuscated/variant names)
            if (metaObj is null)
            {
                foreach (FieldInfo ff in t.GetFields(searchFlags))
                {
                    if (ff.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            metaObj = ff.GetValue(instance);
                            if (metaObj is not null)
                            {
                                if (LogOnce($"TrackMetaDumpFallbackField:{t.FullName}:{ff.Name}"))
                                {
                                    MelonLogger.Msg($"[TrackMetaDump] fallback found meta field: {ff.Name}");
                                }
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            if (metaObj is null)
            {
                foreach (PropertyInfo pp in t.GetProperties(searchFlags))
                {
                    if (pp.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0 && pp.CanRead)
                    {
                        try
                        {
                            metaObj = pp.GetValue(instance);
                            if (metaObj is not null)
                            {
                                if (LogOnce($"TrackMetaDumpFallbackProperty:{t.FullName}:{pp.Name}"))
                                {
                                    MelonLogger.Msg($"[TrackMetaDump] fallback found meta property: {pp.Name}");
                                }
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            if (metaObj is null)
            {
                if (LogOnce($"TrackMetaDumpNotFound:{t.FullName}"))
                {
                    MelonLogger.Msg($"[TrackMetaDump] {caller}: metaData not found on instance type={t.FullName}");
                }
                return;
            }

            Type mtype = metaObj.GetType();
            string trackId = TryGetStringMember(metaObj, mtype, "id") ?? TryGetStringMember(metaObj, mtype, "TrackID") ?? TryGetStringMember(metaObj, mtype, "trackId") ?? TryGetStringMember(metaObj, mtype, "trackid") ?? "<unknown>";

            bool isStartingPoint = string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase);

            if (!isStartingPoint && !LogOnce($"TrackMetaDump:{trackId}"))
            {
                return;
            }

            string display = TryGetStringMember(metaObj, mtype, "displayName") ?? TryGetStringMember(metaObj, mtype, "DisplayName") ?? "<unknown>";
            string displayEN = TryGetStringMember(metaObj, mtype, "displayNameEN") ?? "?";
            string bundle = TryGetStringMember(metaObj, mtype, "bundleID") ?? TryGetStringMember(metaObj, mtype, "BundleID") ?? "<none>";
            string episode = TryGetStringMember(metaObj, mtype, "episodeID") ?? TryGetStringMember(metaObj, mtype, "EpisodeID") ?? "<none>";

            MelonLogger.Msg($"[TrackMetaDump] caller={caller} metaType={mtype.FullName} id={trackId} display={display} displayEN={displayEN} bundle={bundle} episode={episode}");

            // Dump member names and (safe) values for debugging ONLY for startingpoint tracks
            if (string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var memberList = new System.Collections.Generic.List<string>();
                    // reuse existing 'flags' declared above
                    // (flags variable already contains BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)

                    int pcount = 0;
                    foreach (PropertyInfo prop in mtype.GetProperties(searchFlags))
                    {
                        if (!prop.CanRead) { continue; }
                        try
                        {
                            object? val = prop.GetValue(metaObj);
                            string sval = val is null ? "<null>" : val.ToString() ?? "<obj>";
                            string writable = prop.CanWrite ? "W" : "R";
                            memberList.Add($"P:{prop.Name}({writable})={sval}");
                        }
                        catch { memberList.Add($"P:{prop.Name}=<error>"); }
                        if (++pcount >= 64) break;
                    }

                    int fcount = 0;
                    foreach (FieldInfo field in mtype.GetFields(searchFlags))
                    {
                        try
                        {
                            object? val = field.GetValue(metaObj);
                            string sval = val is null ? "<null>" : val.ToString() ?? "<obj>";
                            string ro = field.IsInitOnly ? "RO" : "RW";
                            memberList.Add($"F:{field.Name}({ro})={sval}");
                        }
                        catch { memberList.Add($"F:{field.Name}=<error>"); }
                        if (++fcount >= 128) break;
                    }

                    MelonLogger.Msg($"[TrackMetaDump] members ({memberList.Count}): {string.Join(", ", memberList)}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[TrackMetaDump] member dump failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static string? TryGetStringMember(object obj, Type type, string name)
        {
            try
            {
                FieldInfo? f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f is not null && f.FieldType == typeof(string)) return f.GetValue(obj) as string;

                PropertyInfo? p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p is not null && p.CanRead && p.PropertyType == typeof(string)) return p.GetValue(obj) as string;

                MethodInfo? getter = type.GetMethod("get_" + name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getter is not null && getter.ReturnType == typeof(string)) return getter.Invoke(obj, null) as string;
            }
            catch { }
            return null;
        }
    }
}
