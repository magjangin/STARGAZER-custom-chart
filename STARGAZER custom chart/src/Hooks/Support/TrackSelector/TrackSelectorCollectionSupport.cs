using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // 복제 원본 선택(CloneSourceSelection.Pick)에만 쓴다. 트랙 본체에 ID 프로퍼티가 없으면 메타데이터의 trackid로 폴백한다.
        // 주의: 커스텀 트랙도 공식 Starting Point의 복제본이라 TrackID가 똑같다 — 커스텀 식별에는 IsCustomChartTrack(포인터)만 쓴다.
        private static string? GetTrackIdText(object? track)
        {
            if (track is null)
            {
                return null;
            }

            string? trackId = TryGetMemberValue(track, "TrackID", "TrackId", "trackId", "trackid")?.ToString();
            if (trackId is null)
            {
                object? meta = GetTrackMetaDataObject(track);
                if (meta is not null)
                {
                    trackId = TryGetMemberValue(meta, "trackid", "trackId", "TrackID", "TrackId")?.ToString();
                }
            }

            return trackId;
        }

        // 주입한 커스텀 트랙의 IL2CPP 네이티브 포인터 -> (앨범, 관리 래퍼 참조).
        // 관리 래퍼 참조를 함께 보관해야 GC 수거로 네이티브 핸들이 해제되어 주소가 재사용되는 위험을 막는다.
        private static readonly Dictionary<IntPtr, (CustomAlbum Album, object TrackRef)> InjectedCustomTrackAlbums =
            new Dictionary<IntPtr, (CustomAlbum, object)>();

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

        // 앨범 -> 그 앨범의 복제본(포인터, 관리 래퍼). 앨범마다 복제는 한 번만 하고 이후 Set에서는 재사용한다
        // (CustomTrackInjectionLogic 주석 참고). 예전처럼 Set마다 등록표를 비우고 새로 복제하면, 이전 복제본을
        // 붙잡고 있던 화면/결과 데이터가 그 트랙을 공식 Starting Point로 보게 돼 기록 보호가 빗나갈 수 있었다.
        // 관리 래퍼(Il2CppObjectBase)를 계속 보관하므로 네이티브 객체가 GC로 해제되지 않아 주소 재사용 걱정도 없다.
        // CustomAlbum은 CustomAlbumRegistry가 1회 스캔해 캐시한 인스턴스라 참조 동일성으로 키를 삼는다.
        private static readonly Dictionary<CustomAlbum, (IntPtr Ptr, object TrackRef)> InjectedCloneByAlbum =
            new Dictionary<CustomAlbum, (IntPtr, object)>(ReferenceEqualityComparer.Instance);

        private static bool RegisterInjectedCustomTrack(object track, CustomAlbum album)
        {
            IntPtr ptr = TryGetIl2CppPointer(track);
            if (ptr == IntPtr.Zero)
            {
                MelonLogger.Warning($"[TrackSelector.Set] 커스텀 트랙 포인터를 얻지 못해 식별 등록에 실패했습니다: {album.Name}");
                return false;
            }

            InjectedCustomTrackAlbums[ptr] = (album, track);
            InjectedCloneByAlbum[album] = (ptr, track);
            return true;
        }

        private static bool TryGetInjectedClone(CustomAlbum album, out IntPtr ptr, out object? track)
        {
            if (InjectedCloneByAlbum.TryGetValue(album, out var entry))
            {
                ptr = entry.Ptr;
                track = entry.TrackRef;
                return true;
            }

            ptr = IntPtr.Zero;
            track = null;
            return false;
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

            return InjectedCustomTrackAlbums.TryGetValue(ptr, out var entry) ? entry.Album : null;
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
