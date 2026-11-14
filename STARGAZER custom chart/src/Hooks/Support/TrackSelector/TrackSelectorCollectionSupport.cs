using System;
using System.Collections.Concurrent;
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
            if (instance is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase il2cppObj)
            {
                return il2cppObj.Pointer;
            }

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

        private static readonly ConcurrentDictionary<Type, (MethodInfo? Method, Type? ItemType)> InsertMethodCache =
            new ConcurrentDictionary<Type, (MethodInfo?, Type?)>();

        private static readonly ConcurrentDictionary<(Type Source, Type Target), (bool Direct, MethodInfo? TryCast, ConstructorInfo? IntPtrCtor)> CastStrategyCache =
            new ConcurrentDictionary<(Type, Type), (bool, MethodInfo?, ConstructorInfo?)>();

        private static bool TryInsertAtStart(object tracks, object item)
        {
            try
            {
                (MethodInfo? insertMethod, Type? targetType) = InsertMethodCache.GetOrAdd(tracks.GetType(), listType =>
                {
                    MethodInfo? found = listType
                        .GetMethods(InstanceMemberFlags)
                        .FirstOrDefault(method => string.Equals(method.Name, "Insert", StringComparison.Ordinal)
                            && method.GetParameters().Length == 2
                            && method.GetParameters()[0].ParameterType == typeof(int));
                    return (found, found?.GetParameters()[1].ParameterType);
                });

                if (insertMethod is null || targetType is null)
                {
                    MelonLogger.Warning("[TrackSelector.Set] tracks 컬렉션에서 Insert 메서드를 찾지 못했습니다.");
                    return false;
                }

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

            Type sourceType = obj.GetType();
            var strategy = CastStrategyCache.GetOrAdd((sourceType, targetType), key =>
            {
                if (key.Target.IsAssignableFrom(key.Source))
                {
                    return (true, null, null);
                }

                MethodInfo? closedTryCast = null;
                try
                {
                    MethodInfo? openTryCast = key.Source
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => string.Equals(m.Name, "TryCast", StringComparison.Ordinal)
                            && m.IsGenericMethodDefinition
                            && m.GetParameters().Length == 0);
                    if (openTryCast is not null)
                    {
                        closedTryCast = openTryCast.MakeGenericMethod(key.Target);
                    }
                }
                catch
                {
                }

                ConstructorInfo? intPtrCtor = key.Target.GetConstructor(new[] { typeof(IntPtr) });
                return (false, closedTryCast, intPtrCtor);
            });

            if (strategy.Direct)
            {
                return obj;
            }

            if (strategy.TryCast is not null)
            {
                try
                {
                    object? casted = strategy.TryCast.Invoke(obj, null);
                    if (casted is not null)
                    {
                        return casted;
                    }
                }
                catch
                {
                }
            }

            if (strategy.IntPtrCtor is not null)
            {
                try
                {
                    IntPtr ptr = TryGetIl2CppPointer(obj);
                    if (ptr != IntPtr.Zero)
                    {
                        return strategy.IntPtrCtor.Invoke(new object[] { ptr });
                    }
                }
                catch
                {
                }
            }

            return obj; // Return original if all casting fails
        }
    }
}
