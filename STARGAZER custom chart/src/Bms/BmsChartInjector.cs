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
        // 그래서 이 배열은 16=위, 12=아래, 13=왼쪽, 11=오른쪽을 뜻한다
        // (16 기준 시계방향 위->오른쪽->아래->왼쪽으로 11,12,13을 오름차순 배정).
        // 11/12/13 중 어느 게 실제 오른쪽인지는 오름차순 가정이라 게임에서 실측 확인 필요 —
        // 주입할 때 "[BmsInject] 채널→레인 매핑" 로그로 실제 LaneUID와 함께 찍힌다.
        // 실제 LaneUID 문자열은 곡마다 다를 수 있어 하드코딩하지 않고, 런타임에 Layer.Lanes에서 순서대로 뽑는다.
        private static readonly int[] BmsChannelLaneOrder = { 16, 12, 13, 11 };
        private static readonly string[] BmsLaneDirectionNames = { "위", "아래", "왼쪽", "오른쪽" };

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

                LogBmsLaneMappingOnce(laneUids);

                string? bmsPath = album.BmsPath;
                if (bmsPath is null)
                {
                    MelonLogger.Msg($"[BmsInject] .bms 파일이 없어 건너뜁니다: {album.Name}");
                    return;
                }

                string? musicFileName = album.MusicPath is null ? null : Path.GetFileName(album.MusicPath);
                BmsChart? chart = BmsChart.TryParse(bmsPath, musicFileName);
                if (chart is null || chart.Measures.Count == 0)
                {
                    MelonLogger.Warning($"[BmsInject] BMS 파싱 실패 또는 노트 없음: {bmsPath}");
                    return;
                }

                LogBmsChartNotices(chart, bmsPath);

                object? templateLength = TryGetMemberValue(templateArea, areaType, "length");
                Type noteType = templateNote.GetType();
                Type? beatInfoType = FindBeatInfo(templateNote)?.GetType();
                if (templateLength is null || beatInfoType is null)
                {
                    MelonLogger.Warning("[BmsInject] 템플릿의 length/BeatInfo 타입을 찾지 못했습니다.");
                    return;
                }

                // Area.length는 BmsChart.MeasureBeats(4비트)로 명시한다. 분모는 원본 곡과 같게 맞춘다(원본: 192/48).
                // 예전처럼 템플릿 length를 그대로 복사하면 원본 첫 마디가 4비트가 아닐 때 모든 마디가 틀어진다.
                int lengthSplit = TryReadBeatInfoPosition(templateLength, out _, out int templateSplit) && templateSplit > 0
                    ? templateSplit
                    : 1;

                NotePropertyTemplate? propertyTemplate = CreateNotePropertyTemplate(templateNote);
                if (propertyTemplate is null)
                {
                    MelonLogger.Warning("[BmsInject] 템플릿 노트의 property를 찾지 못해 롱노트 연결 정보 없이 주입합니다.");
                }

                string soundKinds = chart.SoundKinds.Count == 0
                    ? "<none>"
                    : string.Join(", ", chart.SoundKinds.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                MelonLogger.Msg($"[BmsInject] 사운드 ID 분류: {soundKinds}");

                int notesCreated = 0, notesSkippedChannel = 0;
                int holdStarts = 0, holdEnds = 0;
                var skippedChannels = new HashSet<int>();

                // 새 Area를 전부 만들어 놓은 뒤에만 기존 Areas를 비운다. 중간에 실패하면 원본 차트를 그대로 둔다.
                // (비운 뒤에 실패하면 빈 차트가 되고, 한 마디만 빠져도 뒤 마디가 전부 당겨져 싱크가 어긋난다.)
                var builtAreas = new List<object>(chart.Measures.Count);
                foreach (BmsMeasure measure in chart.Measures)
                {
                    object? newArea = CreateMeasureArea(areaType, templateArea, layer, chart.Bpm, beatInfoType, lengthSplit);
                    if (newArea is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} Area 생성 실패 — 원본 차트를 그대로 둡니다.");
                        return;
                    }

                    if (!TryResolveAreaNotesMember(newArea, out PropertyInfo? notesProp, out object? notesValue) || notesValue is null)
                    {
                        MelonLogger.Warning($"[BmsInject] 마디 {measure.Index} 새 Area의 notes 컬렉션을 찾지 못했습니다 — 원본 차트를 그대로 둡니다.");
                        return;
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
                        // 짝이 안 맞는 마커는 파서(BmsChart.NormalizeHoldPairs)가 이미 일반 노트로 바꿔 두었다.
                        if (propertyTemplate is not null)
                        {
                            TryApplyNotePropertyLinkedState(propertyTemplate, newNote, ResolveLinkedState(noteEvent.Kind));
                        }

                        if (TryAddToNotesCollection(notesValue, newNote))
                        {
                            notesCreated++;
                            if (noteEvent.Kind == BmsNoteKind.HoldStart) holdStarts++;
                            else if (noteEvent.Kind == BmsNoteKind.HoldEnd) holdEnds++;
                        }
                    }

                    builtAreas.Add(newArea);
                }

                if (!TryClearCollection(areasValue))
                {
                    MelonLogger.Warning("[BmsInject] 기존 Areas를 비우지 못했습니다.");
                    return;
                }

                int areasCreated = 0;
                foreach (object area in builtAreas)
                {
                    if (TryAddToNotesCollection(areasValue, area))
                    {
                        areasCreated++;
                    }
                }

                if (areasCreated != builtAreas.Count)
                {
                    MelonLogger.Warning($"[BmsInject] Area 일부를 Layer.Areas에 추가하지 못했습니다: {areasCreated}/{builtAreas.Count} — 이후 마디 싱크가 어긋날 수 있습니다.");
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

        private static object? CreateMeasureArea(Type areaType, object templateArea, object layer, double bpm, Type beatInfoType, int lengthSplit)
        {
            object? area = TryInstantiateAreaViaLayerConstructor(areaType, templateArea);
            if (area is null)
            {
                return null;
            }

            object? length = InstantiateIl2CppObject(beatInfoType);
            if (length is null || !TryWriteBeatInfoPosition(length, BmsChart.MeasureBeats * lengthSplit, lengthSplit))
            {
                return null;
            }

            TrySetValueByNameCandidates(area, new[] { "areabpm" }, bpm);
            TrySetValueByNameCandidates(area, new[] { "length" }, length);
            TrySetValueByNameCandidates(area, new[] { "targetlayer" }, layer);
            return area;
        }

        private static void LogBmsLaneMappingOnce(string[] laneUids)
        {
            var parts = new List<string>();
            for (int i = 0; i < BmsChannelLaneOrder.Length; i++)
            {
                string uid = i < laneUids.Length ? laneUids[i] : "<없음>";
                parts.Add($"{BmsChannelLaneOrder[i]}->{uid}({BmsLaneDirectionNames[i]})");
            }

            string text = string.Join(", ", parts);
            if (LogOnce($"BmsInject.LaneMap.{text}"))
            {
                MelonLogger.Msg($"[BmsInject] 채널→레인 매핑: {text} (방향은 Layer.Lanes 순서 기준 — 11/12/13은 게임에서 확인 필요)");
            }
        }

        private static void LogBmsChartNotices(BmsChart chart, string bmsPath)
        {
            MelonLogger.Msg($"[BmsInject] 파싱: file={Path.GetFileName(bmsPath)} encoding={chart.EncodingName} bpm={chart.Bpm} measures={chart.Measures.Count}");

            if (chart.MusicStartBeat > 0)
            {
                MelonLogger.Msg($"[BmsInject] 채널 01의 곡 음원이 {chart.MusicStartBeat:0.###}비트({chart.MusicStartBeat / BmsChart.MeasureBeats:0.###}마디) 지점에서 시작해 노트를 그만큼 앞으로 당겼습니다."
                    + (chart.DroppedBeforeMusicCount > 0 ? $" 음원 시작 전 노트 {chart.DroppedBeforeMusicCount}개는 버렸습니다." : string.Empty));
            }

            if (chart.UnsupportedFeatures.Count > 0)
            {
                MelonLogger.Warning($"[BmsInject] 지원하지 않는 기능이 차트에 있습니다: {string.Join(", ", chart.UnsupportedFeatures)} — 그 지점부터 싱크가 어긋날 수 있습니다.");
            }

            if (chart.DemotedHoldCount > 0)
            {
                MelonLogger.Warning($"[BmsInject] 짝이 맞지 않는 롱노트 마커 {chart.DemotedHoldCount}개를 일반 노트로 바꿨습니다(짝이 깨진 롱노트는 게임에서 무결성 오류를 냅니다).");
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

        // TryCreateNoteAtOffset(HookNoteProbes.LongNote.cs)과 같은 생성자를 쓰지만,
        // 오프셋 계산 없이 위치(area/laneUid/beatInfo)를 그대로 받는다 — BMS 변환기 전용.
        private static bool TryCreateNoteAtPosition(Type noteType, object area, string laneUid, object beatInfo, out object? newNote)
        {
            newNote = null;
            ConstructorInfo? noteCtor3 = FindNoteAreaLaneBeatConstructor(noteType);
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
