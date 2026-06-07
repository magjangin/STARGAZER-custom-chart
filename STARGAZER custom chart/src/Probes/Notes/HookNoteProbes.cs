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
        private const bool EnableNoteWipeTest = true;
        private const bool EnableBeatInfoShiftTest = false;
        private const bool EnableLaneShiftTest = true;
        private static bool NoteWipeTestDone;
        private static bool BeatInfoShiftTestDone;
        private static bool LaneShiftTestDone;
        private static bool LinkHoldProbeDone;

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
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} Layers property not found");
                    return;
                }

                object? layersValue = null;
                try
                {
                    layersValue = layersProp.GetValue(instance);
                }
                catch
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} Layers read failed");
                    return;
                }

                if (layersValue is null)
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {source} Layers=null");
                    return;
                }

                int totalAreas = 0;
                int resolvedAreas = 0;
                int totalNotes = 0;
                bool? writableNotesMember = null;
                var nonEmptyAreas = new List<string>();
                bool shouldRunWipe = EnableNoteWipeTest
                    && !NoteWipeTestDone
                    && string.Equals(source, "PatternLoader._Load_b__5_0", StringComparison.Ordinal);
                var wipeContexts = shouldRunWipe ? new List<NoteCollectionContext>() : null;
                bool shouldProbeLinkHold = !LinkHoldProbeDone
                    && string.Equals(source, "PatternLoader._Load_b__5_0", StringComparison.Ordinal);
                var linkGroupCounts = shouldProbeLinkHold ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
                var holdSamples = shouldProbeLinkHold ? new List<string>() : null;
                var noteSignatures = shouldProbeLinkHold ? new HashSet<string>(StringComparer.Ordinal) : null;
                var noteTypeCounts = shouldProbeLinkHold ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
                var linkedTypeCounts = shouldProbeLinkHold ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
                string? noteMemberCatalog = null;
                string? beatInfoMemberCatalog = null;
                string? notePropertyMemberCatalog = null;
                int scannedNotes = 0;
                int linkCandidateNotes = 0;
                int holdCandidateNotes = 0;
                int wipeTargets = 0;
                int wipeSucceeded = 0;
                int wipedNotes = 0;

                int layerIndex = 0;
                foreach (object? layer in EnumerateCollectionItems(layersValue, 12))
                {
                    if (layer is null)
                    {
                        layerIndex++;
                        continue;
                    }

                    Type layerType = layer.GetType();
                    PropertyInfo? areasProp = layerType.GetProperty("Areas", flags)
                        ?? layerType.GetProperty("_Areas_k__BackingField", flags);
                    if (areasProp is null)
                    {
                        layerIndex++;
                        continue;
                    }

                    object? areasValue;
                    try
                    {
                        areasValue = areasProp.GetValue(layer);
                    }
                    catch
                    {
                        layerIndex++;
                        continue;
                    }

                    if (areasValue is null)
                    {
                        layerIndex++;
                        continue;
                    }

                    int areaIndex = 0;
                    foreach (object? area in EnumerateCollectionItems(areasValue, 128))
                    {
                        totalAreas++;
                        if (area is null)
                        {
                            areaIndex++;
                            continue;
                        }

                        if (!TryResolveAreaNotesMember(area, out PropertyInfo? notesProp, out object? notesValue))
                        {
                            areaIndex++;
                            continue;
                        }

                        resolvedAreas++;
                        bool isWritable = notesProp.CanWrite && notesProp.SetMethod is not null;
                        if (writableNotesMember is null)
                        {
                            writableNotesMember = isWritable;
                        }

                        int count = TryGetCollectionCount(notesValue) ?? 0;
                        if (shouldRunWipe && count > 0 && notesValue is not null)
                        {
                            wipeTargets++;
                            var items = EnumerateCollectionItems(notesValue, Math.Max(256, count + 8)).ToList();
                            wipeContexts!.Add(new NoteCollectionContext(notesValue, items, count, layerIndex, areaIndex));
                        }

                        if (shouldProbeLinkHold && notesValue is not null)
                        {
                            foreach (object? note in EnumerateCollectionItems(notesValue, Math.Max(256, count + 8)))
                            {
                                if (note is null)
                                {
                                    continue;
                                }

                                scannedNotes++;
                                if (noteTypeCounts is not null)
                                {
                                    string typeName = note.GetType().FullName ?? note.GetType().Name;
                                    if (!noteTypeCounts.TryGetValue(typeName, out int current))
                                    {
                                        current = 0;
                                    }

                                    noteTypeCounts[typeName] = current + 1;
                                }

                                if (linkedTypeCounts is not null
                                    && TryGetValueByNameCandidates(note, new[] { "property", "noteproperty" }, out object? linkedPropObj)
                                    && linkedPropObj is not null
                                    && TryGetValueByNameCandidates(linkedPropObj, new[] { "linked" }, out object? linkedObj)
                                    && linkedObj is not null)
                                {
                                    string linkedText = linkedObj.ToString() ?? "<null>";
                                    if (!linkedTypeCounts.TryGetValue(linkedText, out int linkedCurrent))
                                    {
                                        linkedCurrent = 0;
                                    }

                                    linkedTypeCounts[linkedText] = linkedCurrent + 1;
                                }

                                if (TryExtractLinkGroupKey(note, out string? linkKey) && linkKey is not null)
                                {
                                    linkCandidateNotes++;
                                    if (!linkGroupCounts!.TryGetValue(linkKey, out int value))
                                    {
                                        value = 0;
                                    }

                                    linkGroupCounts[linkKey] = value + 1;
                                }

                                if (TryExtractHoldTiming(note, out double? start, out double? end, out double? duration))
                                {
                                    holdCandidateNotes++;
                                    if (holdSamples!.Count < 8)
                                    {
                                        holdSamples.Add(BuildHoldSampleText(note, start, end, duration));
                                    }
                                }

                                if (noteSignatures is not null && noteSignatures.Count < 4)
                                {
                                    noteSignatures.Add(BuildNoteSignature(note));
                                }

                                if (noteMemberCatalog is null)
                                {
                                    noteMemberCatalog = BuildNoteMemberCatalog(note);
                                }

                                if (beatInfoMemberCatalog is null && TryGetValueByNameCandidates(note, new[] { "beatinfo" }, out object? beatInfoObj) && beatInfoObj is not null)
                                {
                                    beatInfoMemberCatalog = BuildObjectMemberCatalog("beatInfo", beatInfoObj);
                                }

                                if (notePropertyMemberCatalog is null && TryGetValueByNameCandidates(note, new[] { "property", "noteproperty" }, out object? notePropObj) && notePropObj is not null)
                                {
                                    notePropertyMemberCatalog = BuildObjectMemberCatalog("property", notePropObj);
                                }
                            }
                        }

                        totalNotes += count;
                        if (count > 0 && nonEmptyAreas.Count < 16)
                        {
                            nonEmptyAreas.Add($"L{layerIndex}A{areaIndex}:{count}");
                        }

                        areaIndex++;
                    }

                    layerIndex++;
                }

                if (shouldRunWipe)
                {
                    LogNotesBeforeOperation(wipeContexts!, "BeforeOperation");
                    EarliestNoteChoice? keepChoice = SelectEarliestNote(wipeContexts!);
                    if (EnableBeatInfoShiftTest && !BeatInfoShiftTestDone && keepChoice is not null)
                    {
                        TryShiftSelectedNoteBeatInfo(keepChoice);
                        BeatInfoShiftTestDone = true;
                    }

                    if (EnableLaneShiftTest && !LaneShiftTestDone && keepChoice is not null)
                    {
                        TryShiftSelectedNoteLane(keepChoice, wipeContexts!);
                        LaneShiftTestDone = true;
                    }

                    foreach (NoteCollectionContext context in wipeContexts!)
                    {
                        int keepIndex = keepChoice is not null && ReferenceEquals(keepChoice.Context, context)
                            ? keepChoice.NoteIndex
                            : -1;
                        if (TryKeepOnlySelectedNote(context.NotesCollection, context.Items, context.CountBefore, keepIndex, out int removedCount))
                        {
                            wipeSucceeded++;
                            wipedNotes += removedCount;

                            if (keepIndex >= 0 && keepIndex < context.Items.Count)
                            {
                                object? keptNote = context.Items[keepIndex];
                                if (keptNote is not null)
                                {
                                    TryDuplicateAndLinkAsLongNote(context.NotesCollection, keptNote, out _);
                                }
                            }
                        }
                    }

                    NoteWipeTestDone = true;
                    string keptTimeText = keepChoice is null || keepChoice.Time is null
                        ? "unknown"
                        : keepChoice.Time.Value.ToString("0.###");
                    string keptTimeNowText = "unknown";
                    if (keepChoice is not null
                        && keepChoice.NoteIndex >= 0
                        && keepChoice.NoteIndex < keepChoice.Context.Items.Count)
                    {
                        object? keptNote = keepChoice.Context.Items[keepChoice.NoteIndex];
                        double? keptNow = keptNote is null ? null : TryExtractNoteTime(keptNote);
                        if (keptNow.HasValue)
                        {
                            keptTimeNowText = keptNow.Value.ToString("0.###");
                        }
                    }
                    string keptAreaText = keepChoice is null
                        ? "none"
                        : $"L{keepChoice.Context.LayerIndex}A{keepChoice.Context.AreaIndex}[{keepChoice.NoteIndex}]";
                    MelonLogger.Msg($"[NoteWipe] mode=keep-earliest-only targets={wipeTargets} succeeded={wipeSucceeded} removedApprox={wipedNotes} keptArea={keptAreaText} keptTime={keptTimeText} keptTimeNow={keptTimeNowText}");
                    LogNotesBeforeOperation(wipeContexts!, "AfterOperation");
                }

                if (shouldProbeLinkHold)
                {
                    LinkHoldProbeDone = true;
                    string topGroups = "<none>";
                    if (linkGroupCounts is not null && linkGroupCounts.Count > 0)
                    {
                        topGroups = string.Join(", ",
                            linkGroupCounts
                                .OrderByDescending(kvp => kvp.Value)
                                .Take(8)
                                .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                    }

                    string holdSampleText = holdSamples is null || holdSamples.Count == 0
                        ? "<none>"
                        : string.Join(" | ", holdSamples);
                    string typeCountsText = "<none>";
                    if (noteTypeCounts is not null && noteTypeCounts.Count > 0)
                    {
                        typeCountsText = string.Join(", ",
                            noteTypeCounts
                                .OrderByDescending(kvp => kvp.Value)
                                .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                                .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                    }
                    string linkedTypeText = "<none>";
                    if (linkedTypeCounts is not null && linkedTypeCounts.Count > 0)
                    {
                        linkedTypeText = string.Join(", ",
                            linkedTypeCounts
                                .OrderByDescending(kvp => kvp.Value)
                                .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                                .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                    }

                    MelonLogger.Msg($"[LinkProbe] scannedNotes={scannedNotes} linkCandidates={linkCandidateNotes} holdCandidates={holdCandidateNotes} topGroups={topGroups}");
                    MelonLogger.Msg($"[LinkProbe] holdSamples={holdSampleText}");
                    MelonLogger.Msg($"[LinkProbe] noteTypes={typeCountsText}");
                    MelonLogger.Msg($"[LinkProbe] linkedTypes={linkedTypeText}");
                    if (linkCandidateNotes == 0 && holdCandidateNotes == 0 && noteSignatures is not null && noteSignatures.Count > 0)
                    {
                        MelonLogger.Msg($"[LinkProbe] noteSignatures={string.Join(" || ", noteSignatures)}");
                    }

                    if (!string.IsNullOrEmpty(noteMemberCatalog))
                    {
                        MelonLogger.Msg($"[LinkProbe] noteMembers={noteMemberCatalog}");
                    }

                    if (!string.IsNullOrEmpty(beatInfoMemberCatalog))
                    {
                        MelonLogger.Msg($"[LinkProbe] beatInfoMembers={beatInfoMemberCatalog}");
                    }

                    if (!string.IsNullOrEmpty(notePropertyMemberCatalog))
                    {
                        MelonLogger.Msg($"[LinkProbe] propertyMembers={notePropertyMemberCatalog}");
                    }
                }

                string nonEmptyText = nonEmptyAreas.Count == 0 ? "<none>" : string.Join(", ", nonEmptyAreas);
                string writableText = writableNotesMember.HasValue ? writableNotesMember.Value.ToString() : "unknown";
                string hit = $"{source} totalAreas={totalAreas} resolvedNotesMembers={resolvedAreas} totalNotes={totalNotes} notesMemberWritable={writableText} nonEmpty={nonEmptyText}";
                if (LoggedNoteArrayHits.Add(hit))
                {
                    MelonLogger.Msg($"[NoteProbe][AreaNotes] {hit}");
                }
            }
            catch
            {
            }
        }
    }
}
