using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static object? CloneTrackData(object sourceTrack, Type trackType, Type metaType)
        {
            try
            {
                object? concreteSourceTrack = CastToConcreteTrackData(sourceTrack, trackType);
                if (concreteSourceTrack is not null)
                {
                    sourceTrack = concreteSourceTrack;
                }

                object? sourceMeta = TryGetMemberValue(sourceTrack, trackType, "_metaData")
                                     ?? TryGetMemberValue(sourceTrack, trackType, "metaData")
                                     ?? TryGetMemberValue(sourceTrack, trackType, "m_metaData")
                                     ?? TryGetMemberValue(sourceTrack, trackType, "MetaData");

                if (sourceMeta is null)
                {
                    MelonLogger.Warning("[TrackSelector.Set] CloneTrackData: source metaData를 찾지 못했습니다.");
                    return null;
                }

                object? newMeta = CloneMetaData(sourceMeta, metaType);
                if (newMeta is null)
                {
                    MelonLogger.Warning("[TrackSelector.Set] CloneTrackData: metaData 복제에 실패했습니다.");
                    return null;
                }

                object? newTrack = CreateTrackDataFromMetaData(trackType, metaType, newMeta);
                if (newTrack is null)
                {
                    return null;
                }

                CopyFieldsAndProperties(sourceTrack, newTrack, skipMetaDataMembers: true);

                object? assignedMeta = TryGetMemberValue(newTrack, trackType, "_metaData")
                                       ?? TryGetMemberValue(newTrack, trackType, "metaData")
                                       ?? TryGetMemberValue(newTrack, trackType, "m_metaData")
                                       ?? TryGetMemberValue(newTrack, trackType, "MetaData");
                if (assignedMeta is null
                    || HaveSameIl2CppIdentity(assignedMeta, sourceMeta)
                    || !HaveSameIl2CppIdentity(assignedMeta, newMeta))
                {
                    MelonLogger.Warning("[TrackSelector.Set] CloneTrackData: 생성자의 metaData 독립 연결 검증에 실패했습니다.");
                    return null;
                }

                MelonLogger.Msg("[TrackSelector.Set] CloneTrackData: INNER_TrackData(metaData) 생성자로 독립 metaData 연결 완료.");
                return newTrack;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] CloneTrackData 실패: {ex.Message}");
                return null;
            }
        }

        private static object? CreateTrackDataFromMetaData(Type trackType, Type metaType, object newMeta)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            ConstructorInfo? constructor = trackType
                .GetConstructors(flags)
                .FirstOrDefault(candidate =>
                {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    return parameters.Length == 1
                        && parameters[0].ParameterType.IsAssignableFrom(metaType);
                });
            if (constructor is null)
            {
                MelonLogger.Warning($"[TrackSelector.Set] INNER_TrackData(metaData) 생성자를 찾지 못했습니다: {trackType.FullName}");
                return null;
            }

            try
            {
                object track = constructor.Invoke(new[] { newMeta });
                MelonLogger.Msg("[TrackSelector.Set] INNER_TrackData(metaData) 생성자로 트랙 생성 완료.");
                return track;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] INNER_TrackData(metaData) 생성 실패: {ex.Message}");
                return null;
            }
        }

        private static bool HaveSameIl2CppIdentity(object left, object right)
        {
            object? leftPointer = TryGetMemberValue(left, left.GetType(), "Pointer")
                                  ?? TryGetMemberValue(left, left.GetType(), "m_CachedPtr");
            object? rightPointer = TryGetMemberValue(right, right.GetType(), "Pointer")
                                   ?? TryGetMemberValue(right, right.GetType(), "m_CachedPtr");
            if (leftPointer is IntPtr leftPtr && rightPointer is IntPtr rightPtr)
            {
                return leftPtr == rightPtr;
            }

            return ReferenceEquals(left, right);
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

        // 새 IL2CPP 래퍼 인스턴스를 만들고(InstantiateIl2CppObject) writable 필드/프로퍼티를
        // 복사하는 방식의 수동 복제. 강타입 생성자/복사 생성자를 쓸 수 없어서(어셈블리 미참조)
        // 리플렉션으로 대신하는 것뿐, 진짜 딥카피가 아니라 "새 래퍼 + 값 복사"임에 유의.
        private static object? CloneIl2CppObject(object source, Type type)
        {
            try
            {
                object? newObj = InstantiateIl2CppObject(type);
                if (newObj is null)
                {
                    MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject: wrapper 생성자 호출 실패: {type.FullName}");
                    return null;
                }

                CopyFieldsAndProperties(source, newObj);
                MelonLogger.Msg($"[TrackSelector.Set] CloneIl2CppObject: wrapper 생성자와 writable 멤버 복사로 복제 완료: {type.FullName}");
                return newObj;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] CloneIl2CppObject 실패: {ex.Message}");
                return null;
            }
        }

        private static void CopyFieldsAndProperties(object source, object target, bool skipMetaDataMembers = false)
        {
            if (source is null || target is null) return;
            Type type = source.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 필드를 복사합니다.
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.IsInitOnly) continue;
                string name = field.Name;
                if (skipMetaDataMembers && NameMatchesAny(name, new[] { "metadata" }))
                {
                    continue;
                }
                if (string.Equals(name, "Pointer", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "m_CachedPtr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "pooledPtr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "isWrapped", StringComparison.OrdinalIgnoreCase) ||
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
                    // 중요하지 않은 복사 오류는 무시합니다.
                }
            }

            // 프로퍼티를 복사합니다.
            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                string name = prop.Name;
                if (skipMetaDataMembers && NameMatchesAny(name, new[] { "metadata" }))
                {
                    continue;
                }
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
                    // 중요하지 않은 복사 오류는 무시합니다.
                }
            }
        }

    }
}
