using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static Type? FindType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? t = assembly.GetType(typeName, false);
                if (t is not null)
                {
                    return t;
                }
            }
            return null;
        }

        private static object? CastToConcreteTrackData(object track, Type targetType)
        {
            try
            {
                object? ptrObj = TryGetMemberValue(track, track.GetType(), "Pointer")
                                 ?? TryGetMemberValue(track, track.GetType(), "m_CachedPtr");
                if (ptrObj is null || !(ptrObj is IntPtr))
                {
                    return null;
                }

                IntPtr ptr = (IntPtr)ptrObj;
                if (ptr == IntPtr.Zero)
                {
                    return null;
                }

                ConstructorInfo? ctor = targetType.GetConstructor(new[] { typeof(IntPtr) });
                if (ctor is not null)
                {
                    return ctor.Invoke(new object[] { ptr });
                }
            }
            catch (Exception ex)
            {
                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Warning($"[TrackSelector.Set.Dump] CastToConcreteTrackData 실패: {ex.Message}");
                }
            }
            return null;
        }

        private static void ApplyStartingPointMetadataOverrides(object track, Type concreteTrackType, string displayName, string composer)
        {
            try
            {
                object? metaObj = GetTrackMetaDataObject(track);
                if (metaObj is null)
                {
                    object? concreteTrack = CastToConcreteTrackData(track, concreteTrackType);
                    metaObj = concreteTrack is null ? null : GetTrackMetaDataObject(concreteTrack);
                }
                if (metaObj is null)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] startingpoint 메타데이터를 찾지 못했습니다: requested={displayName}");
                    return;
                }

                bool displayNameChanged = TrySetValueByNameCandidates(metaObj, new[] { "displayname" }, displayName);
                bool composerChanged = TrySetValueByNameCandidates(metaObj, new[] { "composer" }, composer);
                string actualDisplayName = TryGetMemberValue(metaObj, metaObj.GetType(), "displayName")?.ToString()
                                           ?? TryGetMemberValue(metaObj, metaObj.GetType(), "DisplayName")?.ToString()
                                           ?? "<unreadable>";

                if (displayNameChanged || composerChanged)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 메타데이터 수정: requested={displayName}, actual={actualDisplayName}, displayChanged={displayNameChanged}, composerChanged={composerChanged}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] startingpoint 메타데이터 수정 실패: {ex.Message}");
            }
        }

        private static object? GetTrackMetaDataObject(object concreteTrack)
        {
            BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type ct = concreteTrack.GetType();

            FieldInfo? field = ct.GetField("_metaData", searchFlags)
                               ?? ct.GetField("metaData", searchFlags)
                               ?? ct.GetField("m_metaData", searchFlags);
            if (field is not null)
            {
                try
                {
                    object? metaObj = field.GetValue(concreteTrack);
                    if (metaObj is not null)
                    {
                        return metaObj;
                    }
                }
                catch
                {
                }
            }

            PropertyInfo? property = ct.GetProperty("metaData", searchFlags)
                                     ?? ct.GetProperty("MetaData", searchFlags);
            if (property is not null && property.CanRead)
            {
                try
                {
                    object? metaObj = property.GetValue(concreteTrack);
                    if (metaObj is not null)
                    {
                        return metaObj;
                    }
                }
                catch
                {
                }
            }

            foreach (FieldInfo candidate in ct.GetFields(searchFlags))
            {
                if (candidate.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                try
                {
                    object? metaObj = candidate.GetValue(concreteTrack);
                    if (metaObj is not null)
                    {
                        return metaObj;
                    }
                }
                catch
                {
                }
            }

            foreach (PropertyInfo candidate in ct.GetProperties(searchFlags))
            {
                if (!candidate.CanRead || candidate.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                try
                {
                    object? metaObj = candidate.GetValue(concreteTrack);
                    if (metaObj is not null)
                    {
                        return metaObj;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

    }
}
