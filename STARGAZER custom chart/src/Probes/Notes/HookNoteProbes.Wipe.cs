using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static void TryShiftSelectedNoteLane(EarliestNoteChoice choice, IReadOnlyList<NoteCollectionContext> contexts)
        {
            object? note = choice.NoteIndex >= 0 && choice.NoteIndex < choice.Context.Items.Count
                ? choice.Context.Items[choice.NoteIndex]
                : null;
            if (note is null
                || !TryGetValueByNameCandidates(note, new[] { "targetlaneuid" }, out object? currentLane)
                || currentLane is null)
            {
                MelonLogger.Warning("[LaneShiftTest] 선택된 노트 레인을 찾을 수 없습니다.");
                return;
            }

            string currentLaneText = currentLane.ToString() ?? "<unknown>";
            foreach (NoteCollectionContext context in contexts)
            {
                foreach (object? candidate in context.Items)
                {
                    if (candidate is null
                        || ReferenceEquals(candidate, note)
                        || !TryGetValueByNameCandidates(candidate, new[] { "targetlaneuid" }, out object? candidateLane)
                        || candidateLane is null
                        || candidateLane.GetType() != currentLane.GetType()
                        || string.Equals(candidateLane.ToString(), currentLaneText, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bool changed = TrySetValueByNameCandidates(note, new[] { "targetlaneuid" }, candidateLane);
                    MelonLogger.Msg($"[LaneShiftTest] 선택된 노트 레인 {currentLaneText}->{candidateLane} 변경 여부={changed}");
                    return;
                }
            }

            MelonLogger.Warning($"[LaneShiftTest] 대체 레인을 찾을 수 없습니다. 현재={currentLaneText}");
        }

        private static void TryShiftSelectedNoteBeatInfo(EarliestNoteChoice keepChoice)
        {
            if (keepChoice.NoteIndex < 0 || keepChoice.NoteIndex >= keepChoice.Context.Items.Count)
            {
                return;
            }

            object? note = keepChoice.Context.Items[keepChoice.NoteIndex];
            if (note is null)
            {
                return;
            }

            if (!TryGetValueByNameCandidates(note, new[] { "beatinfo" }, out object? beatInfoObj) || beatInfoObj is null)
            {
                MelonLogger.Warning("[BeatShiftTest] 유지된 노트에서 beatInfo를 찾을 수 없습니다.");
                return;
            }

            if (!TryGetDoubleByNameCandidates(note, new[] { "beatvalue" }, out double? beforeBeatValue))
            {
                beforeBeatValue = null;
            }
            string beforeHash = TryGetValueByNameCandidates(note, new[] { "uniquehash" }, out object? beforeHashObj)
                ? (beforeHashObj?.ToString() ?? "?")
                : "?";

            Type beatInfoType = beatInfoObj.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? splitField = beatInfoType.GetField("BeatSplit", flags);
            FieldInfo? indexField = beatInfoType.GetField("BeatIndex", flags);
            if (splitField is null || indexField is null)
            {
                MelonLogger.Warning("[BeatShiftTest] BeatSplit/BeatIndex 필드를 찾을 수 없습니다.");
                return;
            }

            int oldSplit;
            int oldIndex;
            try
            {
                oldSplit = Convert.ToInt32(splitField.GetValue(beatInfoObj));
                oldIndex = Convert.ToInt32(indexField.GetValue(beatInfoObj));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BeatShiftTest] BeatInfo 필드를 읽지 못했습니다: {ex.GetType().Name}");
                return;
            }

            int newIndex = oldIndex;
            int newSplit = oldSplit + 24;
            if (newSplit >= 192)
            {
                newSplit -= 192;
                newIndex += 1;
            }

            try
            {
                splitField.SetValue(beatInfoObj, newSplit);
                indexField.SetValue(beatInfoObj, newIndex);
                if (!TrySetValueByNameCandidates(note, new[] { "beatinfo" }, beatInfoObj))
                {
                    MelonLogger.Warning("[BeatShiftTest] 노트에 beatInfo를 다시 쓰지 못했습니다.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BeatShiftTest] BeatInfo 필드를 쓰지 못했습니다: {ex.GetType().Name}");
                return;
            }

            TryGetDoubleByNameCandidates(note, new[] { "beatvalue" }, out double? afterBeatValue);
            string afterHash = TryGetValueByNameCandidates(note, new[] { "uniquehash" }, out object? afterHashObj)
                ? (afterHashObj?.ToString() ?? "?")
                : "?";
            string beforeText = beforeBeatValue.HasValue ? beforeBeatValue.Value.ToString("0.###") : "?";
            string afterText = afterBeatValue.HasValue ? afterBeatValue.Value.ToString("0.###") : "?";
            MelonLogger.Msg($"[BeatShiftTest] 유지 노트 L{keepChoice.Context.LayerIndex}A{keepChoice.Context.AreaIndex}[{keepChoice.NoteIndex}] BeatIndex {oldIndex}->{newIndex}, BeatSplit {oldSplit}->{newSplit}, BeatValue {beforeText}->{afterText}, UniqueHash {beforeHash}->{afterHash}");
        }

        private static bool TryKeepOnlySelectedNote(object notesValue, IReadOnlyList<object?> items, int knownCount, int keepIndex, out int removedCount)
        {
            removedCount = 0;
            Type type = notesValue.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            int currentCount = knownCount > 0 ? knownCount : (TryGetCollectionCount(notesValue) ?? 0);
            if (currentCount <= 0)
            {
                return false;
            }

            MethodInfo? removeAtMethod = type.GetMethod("RemoveAt", flags, null, new[] { typeof(int) }, null)
                ?? type.GetMethod("RemoveAt", flags);
            if (removeAtMethod is not null)
            {
                int startIndex = currentCount - 1;
                bool anyRemoved = false;
                try
                {
                    for (int i = startIndex; i >= 0; i--)
                    {
                        if (i == keepIndex)
                        {
                            continue;
                        }

                        removeAtMethod.Invoke(notesValue, new object[] { i });
                        removedCount++;
                        anyRemoved = true;
                    }

                    return anyRemoved;
                }
                catch
                {
                    return anyRemoved;
                }
            }

            MethodInfo? clearMethod = type.GetMethod("Clear", flags, null, Type.EmptyTypes, null);
            if (clearMethod is not null)
            {
                try
                {
                    if (keepIndex >= 0 && keepIndex < items.Count)
                    {
                        MethodInfo? addMethod = type.GetMethods(flags)
                            .FirstOrDefault(method => string.Equals(method.Name, "Add", StringComparison.Ordinal) && method.GetParameters().Length == 1);
                        if (addMethod is null)
                        {
                            return false;
                        }

                        object? keepItem = items[keepIndex];
                        if (keepItem is null)
                        {
                            return false;
                        }

                        clearMethod.Invoke(notesValue, Array.Empty<object>());
                        addMethod.Invoke(notesValue, new[] { keepItem });
                        removedCount = Math.Max(0, currentCount - 1);
                        return removedCount > 0;
                    }

                    clearMethod.Invoke(notesValue, Array.Empty<object>());
                    removedCount = currentCount;
                    return removedCount > 0;
                }
                catch
                {
                }
            }

            return false;
        }

        private static EarliestNoteChoice? SelectEarliestNote(IReadOnlyList<NoteCollectionContext> contexts)
        {
            EarliestNoteChoice? best = null;
            int sequence = 0;
            foreach (NoteCollectionContext context in contexts)
            {
                for (int i = 0; i < context.Items.Count; i++)
                {
                    object? item = context.Items[i];
                    if (item is null)
                    {
                        sequence++;
                        continue;
                    }

                    double? time = TryExtractNoteTime(item);
                    if (best is null)
                    {
                        best = new EarliestNoteChoice(context, i, time, sequence);
                        sequence++;
                        continue;
                    }

                    if (time is not null && best.Time is null)
                    {
                        best = new EarliestNoteChoice(context, i, time, sequence);
                        sequence++;
                        continue;
                    }

                    if (time is not null && best.Time is not null && time.Value < best.Time.Value)
                    {
                        best = new EarliestNoteChoice(context, i, time, sequence);
                        sequence++;
                        continue;
                    }

                    if (time is not null && best.Time is not null && Math.Abs(time.Value - best.Time.Value) < 0.00001 && sequence < best.Sequence)
                    {
                        best = new EarliestNoteChoice(context, i, time, sequence);
                    }

                    sequence++;
                }
            }

            return best;
        }

        private static double? TryExtractNoteTime(object note)
        {
            Type noteType = note.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] candidates = { "time", "timing", "start", "starttime", "hittime", "hittiming", "judge", "tick", "beat", "position", "ms" };

            foreach (PropertyInfo property in noteType.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!LooksLikeTimeName(property.Name, candidates))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(note);
                }
                catch
                {
                    continue;
                }

                if (TryConvertToDouble(value, out double time))
                {
                    return time;
                }
            }

            foreach (MethodInfo method in noteType.GetMethods(flags))
            {
                if (method.GetParameters().Length != 0)
                {
                    continue;
                }

                if (!LooksLikeTimeName(method.Name, candidates))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = method.Invoke(note, Array.Empty<object>());
                }
                catch
                {
                    continue;
                }

                if (TryConvertToDouble(value, out double time))
                {
                    return time;
                }
            }

            foreach (FieldInfo field in noteType.GetFields(flags))
            {
                if (!LooksLikeTimeName(field.Name, candidates))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = field.GetValue(note);
                }
                catch
                {
                    continue;
                }

                if (TryConvertToDouble(value, out double time))
                {
                    return time;
                }
            }

            return null;
        }
        private sealed class NoteCollectionContext
        {
            public NoteCollectionContext(object notesCollection, List<object?> items, int countBefore, int layerIndex, int areaIndex)
            {
                NotesCollection = notesCollection;
                Items = items;
                CountBefore = countBefore;
                LayerIndex = layerIndex;
                AreaIndex = areaIndex;
            }

            public object NotesCollection { get; }
            public List<object?> Items { get; }
            public int CountBefore { get; }
            public int LayerIndex { get; }
            public int AreaIndex { get; }
        }

        private sealed class EarliestNoteChoice
        {
            public EarliestNoteChoice(NoteCollectionContext context, int noteIndex, double? time, int sequence)
            {
                Context = context;
                NoteIndex = noteIndex;
                Time = time;
                Sequence = sequence;
            }

            public NoteCollectionContext Context { get; }
            public int NoteIndex { get; }
            public double? Time { get; }
            public int Sequence { get; }
        }

        private static void LogNotesBeforeOperation(IReadOnlyList<NoteCollectionContext> contexts, string stage)
        {
            MelonLogger.Msg($"[NoteDebug][{stage}] --- Printing Long Notes Only (Total Contexts: {contexts.Count}) ---");
            for (int c = 0; c < contexts.Count; c++)
            {
                NoteCollectionContext context = contexts[c];
                System.Collections.Generic.List<object?> liveItems = EnumerateCollectionItems(context.NotesCollection, 1024).ToList();
                bool hasLongNote = false;
                for (int i = 0; i < liveItems.Count; i++)
                {
                    object? note = liveItems[i];
                    if (note == null) continue;

                    string linkText = "?";
                    TryGetValueByNameCandidates(note, new[] { "property", "noteproperty" }, out object? propObj);
                    if (propObj != null)
                    {
                        TryGetValueByNameCandidates(propObj, new[] { "linked" }, out object? linkedObj);
                        linkText = linkedObj?.ToString() ?? "?";
                    }

                    bool isLong = (linkText != "None" && linkText != "?") || TryExtractHoldTiming(note, out _, out _, out _);
                    if (isLong)
                    {
                        hasLongNote = true;
                        break;
                    }
                }

                if (!hasLongNote)
                {
                    continue;
                }

                MelonLogger.Msg($"[NoteDebug][{stage}] Context {c}: Layer {context.LayerIndex}, Area {context.AreaIndex}, Notes Count: {liveItems.Count}");
                for (int i = 0; i < liveItems.Count; i++)
                {
                    object? note = liveItems[i];
                    if (note == null)
                    {
                        continue;
                    }

                    string linkText = "?";
                    TryGetValueByNameCandidates(note, new[] { "property", "noteproperty" }, out object? propObj);
                    if (propObj != null)
                    {
                        TryGetValueByNameCandidates(propObj, new[] { "linked" }, out object? linkedObj);
                        linkText = linkedObj?.ToString() ?? "?";
                    }

                    bool isLong = (linkText != "None" && linkText != "?") || TryExtractHoldTiming(note, out _, out _, out _);
                    if (!isLong)
                    {
                        continue;
                    }

                    double? time = TryExtractNoteTime(note);
                    string timeText = time.HasValue ? time.Value.ToString("0.###") : "?";

                    TryGetValueByNameCandidates(note, new[] { "targetlaneuid" }, out object? laneUid);
                    string laneText = laneUid?.ToString() ?? "?";

                    int beatIndex = -1;
                    int beatSplit = -1;
                    TryGetValueByNameCandidates(note, new[] { "beatinfo" }, out object? beatInfoObj);
                    if (beatInfoObj != null)
                    {
                        Type biType = beatInfoObj.GetType();
                        FieldInfo? splitField = biType.GetField("BeatSplit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        FieldInfo? indexField = biType.GetField("BeatIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (splitField != null && indexField != null)
                        {
                            beatSplit = Convert.ToInt32(splitField.GetValue(beatInfoObj));
                            beatIndex = Convert.ToInt32(indexField.GetValue(beatInfoObj));
                        }
                    }

                    MelonLogger.Msg($"  [{i}]: Time={timeText}, LaneUID={laneText}, Link={linkText}, BeatIndex={beatIndex}, BeatSplit={beatSplit}");
                }
            }
            MelonLogger.Msg($"[NoteDebug][{stage}] ----------------------------------------------------");
        }

    }
}
