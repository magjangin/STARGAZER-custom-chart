using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // 이 프로젝트는 게임의 IL2CPP 인터롭 어셈블리(Il2CppStargazer.dll 등)를 참조하지 않는다
        // (게임 버전마다 재생성되는 산출물이라 컴파일 타임 의존을 피함). 그래서 강타입 참조 대신
        // 문자열 타입명으로 타입을 찾는다 — 정상적인 모드였다면 `typeof(PlayerBase)`로 끝날 일이다.
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

        // Il2CppObjectBase.Cast<T>()/TryCast<T>()의 리플렉션판. 어셈블리를 참조하지 않아 제네릭
        // Cast<T>()를 못 쓰므로, 원본 래퍼의 네이티브 포인터를 꺼내 대상 타입의 (IntPtr) 생성자로
        // 다시 감싸는 방식으로 "캐스트"를 직접 구현한다.
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

        private static void ApplyStartingPointMetadataOverrides(object track, Type concreteTrackType, string displayName, string composer, IReadOnlyDictionary<string, string>? levels = null)
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
                bool levelsChanged = ExperimentChartSettings.EnableTrackLevelOverride
                    && levels is not null && levels.Count > 0
                    && TrySetTrackLevels(metaObj, levels);
                string actualDisplayName = TryGetMemberValue(metaObj, metaObj.GetType(), "displayName")?.ToString()
                                           ?? TryGetMemberValue(metaObj, metaObj.GetType(), "DisplayName")?.ToString()
                                           ?? "<unreadable>";

                if (displayNameChanged || composerChanged || levelsChanged)
                {
                    MelonLogger.Msg($"[TrackSelector.Set] startingpoint 메타데이터 수정: requested={displayName}, actual={actualDisplayName}, displayChanged={displayNameChanged}, composerChanged={composerChanged}, levelsChanged={levelsChanged}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] startingpoint 메타데이터 수정 실패: {ex.Message}");
            }
        }

        // INNER_TrackMetaData.levelString(Dictionary<ELevels, string>)의 인덱서 세터를 리플렉션으로 찾아
        // 키(ELevels enum, 실제 타입은 어셈블리 미참조라 세터 파라미터 타입에서 런타임에 얻는다)별로 값을 채운다.
        // ExperimentChartSettings.EnableTrackLevelOverride가 꺼져 있으면 호출되지 않는다.
        private static bool TrySetTrackLevels(object metaObj, IReadOnlyDictionary<string, string> levels)
        {
            Type metaType = metaObj.GetType();
            object? levelStringObj = TryGetMemberValue(metaObj, metaType, "levelString")
                                     ?? TryGetMemberValue(metaObj, metaType, "LevelString");
            if (levelStringObj is null)
            {
                MelonLogger.Warning("[TrackLevel] levelString 멤버를 찾지 못했습니다.");
                return false;
            }

            // 공유 딕셔너리를 그대로 쓰면 공식 트랙 난이도까지 바뀐다. 반드시 먼저 분리한다.
            object? detached = TryDetachLevelStringDictionary(metaObj, levelStringObj);
            if (detached is null)
            {
                MelonLogger.Warning("[TrackLevel] levelString 분리에 실패해 난이도 설정을 건너뜁니다(공식 트랙 오염 방지).");
                return false;
            }

            levelStringObj = detached;
            Type dictType = levelStringObj.GetType();
            MethodInfo? setItem = dictType.GetMethod("set_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (setItem is null)
            {
                return false;
            }

            Type keyType = setItem.GetParameters()[0].ParameterType;
            bool any = false;
            foreach (KeyValuePair<string, string> kvp in levels)
            {
                try
                {
                    object enumValue = Enum.Parse(keyType, kvp.Key, ignoreCase: true);
                    string? before = TryReadLevelValue(levelStringObj, enumValue);
                    setItem.Invoke(levelStringObj, new object[] { enumValue, kvp.Value });
                    string? after = TryReadLevelValue(levelStringObj, enumValue);
                    MelonLogger.Msg($"[TrackLevel] {kvp.Key}: '{before ?? "<none>"}' -> '{after ?? "<none>"}' (요청={kvp.Value})");
                    any = true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[TrackLevel] 난이도 설정 실패({kvp.Key}): {ex.Message}");
                }
            }

            return any;
        }

        // 클론의 levelString은 CopyFieldsAndProperties가 참조만 복사해서 복제 원본(공식 트랙)과 같은
        // Dictionary 객체다. 같은 키를 모두 새 Dictionary로 옮겨 담고 metaObj에 다시 꽂아 소유권을 분리한다.
        // 실패하면 null을 반환해서 호출부가 쓰기를 포기하도록 한다(공유 딕셔너리에 쓰는 것보다 안전).
        private static object? TryDetachLevelStringDictionary(object metaObj, object sharedDict)
        {
            try
            {
                Type dictType = sharedDict.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo? setItem = dictType.GetMethod("set_Item", flags);
                if (setItem is null)
                {
                    return null;
                }

                object? newDict = InstantiateIl2CppObject(dictType);
                if (newDict is null)
                {
                    return null;
                }

                // Il2Cpp Dictionary는 관리 측 IEnumerable이 아닐 수 있어 열거 대신 enum 키를 전수 조회한다.
                Type keyType = setItem.GetParameters()[0].ParameterType;
                int copied = 0;
                foreach (object key in Enum.GetValues(keyType))
                {
                    string? value = TryReadLevelValue(sharedDict, key);
                    if (value is null)
                    {
                        continue;
                    }

                    setItem.Invoke(newDict, new object[] { key, value });
                    copied++;
                }

                if (!TrySetValueByNameCandidates(metaObj, new[] { "levelstring" }, newDict))
                {
                    MelonLogger.Warning("[TrackLevel] 새 levelString을 메타데이터에 쓰지 못했습니다.");
                    return null;
                }

                MelonLogger.Msg($"[TrackLevel] levelString을 공식 트랙과 분리했습니다(복사한 난이도 {copied}개). shared={DescribePointer(sharedDict)} new={DescribePointer(newDict)}");
                return newDict;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackLevel] levelString 분리 실패: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // LoadPattern 호출 시점에 게임이 실제로 들고 있는 트랙 ID와 해당 난이도 문자열을 남긴다.
        // 로딩이 여기서 멈추면, 마지막으로 찍힌 이 값이 원인 후보다.
        private static void LogPatternLoadDiagnostics(object? trackData, object[] args)
        {
            try
            {
                if (trackData is null)
                {
                    return;
                }

                Type type = trackData.GetType();
                string trackId = TryGetMemberValue(trackData, type, "TrackID")?.ToString() ?? "<unknown>";
                object? levelArg = args.Length > 0 ? args[0] : null;
                string levelName = levelArg?.ToString() ?? "<none>";

                string levelText = "<unreadable>";
                object? metaObj = GetTrackMetaDataObject(trackData);
                if (metaObj is not null)
                {
                    object? dict = TryGetMemberValue(metaObj, metaObj.GetType(), "levelString")
                                   ?? TryGetMemberValue(metaObj, metaObj.GetType(), "LevelString");
                    if (dict is not null && levelArg is not null)
                    {
                        levelText = TryReadLevelValue(dict, levelArg) ?? "<none>";
                        levelText += $" (dict={DescribePointer(dict)})";
                    }
                }

                MelonLogger.Msg($"[PatternLoad] trackId={trackId} level={levelName} levelString={levelText} custom={IsCustomChartTrack(trackData)}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[PatternLoad] 진단 로그 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string? TryReadLevelValue(object dict, object key)
        {
            try
            {
                Type dictType = dict.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo? containsKey = dictType.GetMethod("ContainsKey", flags);
                if (containsKey is not null && containsKey.Invoke(dict, new[] { key }) is bool has && !has)
                {
                    return null;
                }

                MethodInfo? getItem = dictType.GetMethod("get_Item", flags);
                return getItem?.Invoke(dict, new[] { key })?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string? FindCustomTrackInfoFile(string hwaPath)
        {
            string primary = Path.Combine(hwaPath, "info.txt");
            if (File.Exists(primary))
            {
                return primary;
            }

            try
            {
                return Directory.EnumerateFiles(hwaPath, "*.txt").FirstOrDefault();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TrackSelector.Set] hwa 폴더에서 info.txt 탐색 실패: {ex.Message}");
                return null;
            }
        }

        private static object? GetTrackMetaDataObject(object concreteTrack)
        {
            BindingFlags searchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type ct = concreteTrack.GetType();

            // INNER_TrackData._metaData는 auto-property로 선언되어 있어 프로퍼티 조회를 먼저 시도한다.
            PropertyInfo? property = ct.GetProperty("_metaData", searchFlags)
                                     ?? ct.GetProperty("metaData", searchFlags)
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
