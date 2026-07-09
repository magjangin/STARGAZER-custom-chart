using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static readonly HashSet<string> LoggedNoteArrayHits = new HashSet<string>(StringComparer.Ordinal);
        private static bool NoteWipeTestDone;
        private static bool BeatInfoShiftTestDone;
        private static bool LaneShiftTestDone;
        private static bool LinkHoldProbeDone;
        private static bool AreaProbeDone;
        private static bool AreaCreationTestDone;

        private static void ProbeNoteArrayMembers(object? instance, string source)
        {
            try
            {
                if (instance is null)
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} pattern=null");
                    return;
                }

                Type patternType = instance.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo? layersProp = patternType.GetProperty("Layers", flags);
                if (layersProp is null)
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} Layers 속성을 찾지 못했습니다.");
                    return;
                }

                object? layersValue;
                try { layersValue = layersProp.GetValue(instance); }
                catch
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} Layers 값을 읽지 못했습니다.");
                    return;
                }

                if (layersValue is null)
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} Layers=null");
                    return;
                }

                bool isPatternLoaderSource = string.Equals(source, "PatternLoader._Load_b__5_0", StringComparison.Ordinal);
                bool shouldRunWipe = ExperimentChartSettings.EnableKeepEarliestOnlyChart && !NoteWipeTestDone && isPatternLoaderSource;
                bool shouldProbeLinkHold = !LinkHoldProbeDone && isPatternLoaderSource;
                bool shouldProbeArea = !AreaProbeDone && isPatternLoaderSource;
                bool shouldRunAreaCreateTest = ExperimentChartSettings.EnableAreaCreationTest && !AreaCreationTestDone && isPatternLoaderSource;

                var wipeContexts = shouldRunWipe ? new List<NoteCollectionContext>() : null;
                var linkProbe = shouldProbeLinkHold ? new LinkProbeState() : null;
                var areaDetails = shouldProbeArea ? new List<string>() : null;

                int totalAreas = 0, resolvedAreas = 0, totalNotes = 0;
                bool? writableNotesMember = null;
                var nonEmptyAreas = new List<string>();

                int layerIndex = 0;
                foreach (object? layer in EnumerateCollectionItems(layersValue, 12))
                {
                    if (layer is null) { layerIndex++; continue; }

                    Type layerType = layer.GetType();
                    PropertyInfo? areasProp = layerType.GetProperty("Areas", flags)
                        ?? layerType.GetProperty("_Areas_k__BackingField", flags);
                    if (areasProp is null) { layerIndex++; continue; }

                    object? areasValue;
                    try { areasValue = areasProp.GetValue(layer); }
                    catch { layerIndex++; continue; }
                    if (areasValue is null) { layerIndex++; continue; }

                    int areaIndex = 0;
                    foreach (object? area in EnumerateCollectionItems(areasValue, 128))
                    {
                        totalAreas++;
                        if (area is null) { areaIndex++; continue; }

                        if (shouldProbeArea)
                        {
                            if (areaDetails!.Count == 0)
                            {
                                string areaCatalog = BuildObjectMemberCatalog("area", area);
                                MelonLogger.Msg($"[AreaProbe] type={area.GetType().FullName} {areaCatalog}");
                            }
                            if (areaDetails.Count < 32)
                                areaDetails.Add(BuildAreaDetailText(area, layerIndex, areaIndex));
                        }

                        if (shouldRunAreaCreateTest && !AreaCreationTestDone)
                        {
                            TryRunAreaCreationTest(areasProp, areasValue, area);
                            AreaCreationTestDone = true;
                        }

                        if (!TryResolveAreaNotesMember(area, out PropertyInfo? notesProp, out object? notesValue))
                        {
                            areaIndex++;
                            continue;
                        }

                        resolvedAreas++;
                        if (writableNotesMember is null)
                            writableNotesMember = notesProp.CanWrite && notesProp.SetMethod is not null;

                        int count = TryGetCollectionCount(notesValue) ?? 0;

                        if (shouldRunWipe && count > 0 && notesValue is not null)
                        {
                            var items = EnumerateCollectionItems(notesValue, Math.Max(256, count + 8)).ToList();
                            wipeContexts!.Add(new NoteCollectionContext(notesValue, items, count, layerIndex, areaIndex));
                        }

                        if (shouldProbeLinkHold && notesValue is not null)
                        {
                            foreach (object? note in EnumerateCollectionItems(notesValue, Math.Max(256, count + 8)))
                            {
                                if (note is not null)
                                    linkProbe!.ProcessNote(note);
                            }
                        }

                        totalNotes += count;
                        if (count > 0 && nonEmptyAreas.Count < 16)
                            nonEmptyAreas.Add($"L{layerIndex}A{areaIndex}:{count}");

                        areaIndex++;
                    }

                    layerIndex++;
                }

                if (shouldRunWipe)
                    ExecuteWipePhase(wipeContexts!);

                if (shouldProbeLinkHold)
                {
                    linkProbe!.LogResults();
                    LinkHoldProbeDone = true;
                }

                if (shouldProbeArea)
                {
                    MelonLogger.Msg($"[AreaProbe][Detail] {string.Join(" | ", areaDetails!)}");
                    AreaProbeDone = true;
                }

                string nonEmptyText = nonEmptyAreas.Count == 0 ? "<none>" : string.Join(", ", nonEmptyAreas);
                string writableText = writableNotesMember.HasValue ? writableNotesMember.Value.ToString() : "unknown";
                string hit = $"{source} totalAreas={totalAreas} resolvedNotesMembers={resolvedAreas} totalNotes={totalNotes} notesMemberWritable={writableText} nonEmpty={nonEmptyText}";
                if (LoggedNoteArrayHits.Add(hit))
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {hit}");
            }
            catch
            {
            }
        }

        private static string BuildAreaDetailText(object area, int layerIndex, int areaIndex)
        {
            Type areaType = area.GetType();

            string bpmText = "?";
            object? bpmObj = TryGetMemberValue(area, areaType, "AreaBPM");
            if (bpmObj is not null && TryConvertToDouble(bpmObj, out double bpm))
                bpmText = bpm.ToString("0.###");

            string lengthText = "?";
            object? lengthObj = TryGetMemberValue(area, areaType, "length") ?? TryGetMemberValue(area, areaType, "Length");
            if (lengthObj is not null && TryReadBeatInfoPosition(lengthObj, out int lenIndex, out int lenSplit))
                lengthText = $"{lenIndex}/{lenSplit}";

            string durationText = "?";
            object? durationObj = TryGetMemberValue(area, areaType, "Duration");
            if (durationObj is not null && TryConvertToDouble(durationObj, out double duration))
                durationText = duration.ToString("0.###");

            object? notesObj = TryGetMemberValue(area, areaType, "notes") ?? TryGetMemberValue(area, areaType, "Notes");
            int noteCount = TryGetCollectionCount(notesObj) ?? 0;

            return $"L{layerIndex}A{areaIndex}[bpm={bpmText},len={lengthText},dur={durationText},notes={noteCount}]";
        }

        private static void ExecuteWipePhase(List<NoteCollectionContext> wipeContexts)
        {
            int wipeTargets = wipeContexts.Count;
            LogNotesBeforeOperation(wipeContexts, "BeforeOperation");
            EarliestNoteChoice? keepChoice = SelectEarliestNote(wipeContexts);

            if (ExperimentChartSettings.EnableBeatInfoShiftTest && !BeatInfoShiftTestDone && keepChoice is not null)
            {
                TryShiftSelectedNoteBeatInfo(keepChoice);
                BeatInfoShiftTestDone = true;
            }

            if (ExperimentChartSettings.EnableLaneShiftTest && !LaneShiftTestDone && keepChoice is not null)
            {
                TryShiftSelectedNoteLane(keepChoice, wipeContexts);
                LaneShiftTestDone = true;
            }

            int wipeSucceeded = 0;
            int wipedNotes = 0;
            foreach (NoteCollectionContext context in wipeContexts)
            {
                int keepIndex = keepChoice is not null && ReferenceEquals(keepChoice.Context, context)
                    ? keepChoice.NoteIndex
                    : -1;

                bool success = TryKeepOnlySelectedNote(context.NotesCollection, context.Items, context.CountBefore, keepIndex, out int removedCount);
                wipedNotes += removedCount;
                if (success || keepIndex >= 0)
                    wipeSucceeded++;
            }

            if (keepChoice is not null && keepChoice.NoteIndex >= 0 && keepChoice.NoteIndex < keepChoice.Context.Items.Count)
            {
                object? keptNote = keepChoice.Context.Items[keepChoice.NoteIndex];
                if (keptNote is not null)
                    TryAddExperimentChartNotes(keepChoice.Context.NotesCollection, keptNote);
            }

            NoteWipeTestDone = true;

            string keptTimeText = keepChoice?.Time?.ToString("0.###") ?? "unknown";
            string keptTimeNowText = "unknown";
            if (keepChoice is not null && keepChoice.NoteIndex >= 0 && keepChoice.NoteIndex < keepChoice.Context.Items.Count)
            {
                object? keptNote = keepChoice.Context.Items[keepChoice.NoteIndex];
                double? keptNow = keptNote is null ? null : TryExtractNoteTime(keptNote);
                if (keptNow.HasValue)
                    keptTimeNowText = keptNow.Value.ToString("0.###");
            }

            string keptAreaText = keepChoice is null
                ? "none"
                : $"L{keepChoice.Context.LayerIndex}A{keepChoice.Context.AreaIndex}[{keepChoice.NoteIndex}]";

            MelonLogger.Msg($"[NoteWipe] mode=keep-earliest-only targets={wipeTargets} succeeded={wipeSucceeded} removedApprox={wipedNotes} keptArea={keptAreaText} keptTime={keptTimeText} keptTimeNow={keptTimeNowText}");
            LogNotesBeforeOperation(wipeContexts, "AfterOperation");
        }

        private sealed class LinkProbeState
        {
            public int ScannedNotes;
            public int LinkCandidateNotes;
            public int HoldCandidateNotes;
            public readonly Dictionary<string, int> LinkGroupCounts = new(StringComparer.Ordinal);
            public readonly List<string> HoldSamples = new();
            public readonly HashSet<string> NoteSignatures = new(StringComparer.Ordinal);
            public readonly Dictionary<string, int> NoteTypeCounts = new(StringComparer.Ordinal);
            public readonly Dictionary<string, int> LinkedTypeCounts = new(StringComparer.Ordinal);
            public string? NoteMemberCatalog;
            public string? BeatInfoMemberCatalog;
            public string? NotePropertyMemberCatalog;

            public void ProcessNote(object note)
            {
                ScannedNotes++;

                string typeName = note.GetType().FullName ?? note.GetType().Name;
                NoteTypeCounts.TryGetValue(typeName, out int current);
                NoteTypeCounts[typeName] = current + 1;

                if (TryGetValueByNameCandidates(note, new[] { "property", "noteproperty" }, out object? linkedPropObj)
                    && linkedPropObj is not null
                    && TryGetValueByNameCandidates(linkedPropObj, new[] { "linked" }, out object? linkedObj)
                    && linkedObj is not null)
                {
                    string linkedText = linkedObj.ToString() ?? "<null>";
                    LinkedTypeCounts.TryGetValue(linkedText, out int linkedCurrent);
                    LinkedTypeCounts[linkedText] = linkedCurrent + 1;
                }

                if (TryExtractLinkGroupKey(note, out string? linkKey) && linkKey is not null)
                {
                    LinkCandidateNotes++;
                    LinkGroupCounts.TryGetValue(linkKey, out int value);
                    LinkGroupCounts[linkKey] = value + 1;
                }

                if (TryExtractHoldTiming(note, out double? start, out double? end, out double? duration))
                {
                    HoldCandidateNotes++;
                    if (HoldSamples.Count < 8)
                        HoldSamples.Add(BuildHoldSampleText(note, start, end, duration));
                }

                if (NoteSignatures.Count < 4)
                    NoteSignatures.Add(BuildNoteSignature(note));

                if (NoteMemberCatalog is null)
                    NoteMemberCatalog = BuildNoteMemberCatalog(note);

                if (BeatInfoMemberCatalog is null
                    && TryGetValueByNameCandidates(note, new[] { "beatinfo" }, out object? beatInfoObj)
                    && beatInfoObj is not null)
                {
                    BeatInfoMemberCatalog = BuildObjectMemberCatalog("beatInfo", beatInfoObj);
                }

                if (NotePropertyMemberCatalog is null
                    && TryGetValueByNameCandidates(note, new[] { "property", "noteproperty" }, out object? notePropObj)
                    && notePropObj is not null)
                {
                    NotePropertyMemberCatalog = BuildObjectMemberCatalog("property", notePropObj);
                }
            }

            public void LogResults()
            {
                string topGroups = LinkGroupCounts.Count > 0
                    ? string.Join(", ", LinkGroupCounts.OrderByDescending(kvp => kvp.Value).Take(8).Select(kvp => $"{kvp.Key}:{kvp.Value}"))
                    : "<none>";
                string holdSampleText = HoldSamples.Count == 0 ? "<none>" : string.Join(" | ", HoldSamples);
                string typeCountsText = NoteTypeCounts.Count > 0
                    ? string.Join(", ", NoteTypeCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => $"{kvp.Key}:{kvp.Value}"))
                    : "<none>";
                string linkedTypeText = LinkedTypeCounts.Count > 0
                    ? string.Join(", ", LinkedTypeCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => $"{kvp.Key}:{kvp.Value}"))
                    : "<none>";

                MelonLogger.Msg($"[LinkProbe] scannedNotes={ScannedNotes} linkCandidates={LinkCandidateNotes} holdCandidates={HoldCandidateNotes} topGroups={topGroups}");
                MelonLogger.Msg($"[LinkProbe] holdSamples={holdSampleText}");
                MelonLogger.Msg($"[LinkProbe] noteTypes={typeCountsText}");
                MelonLogger.Msg($"[LinkProbe] linkedTypes={linkedTypeText}");

                if (LinkCandidateNotes == 0 && HoldCandidateNotes == 0 && NoteSignatures.Count > 0)
                    MelonLogger.Msg($"[LinkProbe] noteSignatures={string.Join(" || ", NoteSignatures)}");

                if (!string.IsNullOrEmpty(NoteMemberCatalog))
                    MelonLogger.Msg($"[LinkProbe] noteMembers={NoteMemberCatalog}");

                if (!string.IsNullOrEmpty(BeatInfoMemberCatalog))
                    MelonLogger.Msg($"[LinkProbe] beatInfoMembers={BeatInfoMemberCatalog}");

                if (!string.IsNullOrEmpty(NotePropertyMemberCatalog))
                    MelonLogger.Msg($"[LinkProbe] propertyMembers={NotePropertyMemberCatalog}");
            }
        }
    }
}
