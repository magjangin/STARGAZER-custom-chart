using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        // BMS 채널 -> 레인 순번 매핑. Layer.Lanes[0..3]는 실측으로 확인된 (위, 아래, 왼쪽, 오른쪽) 물리 배치다.
        // 16=위(고정), 나머지는 16 기준 시계방향(위->오른쪽->아래->왼쪽)으로 11,12,13을 오름차순 배정한다.
        // 실제 LaneUID 문자열은 곡마다 다를 수 있어 하드코딩하지 않고, 런타임에 Layer.Lanes에서 순서대로 뽑는다.
        private static readonly int[] BmsChannelLaneOrder = { 16, 12, 13, 11 };

        private sealed class AreaInjectorMetadata
        {
            public ConstructorInfo? LayerCtor { get; init; }
            public MethodInfo? NotesAddMethod { get; set; }
        }

        private sealed class BeatInfoInjectorMetadata
        {
            public ConstructorInfo? DefaultCtor { get; init; }
            public FieldInfo? BeatSplitField { get; init; }
            public PropertyInfo? BeatSplitProp { get; init; }
            public FieldInfo? BeatIndexField { get; init; }
            public PropertyInfo? BeatIndexProp { get; init; }

            public bool CanWriteDirect =>
                (BeatSplitField is not null || BeatSplitProp is not null)
                && (BeatIndexField is not null || BeatIndexProp is not null);

            public void WriteDirect(object beatInfo, int split, int index)
            {
                if (BeatSplitField is not null) BeatSplitField.SetValue(beatInfo, split);
                else BeatSplitProp!.SetValue(beatInfo, split);

                if (BeatIndexField is not null) BeatIndexField.SetValue(beatInfo, index);
                else BeatIndexProp!.SetValue(beatInfo, index);
            }
        }

        private sealed class NoteInjectorMetadata
        {
            public ConstructorInfo? Ctor3 { get; init; }
            public PropertyInfo? PropertyProp { get; init; }
            public FieldInfo? PropertyField { get; init; }

            public bool CanSetProperty =>
                (PropertyProp is not null && PropertyProp.CanWrite) || PropertyField is not null;

            public void SetProperty(object note, object propValue)
            {
                if (PropertyProp is not null && PropertyProp.CanWrite) PropertyProp.SetValue(note, propValue);
                else PropertyField?.SetValue(note, propValue);
            }
        }

        private sealed class NotePropertyInjectorMetadata
        {
            public ConstructorInfo? DefaultCtor { get; init; }
            public PropertyInfo? ExprProp { get; init; }
            public FieldInfo? ExprField { get; init; }
            public PropertyInfo? LinkedProp { get; init; }
            public FieldInfo? LinkedField { get; init; }
            public object? LinkedNone { get; init; }
            public object? LinkedStart { get; init; }
            public object? LinkedEnd { get; init; }

            public void Apply(object propObj, object? templateExprHolder, object linkedEnumVal)
            {
                if (templateExprHolder is not null)
                {
                    if (ExprProp is not null && ExprProp.CanWrite) ExprProp.SetValue(propObj, templateExprHolder);
                    else if (ExprField is not null) ExprField.SetValue(propObj, templateExprHolder);
                }

                if (LinkedProp is not null && LinkedProp.CanWrite) LinkedProp.SetValue(propObj, linkedEnumVal);
                else if (LinkedField is not null) LinkedField.SetValue(propObj, linkedEnumVal);
            }
        }

        private static readonly ConcurrentDictionary<(Type AreaType, Type LayerType), AreaInjectorMetadata> AreaMetaCache =
            new ConcurrentDictionary<(Type, Type), AreaInjectorMetadata>();

        private static readonly ConcurrentDictionary<Type, BeatInfoInjectorMetadata> BeatInfoMetaCache =
            new ConcurrentDictionary<Type, BeatInfoInjectorMetadata>();

        private static readonly ConcurrentDictionary<Type, NoteInjectorMetadata> NoteMetaCache =
            new ConcurrentDictionary<Type, NoteInjectorMetadata>();

        private static readonly ConcurrentDictionary<Type, NotePropertyInjectorMetadata> NotePropertyMetaCache =
            new ConcurrentDictionary<Type, NotePropertyInjectorMetadata>();

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

                string? bmsPath = album.BmsPath;
                if (bmsPath is null)
                {
                    MelonLogger.Msg($"[BmsInject] .bms 파일이 없어 건너뜁니다: {album.Name}");
                    return;
                }

                BmsChart? chart = BmsChart.TryGetOrParse(bmsPath);
                if (chart is null || chart.Measures.Count == 0)
                {
                    MelonLogger.Warning($"[BmsInject] BMS 파싱 실패 또는 노트 없음: {bmsPath}");
                    return;
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
                if (!TryClearCollection(areasValue, areasAccessor))
                {
                    MelonLogger.Warning("[BmsInject] 기존 Areas를 비우지 못했습니다.");
                    return;
                }

                int areasCreated = 0, notesCreated = 0, notesSkippedChannel = 0;
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

                MethodInfo? areasAddMethod = areasAccessor.AddMethod;
                MethodInfo? notesAddMethod = areaMeta.NotesAddMethod;

                foreach (BmsMeasure measure in chart.Measures)
                {
                    object? newArea = TryInstantiateArea(areaType, layer, areaMeta);
                    if (newArea is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} Area 생성 실패, 건너뜁니다.");
                        continue;
                    }

                    TrySetValueByNameCandidates(newArea, new[] { "areabpm" }, chart.Bpm);
                    TrySetValueByNameCandidates(newArea, new[] { "length" }, templateLength);
                    TrySetValueByNameCandidates(newArea, new[] { "targetlayer" }, layer);

                    if (!InvokeAdd(areasAddMethod, areasValue, newArea))
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} Area를 Layer.Areas에 추가하지 못했습니다.");
                        continue;
                    }

                    areasCreated++;

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
                }

                string skippedChannelsText = skippedChannels.Count == 0 ? "<none>" : string.Join(",", skippedChannels.OrderBy(c => c));
                string noiseText = chart.SuppressedNoiseCount > 0 ? $" suppressedNoise={chart.SuppressedNoiseCount}" : string.Empty;
                MelonLogger.Msg($"[BmsInject] 완료: file={Path.GetFileName(bmsPath)} bpm={chart.Bpm} areasCreated={areasCreated} "
                    + $"notesCreated={notesCreated}(hold {holdStarts}시작/{holdEnds}끝){noiseText} skippedByChannel={notesSkippedChannel}(channels={skippedChannelsText})");

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

                return new NoteInjectorMetadata
                {
                    Ctor3 = ctor3,
                    PropertyProp = propProp,
                    PropertyField = propField,
                };
            });
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
                    catch
                    {
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
                catch
                {
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

        private static bool InvokeAdd(MethodInfo? addMethod, object collection, object item)
        {
            if (addMethod is null)
            {
                return false;
            }

            try
            {
                addMethod.Invoke(collection, new[] { item });
                return true;
            }
            catch
            {
                return false;
            }
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
                catch
                {
                }
            }

            bool splitSet = TrySetValueByNameCandidates(beatInfo, new[] { "beatsplit" }, beatDenominator);
            bool indexSet = TrySetValueByNameCandidates(beatInfo, new[] { "beatindex" }, beatNumerator);
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
                    newNote = noteMeta.Ctor3.Invoke(new[] { area, laneUid, beatInfo });
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
            }

            newNote = InstantiateIl2CppObject(noteType);
            if (newNote is null)
            {
                return false;
            }

            TrySetValueByNameCandidates(newNote, new[] { "targetlaneuid" }, laneUid);
            TrySetValueByNameCandidates(newNote, new[] { "owner" }, area);
            TrySetValueByNameCandidates(newNote, new[] { "beatinfo" }, beatInfo);
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
                catch
                {
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
                            TrySetValueByNameCandidates(targetProp, new[] { "expressionholder" }, templateExprHolder);
                        }

                        TrySetValueByNameCandidates(newNote, new[] { "property", "noteproperty" }, targetProp);
                    }
                }
                catch
                {
                    targetProp = null;
                }
            }

            if (targetProp is not null && linkedEnumVal is not null)
            {
                TrySetValueByNameCandidates(targetProp, new[] { "linked" }, linkedEnumVal);
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
                            TrySetValueByNameCandidates(cloned, new[] { "expressionholder" }, templateExprHolder);
                        }

                        object? linkedValue = TryGetMemberValue(cloned, "linked", "Linked");
                        if (linkedValue is Enum enumVal)
                        {
                            try
                            {
                                TrySetValueByNameCandidates(cloned, new[] { "linked" }, Enum.Parse(enumVal.GetType(), linkedState));
                            }
                            catch
                            {
                            }
                        }

                        TrySetValueByNameCandidates(newNote, new[] { "property", "noteproperty" }, cloned);
                    }
                }
            }
        }
    }
}
