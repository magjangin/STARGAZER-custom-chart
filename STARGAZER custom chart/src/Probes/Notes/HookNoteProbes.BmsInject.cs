using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class StargazerCustomChartMod
    {
        // BMS 채널 -> 레인 순번 매핑. Layer.Lanes[0..3]는 실측으로 확인된 (위, 아래, 왼쪽, 오른쪽) 물리 배치다.
        // 16=위(고정), 나머지는 16 기준 시계방향(위->오른쪽->아래->왼쪽)으로 11,12,13을 오름차순 배정한다.
        // 실제 LaneUID 문자열은 곡마다 다를 수 있어 하드코딩하지 않고, 런타임에 Layer.Lanes에서 순서대로 뽑는다.
        private static readonly int[] BmsChannelLaneOrder = { 16, 12, 13, 11 };

        // TrySetValueByNameCandidates 후보 목록. 마디/노트마다 배열을 새로 만들지 않도록 정적으로 둔다.
        private static readonly string[] AreaBpmNames = { "areabpm" };
        private static readonly string[] BeatIndexNames = { "beatindex" };
        private static readonly string[] BeatInfoNames = { "beatinfo" };
        private static readonly string[] BeatSplitNames = { "beatsplit" };
        private static readonly string[] ExpressionHolderNames = { "expressionholder" };
        private static readonly string[] LengthNames = { "length" };
        private static readonly string[] LinkedNames = { "linked" };
        private static readonly string[] OwnerNames = { "owner" };
        private static readonly string[] NotePropertyNames = { "property", "noteproperty" };
        private static readonly string[] TargetLaneUidNames = { "targetlaneuid" };
        private static readonly string[] TargetLayerNames = { "targetlayer" };

        // templateArea/templateNote는 위치 정보용이 아니라, Area/Note/BeatInfo/NoteProperty의 실제 IL2CPP 타입과
        // length 및 NoteProperty.expressionHolder 샘플 값을 얻기 위한 템플릿으로만 쓰인다.
        private static void TryInjectBmsChart(object layer, object areasValue, object templateArea, object templateNote)
        {
            try
            {
                // 방어적 재확인: Layer.Areas를 통째로 지우는 파괴적 작업이라, 호출부 가드가 뚫려도
                // 여기서 한 번 더 커스텀 트랙 여부를 확인하고 공식곡이면 절대 실행하지 않는다.
                CustomAlbum? album = CurrentPlayAlbum;
                if (!IsCustomChartPlayActive || album is null)
                {
                    return;
                }

                string[] laneUids = ResolveLaneUidsInOrder(layer, BmsChannelLaneOrder.Length);
                if (laneUids.Length == 0)
                {
                    MelonLogger.Warning("[BmsInject] Layer에서 레인 UID를 하나도 못 찾았습니다.");
                    return;
                }

                string? bmsPath = album.GetBmsPath(CustomChartPlayLevelKey);
                if (bmsPath is null)
                {
                    MelonLogger.Msg($"[BmsInject] .bms 파일이 없어 건너뜁니다: {album.Name} (level={CustomChartPlayLevelKey ?? "<none>"})");
                    return;
                }

                BmsChart? chart = BmsChart.TryGetOrParse(bmsPath);
                if (chart is null || chart.Measures.Count == 0)
                {
                    MelonLogger.Warning($"[BmsInject] BMS 파싱 실패 또는 노트 없음: {bmsPath}");
                    return;
                }

                foreach (string warning in chart.Warnings)
                {
                    if (LogOnce($"Bms.Warn.{bmsPath}.{warning}"))
                    {
                        MelonLogger.Warning($"[Bms] ({Path.GetFileName(bmsPath)}) {warning}");
                    }
                }

                object? templateLength = TryGetMemberByType(templateArea, "BeatInfo", "length", "Length");
                object? templateBeatInfo = TryGetMemberByType(templateNote, "BeatInfo", "beatInfo", "BeatInfo", "beat");
                Type noteType = templateNote.GetType();
                Type? beatInfoType = templateBeatInfo?.GetType();
                if (templateLength is null || beatInfoType is null)
                {
                    MelonLogger.Warning("[BmsInject] 템플릿의 length/BeatInfo 타입을 찾지 못했습니다.");
                    return;
                }

                CollectionAccessor areasAccessor = GetCollectionAccessor(areasValue.GetType());
                MethodInfo? areasAddMethod = areasAccessor.AddMethod;
                if (areasAccessor.ClearMethod is null || areasAddMethod is null)
                {
                    MelonLogger.Warning("[BmsInject] Layer.Areas의 Clear/Add 메서드를 찾지 못해 원본 차트를 유지합니다.");
                    return;
                }

                int notesCreated = 0, notesSkippedChannel = 0;
                int holdStarts = 0, holdEnds = 0;
                var skippedChannels = new HashSet<int>();

                string soundKinds = chart.SoundKinds.Count == 0
                    ? "<none>"
                    : string.Join(", ", chart.SoundKinds.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                MelonLogger.Msg($"[BmsInject] 사운드 ID 분류: {soundKinds}");

                // 루프 진입 전 타입별 리플렉션 메타를 캐시해 노트 수천 개 생성 시 병목을 제거한다.
                Type areaType = templateArea.GetType();
                Type layerType = layer.GetType();
                AreaInjectorMetadata areaMeta = GetOrBuildAreaMeta(areaType, layerType);
                BeatInfoInjectorMetadata beatInfoMeta = GetOrBuildBeatInfoMeta(beatInfoType);
                NoteInjectorMetadata noteMeta = GetOrBuildNoteMeta(noteType);

                object? templateProp = TryGetMemberByType(
                    templateNote,
                    "NoteProperty",
                    "property",
                    "Property",
                    "noteProperty",
                    "NoteProperty");
                Type? propType = templateProp?.GetType();
                NotePropertyInjectorMetadata? propMeta = propType is null ? null : GetOrBuildNotePropertyMeta(propType);
                object? templateExprHolder = templateProp is null
                    ? null
                    : TryGetMemberValue(templateProp, "expressionHolder", "expressionholder", "ExpressionHolder");

                MethodInfo? notesAddMethod = areaMeta.NotesAddMethod;

                // 먼저 임시 리스트에 새 Area를 모두 조립한다. 1개도 못 만들면 원본 Areas를 지우지 않고 보존한다.
                var builtAreas = new List<object>(chart.Measures.Count);
                int templateLengthIndex = TryGetIntMember(templateLength, "BeatIndex", "beatIndex") ?? 4;
                int templateLengthSplit = TryGetIntMember(templateLength, "BeatSplit", "beatSplit") ?? 1;

                foreach (BmsMeasure measure in chart.Measures)
                {
                    object? newArea = TryInstantiateArea(areaType, layer, areaMeta);
                    if (newArea is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} Area 생성 실패, 건너뜁니다.");
                        continue;
                    }

                    object areaLength = templateLength;
                    if (Math.Abs(measure.LengthRatio - 1.0) >= 1e-6)
                    {
                        (int ratioNum, int ratioDenom) = BmsChart.ToFraction(measure.LengthRatio);
                        object? customLength = beatInfoMeta.DefaultCtor is not null
                            ? beatInfoMeta.DefaultCtor.Invoke(null)
                            : InstantiateIl2CppObject(beatInfoType);
                        if (customLength is not null
                            && TryWriteBeatInfoPosition(customLength, beatInfoMeta, templateLengthIndex * ratioNum, templateLengthSplit * ratioDenom))
                        {
                            areaLength = customLength;
                        }
                    }

                    TrySetValueByNameCandidates(newArea, AreaBpmNames, chart.Bpm);
                    TrySetValueByNameCandidates(newArea, LengthNames, areaLength);
                    TrySetValueByNameCandidates(newArea, TargetLayerNames, layer);

                    object? notesValue = TryGetMemberByType(newArea, "List<Note>", "notes", "Notes");
                    if (notesValue is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} 새 Area의 notes 컬렉션을 찾지 못했습니다.");
                        continue;
                    }

                    if (notesAddMethod is null)
                    {
                        notesAddMethod = GetCollectionAccessor(notesValue.GetType()).AddMethod;
                        areaMeta.NotesAddMethod ??= notesAddMethod;
                    }

                    foreach (BmsNoteEvent noteEvent in measure.Notes)
                    {
                        int laneIndex = Array.IndexOf(BmsChannelLaneOrder, noteEvent.Channel);
                        if (laneIndex < 0 || laneIndex >= laneUids.Length)
                        {
                            notesSkippedChannel++;
                            skippedChannels.Add(noteEvent.Channel);
                            continue;
                        }

                        object? beatInfo = beatInfoMeta.DefaultCtor is not null
                            ? beatInfoMeta.DefaultCtor.Invoke(null)
                            : InstantiateIl2CppObject(beatInfoType);
                        if (beatInfo is null)
                        {
                            continue;
                        }

                        if (!TryWriteBeatInfoPosition(beatInfo, beatInfoMeta, noteEvent.BeatNumerator, noteEvent.BeatDenominator))
                        {
                            continue;
                        }

                        if (!TryCreateNoteAtPosition(noteType, noteMeta, newArea, laneUids[laneIndex], beatInfo, out object? newNote)
                            || newNote is null)
                        {
                            continue;
                        }

                        // 롱노트는 시작/끝 노트에 각각 StartPoint/EndPoint를 걸어 한 쌍으로 만든다.
                        // 홀드가 마디를 넘어가면 시작과 끝이 서로 다른 Area에 들어가는데,
                        // 게임 원본 차트도 인접 Area에 짝이 나뉘어 있어 같은 방식으로 둔다.
                        ApplyNotePropertyLinkedState(noteMeta, propMeta, templateNote, templateExprHolder, newNote, noteEvent.Kind);

                        if (InvokeAdd(notesAddMethod, notesValue, newNote))
                        {
                            notesCreated++;
                            if (noteEvent.Kind == BmsNoteKind.HoldStart) holdStarts++;
                            else if (noteEvent.Kind == BmsNoteKind.HoldEnd) holdEnds++;
                        }
                    }

                    builtAreas.Add(newArea);
                }

                if (builtAreas.Count == 0)
                {
                    MelonLogger.Warning($"[BmsInject] 생성된 Area가 없어 원본 차트를 보존합니다: {Path.GetFileName(bmsPath)}");
                    return;
                }

                if (!TryClearCollection(areasValue, areasAccessor))
                {
                    MelonLogger.Warning("[BmsInject] 기존 Areas를 비우지 못했습니다.");
                    return;
                }

                int areasCreated = 0;
                for (int i = 0; i < builtAreas.Count; i++)
                {
                    if (InvokeAdd(areasAddMethod, areasValue, builtAreas[i]))
                    {
                        areasCreated++;
                    }
                }

                string skippedChannelsText = skippedChannels.Count == 0 ? "<none>" : string.Join(",", skippedChannels.OrderBy(c => c));
                MelonLogger.Msg($"[BmsInject] 완료: file={Path.GetFileName(bmsPath)} level={CustomChartPlayLevelKey ?? "<default>"} bpm={chart.Bpm} areasCreated={areasCreated} "
                    + $"notesCreated={notesCreated}(hold {holdStarts}시작/{holdEnds}끝) skippedByChannel={notesSkippedChannel}(channels={skippedChannelsText})");

                if (holdStarts != holdEnds)
                {
                    MelonLogger.Warning($"[BmsInject] 롱노트 시작/끝 개수가 맞지 않습니다: 시작={holdStarts} 끝={holdEnds}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BmsInject] 실패: {ex.GetType().Name} {ex.Message}");
            }
        }

        private static AreaInjectorMetadata GetOrBuildAreaMeta(Type areaType, Type layerType)
        {
            return AreaMetaCache.GetOrAdd((areaType, layerType), key =>
            {
                ConstructorInfo? layerCtor = null;
                foreach (ConstructorInfo ctor in key.AreaType.GetConstructors(InstanceMemberFlags))
                {
                    ParameterInfo[] parameters = ctor.GetParameters();
                    if (parameters.Length != 1)
                    {
                        continue;
                    }

                    Type paramType = parameters[0].ParameterType;
                    if (paramType.IsAssignableFrom(key.LayerType) || MatchesShortTypeName(paramType, "Layer", false))
                    {
                        layerCtor = ctor;
                        break;
                    }
                }

                return new AreaInjectorMetadata { LayerCtor = layerCtor };
            });
        }

        private static BeatInfoInjectorMetadata GetOrBuildBeatInfoMeta(Type beatInfoType)
        {
            return BeatInfoMetaCache.GetOrAdd(beatInfoType, type => new BeatInfoInjectorMetadata
            {
                DefaultCtor = type.GetConstructor(InstanceMemberFlags, null, Type.EmptyTypes, null),
                BeatSplitField = type.GetField("BeatSplit", InstanceMemberFlags) ?? type.GetField("beatSplit", InstanceMemberFlags),
                BeatSplitProp = type.GetProperty("BeatSplit", InstanceMemberFlags) ?? type.GetProperty("beatSplit", InstanceMemberFlags),
                BeatIndexField = type.GetField("BeatIndex", InstanceMemberFlags) ?? type.GetField("beatIndex", InstanceMemberFlags),
                BeatIndexProp = type.GetProperty("BeatIndex", InstanceMemberFlags) ?? type.GetProperty("beatIndex", InstanceMemberFlags),
            });
        }

        private static NoteInjectorMetadata GetOrBuildNoteMeta(Type noteType)
        {
            return NoteMetaCache.GetOrAdd(noteType, type =>
            {
                ConstructorInfo? ctor3 = null;
                foreach (ConstructorInfo ctor in type.GetConstructors(InstanceMemberFlags))
                {
                    ParameterInfo[] parameters = ctor.GetParameters();
                    if (parameters.Length == 3
                        && parameters[0].ParameterType.Name.Contains("Area", StringComparison.Ordinal)
                        && parameters[1].ParameterType == typeof(string)
                        && parameters[2].ParameterType.Name.Contains("BeatInfo", StringComparison.Ordinal))
                    {
                        ctor3 = ctor;
                        break;
                    }
                }

                PropertyInfo? propProp = type.GetProperty("property", InstanceMemberFlags)
                    ?? type.GetProperty("Property", InstanceMemberFlags)
                    ?? type.GetProperty("noteProperty", InstanceMemberFlags)
                    ?? type.GetProperty("NoteProperty", InstanceMemberFlags);
                FieldInfo? propField = propProp is null
                    ? (type.GetField("property", InstanceMemberFlags)
                        ?? type.GetField("_property", InstanceMemberFlags)
                        ?? type.GetField("noteProperty", InstanceMemberFlags))
                    : null;

                // 업데이트로 Note.property 이름이 바뀐 경우 NoteProperty 타입의 유일 쓰기 가능 멤버로 폴백한다.
                if ((propProp is null || !propProp.CanWrite) && propField is null)
                {
                    PropertyInfo[] typedProps = type.GetProperties(InstanceMemberFlags)
                        .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0 && !IsCompilerBackingMember(p.Name)
                            && MatchesShortTypeName(p.PropertyType, "NoteProperty", matchGenericArgs: false))
                        .ToArray();
                    if (typedProps.Length == 1)
                    {
                        propProp = typedProps[0];
                        if (LogOnce($"BmsInject.resolve.NoteProp.{type.FullName}"))
                        {
                            MelonLogger.Warning($"[HookPatch][resolve] {type.FullName}.property 대신 타입 일치 프로퍼티 '{propProp.Name}' 로 연결합니다.");
                        }
                    }
                    else
                    {
                        FieldInfo[] typedFields = type.GetFields(InstanceMemberFlags)
                            .Where(f => !f.IsInitOnly && !IsCompilerBackingMember(f.Name)
                                && MatchesShortTypeName(f.FieldType, "NoteProperty", matchGenericArgs: false))
                            .ToArray();
                        if (typedFields.Length == 1)
                        {
                            propField = typedFields[0];
                            if (LogOnce($"BmsInject.resolve.NoteProp.{type.FullName}"))
                            {
                                MelonLogger.Warning($"[HookPatch][resolve] {type.FullName}.property 대신 타입 일치 필드 '{propField.Name}' 로 연결합니다.");
                            }
                        }
                    }
                }

                return new NoteInjectorMetadata
                {
                    Ctor3 = ctor3,
                    PropertyProp = propProp,
                    PropertyField = propField,
                };
            });
        }

        private static bool IsLinkedStateEnum(Type type)
        {
            return type.IsEnum
                && Enum.IsDefined(type, "None")
                && Enum.IsDefined(type, "StartPoint")
                && Enum.IsDefined(type, "EndPoint");
        }

        private static NotePropertyInjectorMetadata GetOrBuildNotePropertyMeta(Type propType)
        {
            return NotePropertyMetaCache.GetOrAdd(propType, type =>
            {
                ConstructorInfo? defaultCtor = type.GetConstructor(InstanceMemberFlags, null, Type.EmptyTypes, null);
                PropertyInfo? exprProp = type.GetProperty("expressionHolder", InstanceMemberFlags)
                    ?? type.GetProperty("ExpressionHolder", InstanceMemberFlags);
                FieldInfo? exprField = exprProp is null
                    ? (type.GetField("expressionHolder", InstanceMemberFlags) ?? type.GetField("ExpressionHolder", InstanceMemberFlags))
                    : null;

                PropertyInfo? linkedProp = type.GetProperty("linked", InstanceMemberFlags)
                    ?? type.GetProperty("Linked", InstanceMemberFlags);
                FieldInfo? linkedField = linkedProp is null
                    ? (type.GetField("linked", InstanceMemberFlags) ?? type.GetField("Linked", InstanceMemberFlags))
                    : null;

                // 업데이트로 linked 이름이 바뀐 경우 None/StartPoint/EndPoint를 가진 유일 enum 멤버로 폴백한다.
                if ((linkedProp is null || !linkedProp.CanWrite) && linkedField is null)
                {
                    PropertyInfo[] enumProps = type.GetProperties(InstanceMemberFlags)
                        .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0 && !IsCompilerBackingMember(p.Name)
                            && IsLinkedStateEnum(p.PropertyType))
                        .ToArray();
                    if (enumProps.Length == 1)
                    {
                        linkedProp = enumProps[0];
                        if (LogOnce($"BmsInject.resolve.Linked.{type.FullName}"))
                        {
                            MelonLogger.Warning($"[HookPatch][resolve] {type.FullName}.linked 대신 enum 일치 프로퍼티 '{linkedProp.Name}' 로 연결합니다.");
                        }
                    }
                    else
                    {
                        FieldInfo[] enumFields = type.GetFields(InstanceMemberFlags)
                            .Where(f => !f.IsInitOnly && !IsCompilerBackingMember(f.Name)
                                && IsLinkedStateEnum(f.FieldType))
                            .ToArray();
                        if (enumFields.Length == 1)
                        {
                            linkedField = enumFields[0];
                            if (LogOnce($"BmsInject.resolve.Linked.{type.FullName}"))
                            {
                                MelonLogger.Warning($"[HookPatch][resolve] {type.FullName}.linked 대신 enum 일치 필드 '{linkedField.Name}' 로 연결합니다.");
                            }
                        }
                    }
                }

                Type? linkedEnumType = linkedProp?.PropertyType ?? linkedField?.FieldType;
                object? linkedNone = null;
                object? linkedStart = null;
                object? linkedEnd = null;
                if (linkedEnumType is not null && linkedEnumType.IsEnum)
                {
                    try
                    {
                        linkedNone = Enum.Parse(linkedEnumType, "None");
                        linkedStart = Enum.Parse(linkedEnumType, "StartPoint");
                        linkedEnd = Enum.Parse(linkedEnumType, "EndPoint");
                    }
                    catch (Exception ex)
                    {
                        if (LogOnce($"BmsInject.LinkedEnum.{linkedEnumType.FullName}"))
                        {
                            MelonLogger.Warning($"[BmsInject] Linked enum 파싱 실패({linkedEnumType.FullName}): {ex.Message}");
                        }
                    }
                }

                return new NotePropertyInjectorMetadata
                {
                    DefaultCtor = defaultCtor,
                    ExprProp = exprProp,
                    ExprField = exprField,
                    LinkedProp = linkedProp,
                    LinkedField = linkedField,
                    LinkedNone = linkedNone,
                    LinkedStart = linkedStart,
                    LinkedEnd = linkedEnd,
                };
            });
        }

        private static object? TryInstantiateArea(Type areaType, object layer, AreaInjectorMetadata areaMeta)
        {
            if (areaMeta.LayerCtor is not null)
            {
                try
                {
                    object? created = areaMeta.LayerCtor.Invoke(new[] { layer });
                    if (created is not null)
                    {
                        return created;
                    }
                }
                catch (Exception ex)
                {
                    if (LogOnce($"BmsInject.AreaCtor.{areaType.FullName}"))
                    {
                        MelonLogger.Warning($"[BmsInject] Area(Layer) 생성자 호출 실패, 폴백 생성으로 전환합니다: {ex.Message}");
                    }
                }
            }

            return InstantiateIl2CppObject(areaType);
        }

        // Layer.Lanes를 순서대로 훑어 UID 문자열을 뽑는다. 곡마다 실제 UID 값이 달라질 수 있어
        // "L0/L1..." 같은 이름을 하드코딩하지 않고 항상 런타임에 조회한다.
        private static string[] ResolveLaneUidsInOrder(object layer, int maxCount)
        {
            object? lanesValue = TryGetMemberByType(layer, "List<Lane>", "Lanes", "lanes");
            if (lanesValue is null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(maxCount);
            foreach (object? lane in EnumerateCollectionItems(lanesValue, maxCount))
            {
                if (lane is null)
                {
                    continue;
                }

                object? uid = TryGetMemberValue(lane, "UID", "uid", "laneUid", "LaneUid");
                if (uid is string uidText && !string.IsNullOrEmpty(uidText))
                {
                    result.Add(uidText);
                }

                if (result.Count >= maxCount)
                {
                    break;
                }
            }

            return result.ToArray();
        }

        private static bool TryClearCollection(object collection, CollectionAccessor accessor)
        {
            if (accessor.ClearMethod is null)
            {
                return false;
            }

            try
            {
                accessor.ClearMethod.Invoke(collection, Array.Empty<object>());
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 노트 수천 개를 넣는 루프에서 호출마다 object[]를 만들지 않도록 인자 버퍼를 재사용한다.
        // 주입은 메인 스레드에서만 일어나고 Invoke는 동기 호출이라 공유해도 안전하다. 끝나면 참조를 비워
        // IL2CPP 래퍼가 버퍼에 붙잡혀 남지 않게 한다.
        private static readonly object?[] SingleArgBuffer = new object?[1];
        private static readonly object?[] NoteCtorArgBuffer = new object?[3];

        private static bool InvokeAdd(MethodInfo? addMethod, object collection, object item)
        {
            if (addMethod is null)
            {
                return false;
            }

            try
            {
                SingleArgBuffer[0] = item;
                addMethod.Invoke(collection, SingleArgBuffer);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                SingleArgBuffer[0] = null;
            }
        }

        private static int? TryGetIntMember(object instance, params string[] names)
        {
            object? value = TryGetMemberValue(instance, names);
            return value is int intVal ? intVal : null;
        }

        private static bool TryWriteBeatInfoPosition(object beatInfo, BeatInfoInjectorMetadata meta, int beatNumerator, int beatDenominator)
        {
            if (meta.CanWriteDirect)
            {
                try
                {
                    meta.WriteDirect(beatInfo, beatDenominator, beatNumerator);
                    return true;
                }
                catch (Exception ex)
                {
                    if (LogOnce($"BmsInject.WriteBeatInfo.{beatInfo.GetType().FullName}"))
                    {
                        MelonLogger.Warning($"[BmsInject] BeatInfo 직접 쓰기 실패, 이름 기반 폴백으로 전환합니다: {ex.Message}");
                    }
                }
            }

            bool splitSet = TrySetValueByNameCandidates(beatInfo, BeatSplitNames, beatDenominator);
            bool indexSet = TrySetValueByNameCandidates(beatInfo, BeatIndexNames, beatNumerator);
            return splitSet && indexSet;
        }

        private static bool TryCreateNoteAtPosition(
            Type noteType,
            NoteInjectorMetadata noteMeta,
            object area,
            string laneUid,
            object beatInfo,
            out object? newNote)
        {
            newNote = null;

            if (noteMeta.Ctor3 is not null)
            {
                try
                {
                    NoteCtorArgBuffer[0] = area;
                    NoteCtorArgBuffer[1] = laneUid;
                    NoteCtorArgBuffer[2] = beatInfo;
                    newNote = noteMeta.Ctor3.Invoke(NoteCtorArgBuffer);
                    if (newNote is not null)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    if (LogOnce($"BmsInject.Ctor3Fail.{noteType.FullName}"))
                    {
                        MelonLogger.Warning($"[BmsInject] Note(Area, string, BeatInfo) 생성자 호출 실패, 폴백 생성으로 전환합니다: {ex.Message}");
                    }
                }
                finally
                {
                    Array.Clear(NoteCtorArgBuffer, 0, NoteCtorArgBuffer.Length);
                }
            }

            newNote = InstantiateIl2CppObject(noteType);
            if (newNote is null)
            {
                return false;
            }

            TrySetValueByNameCandidates(newNote, TargetLaneUidNames, laneUid);
            TrySetValueByNameCandidates(newNote, OwnerNames, area);
            TrySetValueByNameCandidates(newNote, BeatInfoNames, beatInfo);
            return true;
        }

        private static void ApplyNotePropertyLinkedState(
            NoteInjectorMetadata noteMeta,
            NotePropertyInjectorMetadata? propMeta,
            object templateNote,
            object? templateExprHolder,
            object newNote,
            BmsNoteKind kind)
        {
            object? linkedEnumVal = propMeta is null
                ? null
                : kind switch
                {
                    BmsNoteKind.HoldStart => propMeta.LinkedStart,
                    BmsNoteKind.HoldEnd => propMeta.LinkedEnd,
                    _ => propMeta.LinkedNone,
                };

            if (propMeta?.DefaultCtor is not null && linkedEnumVal is not null && noteMeta.CanSetProperty)
            {
                try
                {
                    object propObj = propMeta.DefaultCtor.Invoke(null);
                    propMeta.Apply(propObj, templateExprHolder, linkedEnumVal);
                    noteMeta.SetProperty(newNote, propObj);
                    return;
                }
                catch (Exception ex)
                {
                    if (LogOnce($"BmsInject.NoteProp.{newNote.GetType().FullName}"))
                    {
                        MelonLogger.Warning($"[BmsInject] NoteProperty 직접 생성 실패, 폴백으로 전환합니다: {ex.Message}");
                    }
                }
            }

            // 폴백: Note 생성자가 property를 이미 할당했거나 별도 이름일 때 기존/새 NoteProperty의 linked만 갱신한다.
            string linkedState = kind switch
            {
                BmsNoteKind.HoldStart => "StartPoint",
                BmsNoteKind.HoldEnd => "EndPoint",
                _ => "None",
            };

            object? targetProp = TryGetMemberByType(newNote, "NoteProperty", "property", "Property", "noteProperty", "NoteProperty");
            if (targetProp is null && propMeta?.DefaultCtor is not null)
            {
                try
                {
                    targetProp = propMeta.DefaultCtor.Invoke(null);
                    if (targetProp is not null)
                    {
                        if (templateExprHolder is not null)
                        {
                            TrySetValueByNameCandidates(targetProp, ExpressionHolderNames, templateExprHolder);
                        }

                        TrySetValueByNameCandidates(newNote, NotePropertyNames, targetProp);
                    }
                }
                catch
                {
                    targetProp = null;
                }
            }

            if (targetProp is not null && linkedEnumVal is not null)
            {
                TrySetValueByNameCandidates(targetProp, LinkedNames, linkedEnumVal);
            }
            else if (targetProp is null)
            {
                // 마지막 방어: templateNote의 property 타입에서 enum 값을 직접 파싱해 설정 시도
                object? fallbackTemplateProp = TryGetMemberByType(templateNote, "NoteProperty", "property", "Property");
                if (fallbackTemplateProp is not null)
                {
                    object? cloned = InstantiateIl2CppObject(fallbackTemplateProp.GetType());
                    if (cloned is not null)
                    {
                        if (templateExprHolder is not null)
                        {
                            TrySetValueByNameCandidates(cloned, ExpressionHolderNames, templateExprHolder);
                        }

                        object? linkedValue = TryGetMemberValue(cloned, "linked", "Linked");
                        if (linkedValue is Enum enumVal)
                        {
                            try
                            {
                                TrySetValueByNameCandidates(cloned, LinkedNames, Enum.Parse(enumVal.GetType(), linkedState));
                            }
                            catch
                            {
                            }
                        }

                        TrySetValueByNameCandidates(newNote, NotePropertyNames, cloned);
                    }
                }
            }
        }
    }
}
