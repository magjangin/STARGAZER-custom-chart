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
                // 객체 타입에서 일반적인 'TryCast' 또는 'Cast' 메서드를 찾습니다.
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
                // 대체: targetType에 IntPtr를 받는 생성자가 있으면 객체의 Pointer로 인스턴스화합니다.
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
