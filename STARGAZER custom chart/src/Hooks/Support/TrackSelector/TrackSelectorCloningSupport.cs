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
    }
}
