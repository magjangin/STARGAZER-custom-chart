using System;
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
        // 16=위(고정), 나머지는 16 기준 시계방향(위->오른쪽->아래->왼쪽)으로 11,12,13을 오름차순 배정 —
        // 11/12/13 중 어느 게 오른쪽인지는 오름차순 가정이라 게임에서 실측 확인 필요.
        // 실제 LaneUID 문자열은 곡마다 다를 수 있어 하드코딩하지 않고, 런타임에 Layer.Lanes에서 순서대로 뽑는다.
        private static readonly int[] BmsChannelLaneOrder = { 16, 12, 13, 11 };

        // sourceNote는 위치 정보용이 아니라, Note/Area/NoteProperty의 실제 IL2CPP 타입과
        // NoteProperty의 expressionHolder 샘플 값을 얻기 위한 템플릿으로만 쓰인다.
        private static void TryInjectBmsChart(EarliestNoteChoice keepChoice)
        {
            try
            {
                // 방어적 재확인: Layer.Areas를 통째로 지우는 파괴적 작업이라, 호출부 가드가 뚫려도
                // 여기서 한 번 더 커스텀 트랙 여부를 확인하고 공식곡이면 절대 실행하지 않는다.
                // 어느 앨범의 BMS를 쓸지도 재생 중인 앨범으로 정해진다.
                CustomAlbum? album = CurrentPlayAlbum;
                if (!IsCustomChartPlayActive || album is null)
                {
                    return;
                }

                if (keepChoice.NoteIndex < 0 || keepChoice.NoteIndex >= keepChoice.Context.Items.Count)
                {
                    return;
                }

                object? templateNote = keepChoice.Context.Items[keepChoice.NoteIndex];
                if (templateNote is null)
                {
                    MelonLogger.Warning("[BmsInject] 템플릿 노트를 찾지 못했습니다.");
                    return;
                }

                object? templateArea = FindOwnerArea(templateNote);
                if (templateArea is null)
                {
                    MelonLogger.Warning("[BmsInject] 템플릿 노트의 Area를 찾지 못했습니다.");
                    return;
                }

                Type areaType = templateArea.GetType();
                object? layer = TryGetMemberValue(templateArea, areaType, "TargetLayer");
                if (layer is null)
                {
                    MelonLogger.Warning("[BmsInject] Area의 TargetLayer를 찾지 못했습니다.");
                    return;
                }

                Type layerType = layer.GetType();
                PropertyInfo? areasProp = layerType.GetProperty("Areas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? layerType.GetProperty("_Areas_k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? areasValue = areasProp?.GetValue(layer);
                if (areasProp is null || areasValue is null)
                {
                    MelonLogger.Warning("[BmsInject] Layer.Areas를 찾지 못했습니다.");
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

                BmsChart? chart = BmsChart.TryParse(bmsPath);
                if (chart is null || chart.Measures.Count == 0)
                {
                    MelonLogger.Warning($"[BmsInject] BMS 파싱 실패 또는 노트 없음: {bmsPath}");
                    return;
                }

                object? templateLength = TryGetMemberValue(templateArea, areaType, "length");
                Type noteType = templateNote.GetType();
                Type? beatInfoType = FindBeatInfo(templateNote)?.GetType();
                if (templateLength is null || beatInfoType is null)
                {
                    MelonLogger.Warning("[BmsInject] 템플릿의 length/BeatInfo 타입을 찾지 못했습니다.");
                    return;
                }

                if (!TryClearCollection(areasValue))
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

                foreach (BmsMeasure measure in chart.Measures)
                {
                    object? newArea = TryInstantiateAreaViaLayerConstructor(areaType, templateArea);
                    if (newArea is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} Area 생성 실패, 건너뜁니다.");
                        continue;
                    }

                    TrySetValueByNameCandidates(newArea, new[] { "areabpm" }, chart.Bpm);
                    TrySetValueByNameCandidates(newArea, new[] { "length" }, templateLength);
                    TrySetValueByNameCandidates(newArea, new[] { "targetlayer" }, layer);

                    if (!TryAddToNotesCollection(areasValue, newArea))
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} Area를 Layer.Areas에 추가하지 못했습니다.");
                        continue;
                    }

                    areasCreated++;

                    if (!TryResolveAreaNotesMember(newArea, out PropertyInfo? notesProp, out object? notesValue) || notesValue is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} 새 Area의 notes 컬렉션을 찾지 못했습니다.");
                        continue;
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

                        object? beatInfo = InstantiateIl2CppObject(beatInfoType);
                        if (beatInfo is null || !TryWriteBeatInfoPosition(beatInfo, noteEvent.BeatNumerator, noteEvent.BeatDenominator))
                        {
                            continue;
                        }

                        if (!TryCreateNoteAtPosition(noteType, newArea, laneUids[laneIndex], beatInfo, out object? newNote) || newNote is null)
                        {
                            continue;
                        }

                        // 롱노트는 시작/끝 노트에 각각 StartPoint/EndPoint를 걸어 한 쌍으로 만든다.
                        // 홀드가 마디를 넘어가면 시작과 끝이 서로 다른 Area에 들어가는데,
                        // 게임 원본 차트도 인접 Area에 짝이 나뉘어 있어 같은 방식으로 둔다.
                        string linkedState = ResolveLinkedState(noteEvent.Kind);
                        TryApplyNotePropertyLinkedState(templateNote, newNote, linkedState);

                        if (TryAddToNotesCollection(notesValue, newNote))
                        {
                            notesCreated++;
                            if (noteEvent.Kind == BmsNoteKind.HoldStart) holdStarts++;
                            else if (noteEvent.Kind == BmsNoteKind.HoldEnd) holdEnds++;
                        }
                    }
                }

                string skippedChannelsText = skippedChannels.Count == 0 ? "<none>" : string.Join(",", skippedChannels.OrderBy(c => c));
                MelonLogger.Msg($"[BmsInject] 완료: file={Path.GetFileName(bmsPath)} bpm={chart.Bpm} areasCreated={areasCreated} "
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

        // BMS 노트 종류 -> 게임의 NoteProperty.linked 열거값 이름.
        // 값은 실측으로 확인된 것들이다(LinkProbe 로그: None / StartPoint / EndPoint).
        private static string ResolveLinkedState(BmsNoteKind kind) => kind switch
        {
            BmsNoteKind.HoldStart => "StartPoint",
            BmsNoteKind.HoldEnd => "EndPoint",
            _ => "None",
        };

        // Layer.Lanes를 순서대로 훑어 UID 문자열을 뽑는다. 곡마다 실제 UID 값이 달라질 수 있어
        // "L0/L1..." 같은 이름을 하드코딩하지 않고 항상 런타임에 조회한다.
        private static string[] ResolveLaneUidsInOrder(object layer, int maxCount)
        {
            Type layerType = layer.GetType();
            object? lanesValue = TryGetMemberValue(layer, layerType, "Lanes");
            if (lanesValue is null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (object? lane in EnumerateCollectionItems(lanesValue, maxCount))
            {
                if (lane is null)
                {
                    continue;
                }

                object? uid = TryGetMemberValue(lane, lane.GetType(), "UID");
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

        private static bool TryClearCollection(object collection)
        {
            Type type = collection.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo? clearMethod = type.GetMethod("Clear", flags, null, Type.EmptyTypes, null);
            if (clearMethod is null)
            {
                return false;
            }

            try
            {
                clearMethod.Invoke(collection, Array.Empty<object>());
                return true;
            }
            catch
            {
                return false;
            }
        }

        // TryCreateNoteAtOffset(HookNoteProbes.LongNote.cs)과 같은 생성자 탐색 로직이지만,
        // 오프셋 계산 없이 위치(area/laneUid/beatInfo)를 그대로 받는다 — BMS 변환기 전용.
        private static bool TryCreateNoteAtPosition(Type noteType, object area, string laneUid, object beatInfo, out object? newNote)
        {
            newNote = null;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            ConstructorInfo? noteCtor3 = null;
            foreach (ConstructorInfo ctor in noteType.GetConstructors(flags))
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length == 3
                    && parameters[0].ParameterType.Name.Contains("Area")
                    && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType.Name.Contains("BeatInfo"))
                {
                    noteCtor3 = ctor;
                    break;
                }
            }

            if (noteCtor3 is not null)
            {
                try
                {
                    newNote = noteCtor3.Invoke(new[] { area, laneUid, beatInfo });
                    return true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[BmsInject] Note(Area, string, BeatInfo) 생성자 호출에 실패했습니다: {ex.Message}");
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
    }
}
