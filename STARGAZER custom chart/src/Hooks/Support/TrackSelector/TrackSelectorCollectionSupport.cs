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
        // 복제 원본(공식 "Starting Point")을 찾는 용도로만 쓴다.
        // 주의: 우리가 주입한 커스텀 트랙도 이 원본의 복제본이라 TrackID가 똑같으므로,
        // 이 함수로는 커스텀 트랙과 공식 트랙을 구분할 수 없다 — 구분에는 IsCustomChartTrack을 써야 한다.
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

        // 주입한 커스텀 트랙의 IL2CPP 네이티브 포인터 -> 그 트랙이 어느 앨범(hwa 하위 폴더)에서 왔는지.
        // 커스텀 트랙은 공식 트랙의 복제본이라 TrackID도, 표시명도(사용자가 info.txt로 바꾸므로)
        // 식별 기준이 될 수 없다. 우리가 직접 생성한 객체이므로 포인터 동일성이 유일하게 확실한 기준이고,
        // 앨범이 여러 개이므로 어느 트랙이 어느 폴더인지도 여기서 같이 관리한다.
        private static readonly Dictionary<IntPtr, CustomAlbum> InjectedCustomTrackAlbums = new Dictionary<IntPtr, CustomAlbum>();

        private static IntPtr TryGetIl2CppPointer(object instance)
        {
            object? pointer = TryGetMemberValue(instance, instance.GetType(), "Pointer")
                ?? TryGetMemberValue(instance, instance.GetType(), "m_CachedPtr");
            return pointer is IntPtr ptr ? ptr : IntPtr.Zero;
        }

        // 새로 주입할 때마다 이전 목록을 버린다. 오래된 포인터를 남겨두면 해제된 주소가 재사용될 때
        // 엉뚱한 트랙을 커스텀으로 오인할 수 있다.
        private static void ResetInjectedCustomTracks()
        {
            InjectedCustomTrackAlbums.Clear();
        }

        private static void RegisterInjectedCustomTrack(object track, CustomAlbum album)
        {
            IntPtr ptr = TryGetIl2CppPointer(track);
            if (ptr == IntPtr.Zero)
            {
                MelonLogger.Warning($"[TrackSelector.Set] 커스텀 트랙 포인터를 얻지 못해 식별 등록에 실패했습니다: {album.Name}");
                return;
            }

            InjectedCustomTrackAlbums[ptr] = album;
        }

        private static bool IsCustomChartTrack(object? track) => TryGetAlbumForTrack(track) is not null;

        // 트랙 객체 -> 앨범. 자켓/BGM/차트/난이도 등 모든 커스텀 처리는 이걸로 어느 폴더를 쓸지 정한다.
        private static CustomAlbum? TryGetAlbumForTrack(object? track)
        {
            if (track is null || InjectedCustomTrackAlbums.Count == 0)
            {
                return null;
            }

            IntPtr ptr = TryGetIl2CppPointer(track);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            return InjectedCustomTrackAlbums.TryGetValue(ptr, out CustomAlbum? album) ? album : null;
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
