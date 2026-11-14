using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // csproj는 게임의 IL2CPP 인터롭 어셈블리(Il2CppStargazer 네임스페이스가 든 Assembly-CSharp.dll,
        // Il2CppMikazuki.dll 등)를 참조하고, JudgeAllPerfectPatch·NoteMotionSupport는 강타입을 쓴다.
        // 하지만 트랙 주입·복제·메타데이터 계열 지원 코드는 문자열 타입명 + 리플렉션으로 짜여 있고 지금도 그렇다.
        // 인터롭 어셈블리는 게임 버전마다 재생성되므로, 이쪽 타입 이름이 바뀌면 빌드가 깨지는 대신
        // 런타임에 "target not found" / "타입을 찾지 못해" 경고로 드러난다.
        // 트랙 선택 화면에 들어갈 때마다 불리므로 PatchTargetSupport의 캐시된 해석기를 같이 쓴다
        // (전 어셈블리 순회는 첫 호출 1회뿐, 네임스페이스 변경 시 짧은 이름 폴백 포함).
        private static Type? FindType(string typeName) => ResolveGameType(typeName);

        private static readonly string[] ArtistLangCodes = { "KR", "EN", "JP", "CN", "TW" };

        private static readonly ConcurrentDictionary<Type, ConstructorInfo?> IntPtrWrapperCtorCache =
            new ConcurrentDictionary<Type, ConstructorInfo?>();

        private static readonly ConcurrentDictionary<Type, MethodInfo?> ArtistDictSetItemCache =
            new ConcurrentDictionary<Type, MethodInfo?>();

        private static readonly ConcurrentDictionary<Type, MemberAccessor> TrackMetaFallbackCache =
            new ConcurrentDictionary<Type, MemberAccessor>();

        // Il2CppObjectBase.Cast<T>()/TryCast<T>()의 리플렉션판. 대상 타입을 문자열 이름으로만 얻기 때문에
        // 제네릭 Cast<T>()를 컴파일 타임에 쓸 수 없다. 그래서 원본 래퍼의 네이티브 포인터를 꺼내
        // 대상 타입의 (IntPtr) 생성자로 다시 감싸는 방식으로 "캐스트"를 직접 구현한다.
        private static object? CastToConcreteTrackData(object track, Type targetType)
        {
            try
            {
                IntPtr ptr = TryGetIl2CppPointer(track);
                if (ptr == IntPtr.Zero)
                {
                    return null;
                }

                ConstructorInfo? ctor = IntPtrWrapperCtorCache.GetOrAdd(targetType, t => t.GetConstructor(new[] { typeof(IntPtr) }));
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
                bool displayNameEnChanged = TrySetValueByNameCandidates(metaObj, new[] { "displaynameen" }, displayName);
                bool composerChanged = TrySetValueByNameCandidates(metaObj, new[] { "composer" }, composer);
                TrySetArtistInfos(metaObj, composer);
                string actualDisplayName = TryGetMemberValue(metaObj, metaObj.GetType(), "displayName")?.ToString()
                                           ?? TryGetMemberValue(metaObj, metaObj.GetType(), "DisplayName")?.ToString()
                                           ?? "<unreadable>";

                if (displayNameChanged || displayNameEnChanged || composerChanged)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 메타데이터 수정: requested={displayName}, actual={actualDisplayName}, displayChanged={displayNameChanged}, composerChanged={composerChanged}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] startingpoint 메타데이터 수정 실패: {ex.Message}");
            }
        }

        private static void TrySetArtistInfos(object metaObj, string artist)
        {
            try
            {
                object? dict = TryGetMemberValue(metaObj, "artistInfos", "ArtistInfos");
                if (dict is null) return;

                MethodInfo? setItem = ArtistDictSetItemCache.GetOrAdd(
                    dict.GetType(),
                    t => t.GetMethod("set_Item", InstanceMemberFlags));
                if (setItem is not null)
                {
                    foreach (string lang in ArtistLangCodes)
                    {
                        setItem.Invoke(dict, new object[] { lang, artist });
                    }
                }
            }
            catch { }
        }

        private static object? GetTrackMetaDataObject(object concreteTrack)
        {
            object? metaObj = TryGetMemberByType(
                concreteTrack,
                "INNER_TrackMetaData",
                "_metaData",
                "metaData",
                "MetaData",
                "m_metaData");
            if (metaObj is not null)
            {
                return metaObj;
            }

            // 이름에 "meta"를 포함하는 멤버가 복수 INNER_TrackMetaData로 인해 모호했을 때의 캐시된 폴백 스캔
            MemberAccessor fallback = TrackMetaFallbackCache.GetOrAdd(concreteTrack.GetType(), ct =>
            {
                foreach (FieldInfo candidate in ct.GetFields(InstanceMemberFlags))
                {
                    if (candidate.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return new MemberAccessor(candidate.Name, null, candidate, null);
                    }
                }

                foreach (PropertyInfo candidate in ct.GetProperties(InstanceMemberFlags))
                {
                    if (candidate.CanRead && candidate.GetIndexParameters().Length == 0
                        && candidate.Name.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return new MemberAccessor(candidate.Name, candidate, null, null);
                    }
                }

                return MemberAccessor.Empty;
            });

            return fallback.IsEmpty ? null : fallback.GetValue(concreteTrack);
        }

    }
}
