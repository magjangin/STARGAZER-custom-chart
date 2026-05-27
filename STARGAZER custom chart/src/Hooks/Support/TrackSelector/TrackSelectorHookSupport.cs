using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void HandleTrackSelectorSetTracks(object tracks)
        {
            try
            {
                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Msg("[TrackSelector.Set] TrackSelector의 Set 메서드가 호출되었습니다!");
                }

                int trackCount = TryGetCollectionCount(tracks) ?? 0;
                int enumerateLimit = Math.Max(8, trackCount > 0 ? trackCount : 256);
                var items = EnumerateCollectionItems(tracks, enumerateLimit).ToList();
                if (trackCount <= 0)
                {
                    trackCount = items.Count;
                }

                if (EnableTrackSelectorVerboseLogging)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] 트랙 데이터 리스트: count={trackCount}");
                }

                if (items.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set] 트랙 리스트가 비어 있습니다.");
                    return;
                }

                if (EnableTrackSelectorVerboseLogging)
                {
                    for (int i = 0; i < Math.Min(5, items.Count); i++)
                    {
                        object? track = items[i];
                        if (track is null)
                        {
                            MelonLogger.Msg($"[TrackSelector.Set] - 트랙 {i}: null");
                            continue;
                        }

                        Type concreteType = track.GetType();
                        string displayName = TryGetMemberValue(track, concreteType, "TrackDisplayName")?.ToString() ?? "?";
                        string displayNameEn = TryGetMemberValue(track, concreteType, "TrackDisplayNameEN")?.ToString() ?? "?";
                        MelonLogger.Msg($"[TrackSelector.Set] - 트랙 {i}: type={concreteType.FullName ?? concreteType.Name}, TrackDisplayName={displayName}, TrackDisplayNameEN={displayNameEn}");
                    }
                }

                // Dynamic injection check
                var firstTwo = EnumerateCollectionItems(tracks, 2).ToList();
                bool alreadyInjected = firstTwo.Count >= 2
                    && IsStartingPointTrack(firstTwo[0])
                    && IsStartingPointTrack(firstTwo[1]);

                if (alreadyInjected)
                {
                    if (EnableTrackSelectorVerboseLogging)
                    {
                        MelonLogger.Msg("[TrackSelector.Set] 이미 startingpoint 트랙이 주입되어 있어 추가 주입을 건너뜁니다.");
                    }
                    return;
                }

                object? source = items.FirstOrDefault(IsStartingPointTrack);
                if (source is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set] startingpoint 트랙을 찾지 못했습니다. 복사 삽입을 건너뜁니다.");
                    return;
                }

                // Attempt to clone and insert two copies of the starting point at the beginning
                int applied = 0;
                object? track1 = null;
                object? track2 = null;

                Type? concreteTrackType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                Type? concreteMetaType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackMetaData");

                if (concreteTrackType is not null && concreteMetaType is not null)
                {
                    track1 = CloneTrackData(source, concreteTrackType, concreteMetaType);
                    track2 = CloneTrackData(source, concreteTrackType, concreteMetaType);
                }

                if (track1 is not null && track2 is not null)
                {
                    Type metadataOverrideTrackType = concreteTrackType ?? track1.GetType();
                    ApplyStartingPointMetadataOverrides(track1, metadataOverrideTrackType, "테스트 1", "화영왕");
                    ApplyStartingPointMetadataOverrides(track2, metadataOverrideTrackType, "테스트 2", "화영왕");
                }
                else
                {
                    track1 = source;
                    track2 = source;
                }

                try
                {
                    if (TryInsertAtStart(tracks, track2)) applied++;
                    if (TryInsertAtStart(tracks, track1)) applied++;
                }
                catch { }

                if (applied > 0)
                {
                    int updatedCount = TryGetCollectionCount(tracks) ?? (trackCount + applied);
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 트랙을 복사 주입했습니다! 적용={applied} 현재 트랙 수: {updatedCount}");
                    if (EnableTrackSelectorMetadataDump)
                    {
                        DumpInjectedTracksMetadata(tracks);
                    }
                }
                else
                {
                    MelonLogger.Warning("[TrackSelector.Set] startingpoint 트랙 Insert 호출에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] 처리 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

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
                object? concreteTrack = CastToConcreteTrackData(track, concreteTrackType);
                if (concreteTrack is null)
                {
                    return;
                }

                object? metaObj = GetTrackMetaDataObject(concreteTrack);
                if (metaObj is null)
                {
                    return;
                }

                bool displayNameChanged = TrySetValueByNameCandidates(metaObj, new[] { "displayname" }, displayName);
                bool composerChanged = TrySetValueByNameCandidates(metaObj, new[] { "composer" }, composer);

                if (displayNameChanged || composerChanged)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 메타데이터 수정: displayName={(displayNameChanged ? displayName : "<skip>")}, composer={(composerChanged ? composer : "<skip>")}");
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

        [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr il2cpp_object_clone(IntPtr obj);

        [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr il2cpp_class_get_field_from_name(IntPtr klass, string name);

        [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl)]
        private static extern int il2cpp_field_get_offset(IntPtr field);

        private static object? CloneTrackData(object sourceTrack, Type trackType, Type metaType)
        {
            try
            {
                object? concreteSourceTrack = CastToConcreteTrackData(sourceTrack, trackType);
                if (concreteSourceTrack is not null)
                {
                    sourceTrack = concreteSourceTrack;
                }

                // Get the unmanaged pointer of sourceTrack
                object? sourceTrackPtrObj = TryGetMemberValue(sourceTrack, sourceTrack.GetType(), "Pointer")
                                           ?? TryGetMemberValue(sourceTrack, sourceTrack.GetType(), "m_CachedPtr");
                if (sourceTrackPtrObj is not IntPtr sourceTrackPtr || sourceTrackPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[TrackSelector.Set] CloneTrackData: sourceTrackPtr를 찾지 못했습니다.");
                    return null;
                }

                // 1. Clone TrackData natively
                object? newTrack = CloneIl2CppObject(sourceTrack, trackType);
                if (newTrack is null)
                {
                    return null;
                }

                object? newTrackPtrObj = TryGetMemberValue(newTrack, newTrack.GetType(), "Pointer")
                                         ?? TryGetMemberValue(newTrack, newTrack.GetType(), "m_CachedPtr");
                if (newTrackPtrObj is not IntPtr newTrackPtr || newTrackPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[TrackSelector.Set] CloneTrackData: newTrackPtr를 찾지 못했습니다.");
                    return newTrack;
                }

                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                // Get original metaData field
                FieldInfo? metaField = trackType.GetField("_metaData", flags)
                                      ?? trackType.GetField("metaData", flags)
                                      ?? trackType.GetField("m_metaData", flags);

                object? sourceMeta = null;
                if (metaField is not null)
                {
                    sourceMeta = metaField.GetValue(sourceTrack);
                }

                // If we couldn't find sourceMeta via field, let's try to get it by property/reflection
                if (sourceMeta is null)
                {
                    sourceMeta = TryGetMemberValue(sourceTrack, trackType, "metaData")
                                 ?? TryGetMemberValue(sourceTrack, trackType, "_metaData")
                                 ?? TryGetMemberValue(sourceTrack, trackType, "m_metaData")
                                 ?? TryGetMemberValue(sourceTrack, trackType, "MetaData");
                }

                if (sourceMeta is not null)
                {
                    object? sourceMetaPtrObj = TryGetMemberValue(sourceMeta, sourceMeta.GetType(), "Pointer")
                                               ?? TryGetMemberValue(sourceMeta, sourceMeta.GetType(), "m_CachedPtr");

                    if (sourceMetaPtrObj is IntPtr sourceMetaPtr && sourceMetaPtr != IntPtr.Zero)
                    {
                        object? newMeta = CloneMetaData(sourceMeta, metaType);
                        if (newMeta is not null)
                        {
                            object? newMetaPtrObj = TryGetMemberValue(newMeta, newMeta.GetType(), "Pointer")
                                                     ?? TryGetMemberValue(newMeta, newMeta.GetType(), "m_CachedPtr");

                            if (newMetaPtrObj is IntPtr newMetaPtr && newMetaPtr != IntPtr.Zero)
                            {
                                // Step A: Try to find offset using official IL2CPP metadata APIs
                                int offset = -1;
                                IntPtr classPtr = GetIl2CppClassPointer(trackType, sourceTrack);
                                if (classPtr != IntPtr.Zero)
                                {
                                    string[] fieldCandidates = new[] { "_metaData", "metaData", "m_metaData", "meta" };
                                    foreach (string name in fieldCandidates)
                                    {
                                        try
                                        {
                                            IntPtr fieldPtr = il2cpp_class_get_field_from_name(classPtr, name);
                                            if (fieldPtr != IntPtr.Zero)
                                            {
                                                int off = il2cpp_field_get_offset(fieldPtr);
                                                if (off > 0)
                                                {
                                                    offset = off;
                                                    MelonLogger.Msg($"[TrackSelector.Set] CloneTrackData: IL2CPP API를 통해 {name} 필드 오프셋({offset})을 찾았습니다.");
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }

                                // Step B: Fallback to memory scanning if metadata API lookup failed
                                if (offset == -1)
                                {
                                    MelonLogger.Msg("[TrackSelector.Set] CloneTrackData: IL2CPP API로 오프셋을 찾지 못하여 메모리 스캔을 시작합니다...");
                                    for (int i = 8; i <= 128; i += 8)
                                    {
                                        try
                                        {
                                            IntPtr val = Marshal.ReadIntPtr(sourceTrackPtr, i);
                                            if (val == sourceMetaPtr)
                                            {
                                                offset = i;
                                                MelonLogger.Msg($"[TrackSelector.Set] CloneTrackData: 메모리 스캔을 통해 오프셋 {offset}에서 sourceMetaPtr를 찾았습니다.");
                                                break;
                                            }
                                        }
                                        catch { }
                                    }
                                }

                                // Step C: Inject the cloned metadata unmanaged pointer
                                if (offset != -1)
                                {
                                    Marshal.WriteIntPtr(newTrackPtr, offset, newMetaPtr);
                                    MelonLogger.Msg($"[TrackSelector.Set] CloneTrackData: 네이티브 metaData 포인터 교체 완료 (offset={offset})");
                                }
                                else
                                {
                                    MelonLogger.Warning("[TrackSelector.Set] CloneTrackData: sourceTrackPtr에서 sourceMetaPtr 오프셋을 찾지 못했습니다.");
                                }

                                // Step D: Keep the C# wrapper field in sync
                                if (metaField is not null)
                                {
                                    try
                                    {
                                        metaField.SetValue(newTrack, newMeta);
                                        MelonLogger.Msg("[TrackSelector.Set] CloneTrackData: C# wrapper의 _metaData 필드를 성공적으로 캐싱했습니다.");
                                    }
                                    catch (Exception ex)
                                    {
                                        MelonLogger.Warning($"[TrackSelector.Set] CloneTrackData: C# wrapper 필드 캐싱 실패: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                return newTrack;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] CloneTrackData 실패: {ex.Message}");
                return null;
            }
        }

        private static object? CloneMetaData(object sourceMeta, Type metaType)
        {
            object? newMeta = CloneIl2CppObject(sourceMeta, metaType);
            if (newMeta is null)
            {
                throw new InvalidOperationException($"Default constructor not found or failed allocation for type {metaType.FullName}");
            }
            return newMeta;
        }

        private static object? CloneIl2CppObject(object source, Type type)
        {
            try
            {
                // Try native cloning first!
                object? sourcePtrObj = TryGetMemberValue(source, source.GetType(), "Pointer")
                                       ?? TryGetMemberValue(source, source.GetType(), "m_CachedPtr");
                if (sourcePtrObj is IntPtr sourcePtr && sourcePtr != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr clonedPtr = il2cpp_object_clone(sourcePtr);
                        if (clonedPtr != IntPtr.Zero)
                        {
                            ConstructorInfo? clonedCtor = type.GetConstructor(new[] { typeof(IntPtr) });
                            if (clonedCtor is not null)
                            {
                                object clonedObj = clonedCtor.Invoke(new object[] { clonedPtr });
                                MelonLogger.Msg($"[TrackSelector.Set] CloneIl2CppObject: il2cpp_object_clone을 사용하여 네이티브 복제 완료: {type.FullName}");
                                return clonedObj;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is EntryPointNotFoundException || ex is DllNotFoundException)
                        {
                            if (EnableTrackSelectorVerboseLogging)
                            {
                                MelonLogger.Msg($"[TrackSelector.Set] CloneIl2CppObject: il2cpp_object_clone을 사용할 수 없어 object_new fallback으로 전환합니다: {type.FullName}");
                            }
                        }
                        else
                        {
                            MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject: il2cpp_object_clone 실패, fallback 실행: {ex.Message}");
                        }
                    }
                }

                // Fallback: il2cpp_object_new + CopyFieldsAndProperties
                // 1. Get the native Il2CppClass pointer
                IntPtr classPtr = GetIl2CppClassPointer(type, source);
                if (classPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject: Class Pointer를 찾지 못했습니다: {type.FullName}");
                    return null;
                }

                // 2. Resolve and call il2cpp_object_new
                MethodInfo? objectNewMethod = FindIl2CppObjectNew();
                if (objectNewMethod is null)
                {
                    MelonLogger.Warning("[TrackSelector.Set] CloneIl2CppObject: il2cpp_object_new 메서드를 찾지 못했습니다.");
                    return null;
                }

                object? ptrVal = objectNewMethod.Invoke(null, new object[] { classPtr });
                if (ptrVal is null || !(ptrVal is IntPtr))
                {
                    MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject: il2cpp_object_new가 null 또는 유효하지 않은 값을 리턴했습니다: {type.FullName}");
                    return null;
                }

                IntPtr nativeObjPtr = (IntPtr)ptrVal;
                if (nativeObjPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject: il2cpp_object_new가 Zero 포인터를 리턴했습니다: {type.FullName}");
                    return null;
                }

                // 3. Instantiate C# wrapper using the IntPtr constructor
                ConstructorInfo? ctor = type.GetConstructor(new[] { typeof(IntPtr) });
                if (ctor is null)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject: IntPtr 생성자를 찾지 못했습니다: {type.FullName}");
                    return null;
                }

                object newObj = ctor.Invoke(new object[] { nativeObjPtr });

                // 4. Copy fields and properties from source to new object
                CopyFieldsAndProperties(source, newObj);
                return newObj;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject 실패: {ex.Message}");
                return null;
            }
        }

        private static void CopyFieldsAndProperties(object source, object target)
        {
            if (source is null || target is null) return;
            Type type = source.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Copy fields
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.IsInitOnly && field.Name != "isWrapped") continue;
                string name = field.Name;
                if (string.Equals(name, "Pointer", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "m_CachedPtr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "pooledPtr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "ObjectClass", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    object? val = field.GetValue(source);
                    field.SetValue(target, val);
                }
                catch
                {
                    // Ignore non-critical copy errors
                }
            }

            // Copy properties
            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                string name = prop.Name;
                if (string.Equals(name, "Pointer", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "m_CachedPtr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "pooledPtr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "ObjectClass", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    object? val = prop.GetValue(source);
                    prop.SetValue(target, val);
                }
                catch
                {
                    // Ignore non-critical copy errors
                }
            }
        }

        private static IntPtr GetIl2CppClassPointer(Type type, object? sourceInstance)
        {
            if (sourceInstance is not null)
            {
                try
                {
                    BindingFlags instFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    PropertyInfo? objClassProp = sourceInstance.GetType().GetProperty("ObjectClass", instFlags)
                                                 ?? sourceInstance.GetType().GetProperty("Il2CppClassPointer", instFlags);
                    if (objClassProp is not null)
                    {
                        object? val = objClassProp.GetValue(sourceInstance);
                        if (val is IntPtr ptr && ptr != IntPtr.Zero)
                        {
                            return ptr;
                        }
                        if (val is long lVal && lVal != 0)
                        {
                            return new IntPtr(lVal);
                        }
                        if (val is ulong ulVal && ulVal != 0)
                        {
                            return new IntPtr((long)ulVal);
                        }
                    }

                    FieldInfo? objClassField = sourceInstance.GetType().GetField("ObjectClass", instFlags)
                                               ?? sourceInstance.GetType().GetField("Il2CppClassPointer", instFlags);
                    if (objClassField is not null)
                    {
                        object? val = objClassField.GetValue(sourceInstance);
                        if (val is IntPtr ptr && ptr != IntPtr.Zero)
                        {
                            return ptr;
                        }
                        if (val is long lVal && lVal != 0)
                        {
                            return new IntPtr(lVal);
                        }
                        if (val is ulong ulVal && ulVal != 0)
                        {
                            return new IntPtr((long)ulVal);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] sourceInstance로부터 ObjectClass 추출 실패: {ex.Message}");
                }
            }

            // Fallback: Static fields
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? field = type.GetField("NativeClassPtr", flags)
                              ?? type.GetField("Il2CppClassPointer", flags)
                              ?? type.GetField("ClassPointer", flags);
            if (field is not null)
            {
                object? val = field.GetValue(null);
                if (val is IntPtr ptr)
                {
                    return ptr;
                }
            }

            PropertyInfo? prop = type.GetProperty("NativeClassPtr", flags)
                                 ?? type.GetProperty("Il2CppClassPointer", flags);
            if (prop is not null)
            {
                object? val = prop.GetValue(null);
                if (val is IntPtr ptr)
                {
                    return ptr;
                }
            }

            return IntPtr.Zero;
        }

        private static MethodInfo? FindIl2CppObjectNew()
        {
            string[] classNames = {
                "Il2CppInterop.Runtime.IL2CPP",
                "UnhollowerBaseLib.IL2CPP",
                "MelonLoader.IL2CPP"
            };

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string className in classNames)
                {
                    Type? t = assembly.GetType(className, false);
                    if (t is not null)
                    {
                        MethodInfo? method = t.GetMethod("il2cpp_object_new", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IntPtr) }, null);
                        if (method is not null)
                        {
                            return method;
                        }
                    }
                }
            }
            return null;
        }

        private static void SetTrackId(object track, string trackId)
        {
            try
            {
                Type ct = track.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Also set on the INNER_TrackData wrapper itself if writable
                PropertyInfo? wrapperIdProp = ct.GetProperty("TrackID", flags)
                                              ?? ct.GetProperty("TrackId", flags)
                                              ?? ct.GetProperty("trackId", flags);
                if (wrapperIdProp is not null && wrapperIdProp.CanWrite)
                {
                    wrapperIdProp.SetValue(track, trackId);
                }
                else
                {
                    FieldInfo? wrapperIdField = ct.GetField("_trackId", flags)
                                                ?? ct.GetField("trackId", flags)
                                                ?? ct.GetField("m_trackId", flags)
                                                ?? ct.GetField("TrackID", flags);
                    if (wrapperIdField is not null)
                    {
                        wrapperIdField.SetValue(track, trackId);
                    }
                }

                // Set on the inner metaData object
                FieldInfo? metaField = ct.GetField("_metaData", flags)
                                      ?? ct.GetField("metaData", flags)
                                      ?? ct.GetField("m_metaData", flags);

                if (metaField is not null)
                {
                    object? metaObj = metaField.GetValue(track);
                    if (metaObj is not null)
                    {
                        Type mt = metaObj.GetType();
                        PropertyInfo? idProp = mt.GetProperty("trackid", flags)
                                               ?? mt.GetProperty("trackId", flags)
                                               ?? mt.GetProperty("TrackID", flags);
                        if (idProp is not null && idProp.CanWrite)
                        {
                            idProp.SetValue(metaObj, trackId);
                            MelonLogger.Msg($"[TrackSelector.Set] trackid 설정 성공: {trackId}");
                        }
                        else
                        {
                            FieldInfo? idField = mt.GetField("trackid", flags)
                                                 ?? mt.GetField("trackId", flags)
                                                 ?? mt.GetField("TrackID", flags);
                            if (idField is not null)
                            {
                                idField.SetValue(metaObj, trackId);
                                MelonLogger.Msg($"[TrackSelector.Set] trackid 설정 성공(필드): {trackId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] SetTrackId 실패: {ex.Message}");
            }
        }

        private static void DumpInjectedTracksMetadata(object tracks)
        {
            try
            {
                var items = EnumerateCollectionItems(tracks, 2).ToList();
                if (items.Count == 0)
                {
                    MelonLogger.Msg("[TrackSelector.Set.Dump] 주입된 트랙을 찾지 못했습니다.");
                    return;
                }

                Type? concreteTrackType = FindType("Il2CppStargazer.TrackLoader+INNER_TrackData");
                if (concreteTrackType is null)
                {
                    MelonLogger.Msg("[TrackSelector.Set.Dump] Il2CppStargazer.TrackLoader+INNER_TrackData 타입을 로드하지 못했습니다.");
                }

                for (int i = 0; i < items.Count; i++)
                {
                    object? track = items[i];
                    if (track is null)
                    {
                        MelonLogger.Msg($"[TrackSelector.Set.Dump] 주입된 트랙 [{i}]은 null입니다.");
                        continue;
                    }

                    MelonLogger.Msg($"\n[TrackSelector.Set.Dump] ==================== 주입된 트랙 [{i}] 정보 덤프 ====================");
                    Type t = track.GetType();
                    string trackId = TryGetMemberValue(track, t, "TrackID")?.ToString()
                                     ?? TryGetMemberValue(track, t, "TrackId")?.ToString()
                                     ?? TryGetMemberValue(track, t, "trackId")?.ToString() ?? "<unknown>";
                    string displayName = TryGetMemberValue(track, t, "TrackDisplayName")?.ToString() ?? "?";
                    string displayNameEn = TryGetMemberValue(track, t, "TrackDisplayNameEN")?.ToString() ?? "?";
                    string artistName = TryGetMemberValue(track, t, "ArtistDisplayName")?.ToString() ?? "?";

                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - Original Wrapper Type: {t.FullName}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - TrackID: {trackId}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - TrackDisplayName: {displayName}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - TrackDisplayNameEN: {displayNameEn}");
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] - ArtistDisplayName: {artistName}");

                    // Try to cast to INNER_TrackData
                    object? concreteTrack = null;
                    if (concreteTrackType is not null)
                    {
                        concreteTrack = CastToConcreteTrackData(track, concreteTrackType);
                    }

                    if (concreteTrack is not null)
                    {
                        Type ct = concreteTrack.GetType();
                        MelonLogger.Msg($"[TrackSelector.Set.Dump] - Concrete Wrapper Type: {ct.FullName}");

                        // Extract and dump the inner metaData (INNER_TrackMetaData)
                        BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        FieldInfo? f = ct.GetField("_metaData", searchFlags)
                                      ?? ct.GetField("metaData", searchFlags)
                                      ?? ct.GetField("m_metaData", searchFlags);

                        object? metaObj = null;
                        if (f is not null)
                        {
                            metaObj = f.GetValue(concreteTrack);
                        }
                        else
                        {
                            PropertyInfo? p = ct.GetProperty("metaData", searchFlags)
                                               ?? ct.GetProperty("MetaData", searchFlags);
                            if (p is not null && p.CanRead)
                            {
                                metaObj = p.GetValue(concreteTrack);
                            }
                        }

                        // Fallback to find any field/property with "meta" in its name
                        if (metaObj is null)
                        {
                            foreach (FieldInfo ff in ct.GetFields(searchFlags))
                            {
                                if (ff.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    try
                                    {
                                        metaObj = ff.GetValue(concreteTrack);
                                        if (metaObj is not null) break;
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (metaObj is null)
                        {
                            foreach (PropertyInfo pp in ct.GetProperties(searchFlags))
                            {
                                if (pp.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0 && pp.CanRead)
                                {
                                    try
                                    {
                                        metaObj = pp.GetValue(concreteTrack);
                                        if (metaObj is not null) break;
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (metaObj is not null)
                        {
                            Type mtype = metaObj.GetType();
                            MelonLogger.Msg($"[TrackSelector.Set.Dump] - Meta Object Type: {mtype.FullName}");

                            var memberList = new System.Collections.Generic.List<string>();

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

                            MelonLogger.Msg($"[TrackSelector.Set.Dump] - INNER_TrackMetaData members:\n  {string.Join("\n  ", memberList)}");
                        }
                        else
                        {
                            MelonLogger.Msg("[TrackSelector.Set.Dump] - 내부에 INNER_TrackMetaData를 찾지 못했습니다.");
                        }
                    }
                    else
                    {
                        MelonLogger.Msg("[TrackSelector.Set.Dump] - INNER_TrackData로 캐스팅할 수 없습니다.");
                    }
                    MelonLogger.Msg($"[TrackSelector.Set.Dump] ================================================================\n");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set.Dump] 메타데이터 덤프 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool IsStartingPointTrack(object? track)
        {
            if (track is null)
            {
                return false;
            }

            Type type = track.GetType();
            string? trackId = TryGetMemberValue(track, type, "TrackID")?.ToString()
                ?? TryGetMemberValue(track, type, "TrackId")?.ToString()
                ?? TryGetMemberValue(track, type, "trackId")?.ToString();
            if (trackId is null) return false;

            return trackId.StartsWith("startingpoint", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryInsertAtStart(object tracks, object item)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                MethodInfo? insertMethod = tracks
                    .GetType()
                    .GetMethods(Flags)
                    .FirstOrDefault(method => string.Equals(method.Name, "Insert", StringComparison.Ordinal)
                        && method.GetParameters().Length == 2
                        && method.GetParameters()[0].ParameterType == typeof(int));
                if (insertMethod is null)
                {
                    MelonLogger.Warning("[TrackSelector.Set] tracks 컬렉션에서 Insert 메서드를 찾지 못했습니다.");
                    return false;
                }

                Type targetType = insertMethod.GetParameters()[1].ParameterType;
                object? castedItem = CastToType(item, targetType);
                if (castedItem is null)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] item을 {targetType.FullName} 타입으로 변환할 수 없어 원본을 사용합니다.");
                    castedItem = item;
                }

                insertMethod.Invoke(tracks, new[] { (object)0, castedItem });
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] TryInsertAtStart 예외 발생: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException is not null)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] TryInsertAtStart InnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                return false;
            }
        }

        private static object? CastToType(object obj, Type targetType)
        {
            if (obj is null) return null;
            if (targetType.IsAssignableFrom(obj.GetType()))
            {
                return obj;
            }

            try
            {
                // Try to find the generic "TryCast" or "Cast" method on the object's type
                MethodInfo? tryCastMethod = obj.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => string.Equals(m.Name, "TryCast", StringComparison.Ordinal)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 0);

                if (tryCastMethod is not null)
                {
                    MethodInfo genericMethod = tryCastMethod.MakeGenericMethod(targetType);
                    object? casted = genericMethod.Invoke(obj, null);
                    if (casted is not null)
                    {
                        return casted;
                    }
                }
            }
            catch { }

            try
            {
                // Fallback: If targetType has a constructor that accepts IntPtr, instantiate it using the object's Pointer
                object? ptrObj = TryGetMemberValue(obj, obj.GetType(), "Pointer")
                                 ?? TryGetMemberValue(obj, obj.GetType(), "m_CachedPtr");
                if (ptrObj is IntPtr ptr && ptr != IntPtr.Zero)
                {
                    ConstructorInfo? ctor = targetType.GetConstructor(new[] { typeof(IntPtr) });
                    if (ctor is not null)
                    {
                        return ctor.Invoke(new object[] { ptr });
                    }
                }
            }
            catch { }

            return obj; // Return original if all casting fails
        }
    }
}
