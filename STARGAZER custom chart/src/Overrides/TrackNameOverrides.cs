using System;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private const string StartingPointDisplayName = "Starting Point";
        private const string StartingPointSuffix = " [SP]";

        private static void TrackDisplayNamePostfix(object __instance, ref string __result)
        {
            string trackId = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
            if (LogOnce($"Accessor.TrackDisplayName:{trackId}:{__result}"))
            {
                MelonLogger.Msg($"[Accessor][TrackDisplayName] trackId={trackId} result={__result}");
            }
            try { DumpInnerTrackMetaDataSafe(__instance, "Accessor.TrackDisplayNamePostfix"); } catch { }
            ApplyTrackDisplayNameOverride("TrackDisplayName", __instance, ref __result);
        }

        private static void TrackDisplayNameEnPostfix(object __instance, ref string __result)
        {
            string trackIdEn = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
            if (LogOnce($"Accessor.TrackDisplayNameEN:{trackIdEn}:{__result}"))
            {
                MelonLogger.Msg($"[Accessor][TrackDisplayNameEN] trackId={trackIdEn} result={__result}");
            }
            try { DumpInnerTrackMetaDataSafe(__instance, "Accessor.TrackDisplayNameENPostfix"); } catch { }
            ApplyTrackDisplayNameOverride("TrackDisplayNameEN", __instance, ref __result);
        }

        private static void MetaDisplayNamePostfix(object __instance, ref string __result)
        {
            string trackIdMeta = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
            if (LogOnce($"Accessor.MetaDisplayName:{trackIdMeta}:{__result}"))
            {
                MelonLogger.Msg($"[Accessor][MetaDisplayName] trackId={trackIdMeta} result={__result}");
            }
            ApplyTrackDisplayNameOverride("MetaDisplayName", __instance, ref __result);
        }

        private static void MetaDisplayNameEnPostfix(object __instance, ref string __result)
        {
            string trackIdMetaEn = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
            if (LogOnce($"Accessor.MetaDisplayNameEN:{trackIdMetaEn}:{__result}"))
            {
                MelonLogger.Msg($"[Accessor][MetaDisplayNameEN] trackId={trackIdMetaEn} result={__result}");
            }
            ApplyTrackDisplayNameOverride("MetaDisplayNameEN", __instance, ref __result);
        }

        private static void ArtistDisplayNamePostfix(object __instance, ref string __result)
        {
            if (string.IsNullOrWhiteSpace(__result))
            {
                return;
            }
            string trackId = __instance is null ? "<unknown>" : BuildTrackIdForDebug(__instance);
            string before = __result;
            if (string.Equals(trackId, "startingpoint", StringComparison.OrdinalIgnoreCase))
            {
                __result = "화영왕";
                if (LogOnce($"TrackNameOverride.ArtistDisplayName:{trackId}:{__result}"))
                {
                    MelonLogger.Msg($"[TrackNameOverride][ArtistDisplayName] trackId={trackId} {before} -> {__result}");
                }
            }
            else
            {
                if (LogOnce($"TrackNameOverride.ArtistDisplayName:{trackId}:{__result}"))
                {
                    MelonLogger.Msg($"[TrackNameOverride][ArtistDisplayName] trackId={trackId} artist={__result}");
                }
            }
        }

        private static void TrackIDPostfix(object __instance, ref string __result)
        {
            if (string.IsNullOrWhiteSpace(__result))
            {
                return;
            }

            if (LogOnce($"TrackNameOverride.TrackID:{__result}"))
            {
                MelonLogger.Msg($"[TrackNameOverride][TrackID] trackId={__result}");
            }
        }

        private static void ApplyTrackDisplayNameOverride(string source, object? trackData, ref string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            // Removed automatic appending of Starting Point suffix (was: " [SP]")
            // Preserve existing behavior: do nothing to the displayName here.
            return;
        }

        private static bool IsStartingPointDisplayName(string displayName)
        {
            return string.Equals(displayName.Trim(), StartingPointDisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasStartingPointSuffix(string displayName)
        {
            return displayName.EndsWith(StartingPointSuffix, StringComparison.OrdinalIgnoreCase)
                || displayName.EndsWith(" [sp]", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTrackIdForDebug(object trackData)
        {
            try
            {
                var idObj = TryGetMemberValue(trackData, trackData.GetType(), "TrackID")
                            ?? TryGetMemberValue(trackData, trackData.GetType(), "TrackId")
                            ?? TryGetMemberValue(trackData, trackData.GetType(), "trackId");
                return idObj?.ToString() ?? "<unknown>";
            }
            catch
            {
                return "<error>";
            }
        }

        private static bool TryResolveTrackId(object trackData, out string? id)
        {
            id = null;
            try
            {
                var idObj = TryGetMemberValue(trackData, trackData.GetType(), "TrackID")
                            ?? TryGetMemberValue(trackData, trackData.GetType(), "TrackId")
                            ?? TryGetMemberValue(trackData, trackData.GetType(), "trackId");
                if (idObj is null)
                {
                    return false;
                }

                id = idObj.ToString();
                return id is not null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryForceDisplayNameOnRecordItem(object recordItem, string displayName)
        {
            try
            {
                if (TrySetValueByNameCandidates(recordItem, new[] { "displayName", "DisplayName", "trackDisplayName" }, displayName))
                {
                    return true;
                }

                object? metaObj = TryGetMemberValue(recordItem, recordItem.GetType(), "meta")
                                    ?? TryGetMemberValue(recordItem, recordItem.GetType(), "Meta");
                if (metaObj is not null && TrySetValueByNameCandidates(metaObj, new[] { "displayName", "DisplayName" }, displayName))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
