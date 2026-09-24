using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 복제 원본(공식 "Starting Point")의 TrackID. 정확히 일치하는 것만 원본으로 쓴다.
        // StartsWith로 비교하면 "Starting Point (yomoha Jazz Arrange)"처럼 같은 접두어를 쓰는 다른 공식곡이
        // 목록에서 먼저 나올 때 그 곡이 복제 원본이 된다(WORKLOG_04 함정 1과 같은 원인).
        private const string StartingPointTrackId = "startingpoint";

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
            return string.Equals(trackId, StartingPointTrackId, StringComparison.OrdinalIgnoreCase);
        }

        // 주입한 커스텀 트랙의 IL2CPP 네이티브 포인터 -> 그 트랙이 어느 앨범(hwa 하위 폴더)에서 왔는지.
        // 커스텀 트랙은 공식 트랙의 복제본이라 TrackID도, 표시명도(사용자가 info.txt로 바꾸므로)
        // 식별 기준이 될 수 없다. 우리가 직접 생성한 객체이므로 포인터 동일성이 유일하게 확실한 기준이고,
        // 앨범이 여러 개이므로 어느 트랙이 어느 폴더인지도 여기서 같이 관리한다.
        private static readonly Dictionary<IntPtr, CustomAlbum> InjectedCustomTrackAlbums = new Dictionary<IntPtr, CustomAlbum>();

        // 복제본은 세션 동안 한 번만 만들고, 목록이 새로 들어올 때마다 같은 객체를 다시 넣는다.
        // 래퍼를 여기 붙잡아 두면 IL2CPP GC 핸들이 살아 있어 네이티브 객체가 해제되지 않으므로,
        // 포인터가 다른 객체에 재사용되어 공식곡을 커스텀으로 오인하는 일이 없다.
        // (예전에는 주입할 때마다 매핑을 비워서, 목록 어딘가에 남은 이전 복제본이 "공식곡"으로 취급됐다.
        //  그 상태로 플레이하면 BMS/자켓/음원이 원본으로 나오고 기록 저장 차단도 풀렸다.)
        private static readonly List<(object Track, CustomAlbum Album)> InjectedCustomTracks = new List<(object Track, CustomAlbum Album)>();

        private static IntPtr TryGetIl2CppPointer(object instance)
        {
            try
            {
                if (instance is Il2CppObjectBase il2CppObject)
                {
                    return il2CppObject.Pointer;
                }
            }
            catch
            {
                // 이미 수집된 객체면 Pointer가 예외를 던진다.
                return IntPtr.Zero;
            }

            object? pointer = TryGetMemberValue(instance, instance.GetType(), "Pointer")
                ?? TryGetMemberValue(instance, instance.GetType(), "m_CachedPtr");
            return pointer is IntPtr ptr ? ptr : IntPtr.Zero;
        }

        private static bool RegisterInjectedCustomTrack(object track, CustomAlbum album)
        {
            IntPtr ptr = TryGetIl2CppPointer(track);
            if (ptr == IntPtr.Zero)
            {
                MelonLogger.Warning($"[TrackSelector.Set] 커스텀 트랙 포인터를 얻지 못해 식별 등록에 실패했습니다: {album.Name}");
                return false;
            }

            InjectedCustomTrackAlbums[ptr] = album;
            InjectedCustomTracks.Add((track, album));
            return true;
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
