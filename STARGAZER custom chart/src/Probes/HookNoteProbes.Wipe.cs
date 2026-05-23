using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
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
                MelonLogger.Warning("[BeatShiftTest] beatInfo not found on kept note.");
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
                MelonLogger.Warning("[BeatShiftTest] BeatSplit/BeatIndex fields not found.");
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
                MelonLogger.Warning($"[BeatShiftTest] failed to read BeatInfo fields: {ex.GetType().Name}");
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
                    MelonLogger.Warning("[BeatShiftTest] beatInfo write-back to note failed.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BeatShiftTest] failed to write BeatInfo fields: {ex.GetType().Name}");
                return;
            }

            TryGetDoubleByNameCandidates(note, new[] { "beatvalue" }, out double? afterBeatValue);
            string afterHash = TryGetValueByNameCandidates(note, new[] { "uniquehash" }, out object? afterHashObj)
                ? (afterHashObj?.ToString() ?? "?")
                : "?";
            string beforeText = beforeBeatValue.HasValue ? beforeBeatValue.Value.ToString("0.###") : "?";
            string afterText = afterBeatValue.HasValue ? afterBeatValue.Value.ToString("0.###") : "?";
            MelonLogger.Msg($"[BeatShiftTest] keptNote L{keepChoice.Context.LayerIndex}A{keepChoice.Context.AreaIndex}[{keepChoice.NoteIndex}] BeatIndex {oldIndex}->{newIndex}, BeatSplit {oldSplit}->{newSplit}, BeatValue {beforeText}->{afterText}, UniqueHash {beforeHash}->{afterHash}");
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
    }
}
