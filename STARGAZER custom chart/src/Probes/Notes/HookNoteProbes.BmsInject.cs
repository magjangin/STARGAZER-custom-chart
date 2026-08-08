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

        private static string? FindCustomBmsFile(string hwaPath)
        {
            try
            {
                return Directory.EnumerateFiles(hwaPath, "*.bms").FirstOrDefault();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BmsInject] hwa 폴더 스캔 실패: {ex.Message}");
                return null;
            }
        }

        // sourceNote는 위치 정보용이 아니라, Note/Area/NoteProperty의 실제 IL2CPP 타입과
        // NoteProperty의 expressionHolder 샘플 값을 얻기 위한 템플릿으로만 쓰인다.
        private static void TryInjectBmsChart(EarliestNoteChoice keepChoice)
        {
            try
            {
                // 방어적 재확인: Layer.Areas를 통째로 지우는 파괴적 작업이라, 호출부 가드가 뚫려도
                // 여기서 한 번 더 커스텀 트랙 여부를 확인하고 공식곡이면 절대 실행하지 않는다.
                if (!IsCustomChartPlayActive)
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

                string hwaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwa");
                string? bmsPath = FindCustomBmsFile(hwaPath);
                if (bmsPath is null)
                {
                    MelonLogger.Msg("[BmsInject] hwa 폴더에 .bms 파일이 없어 건너뜁니다.");
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
                var skippedChannels = new HashSet<int>();

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

                        TryApplyNotePropertyLinkedState(templateNote, newNote, "None");

                        if (TryAddToNotesCollection(notesValue, newNote))
                        {
                            notesCreated++;
                        }
                    }
                }

                string skippedChannelsText = skippedChannels.Count == 0 ? "<none>" : string.Join(",", skippedChannels.OrderBy(c => c));
                MelonLogger.Msg($"[BmsInject] 완료: file={Path.GetFileName(bmsPath)} bpm={chart.Bpm} areasCreated={areasCreated} notesCreated={notesCreated} skippedByChannel={notesSkippedChannel}(channels={skippedChannelsText})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BmsInject] 실패: {ex.GetType().Name} {ex.Message}");
            }
        }

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
