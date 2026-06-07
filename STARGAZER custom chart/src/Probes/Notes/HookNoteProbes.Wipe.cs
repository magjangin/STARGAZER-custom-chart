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
                MelonLogger.Warning("[LaneShiftTest] selected note lane not found.");
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
                    MelonLogger.Msg($"[LaneShiftTest] selected note lane {currentLaneText}->{candidateLane} changed={changed}");
                    return;
                }
            }

            MelonLogger.Warning($"[LaneShiftTest] alternate lane not found. current={currentLaneText}");
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

        private static object? InstantiateIl2CppObject(Type type)
        {
            // Try 1: ScriptableObject.CreateInstance (if it is a ScriptableObject)
            try
            {
                Type? scriptableObjectType = Type.GetType("UnityEngine.ScriptableObject, UnityEngine.CoreModule")
                    ?? Type.GetType("UnityEngine.ScriptableObject, UnityEngine");
                if (scriptableObjectType != null && scriptableObjectType.IsAssignableFrom(type))
                {
                    MethodInfo? createInstanceMethod = scriptableObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => string.Equals(m.Name, "CreateInstance", StringComparison.Ordinal)
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType == typeof(Type));
                    if (createInstanceMethod != null)
                    {
                        object? obj = createInstanceMethod.Invoke(null, new object[] { type });
                        if (obj != null)
                        {
                            MelonLogger.Msg($"[Instantiate] Created {type.Name} using ScriptableObject.CreateInstance");
                            return obj;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Instantiate] ScriptableObject.CreateInstance failed for {type.Name}: {ex.Message}");
            }

            // Try 2: Parameterless constructor (public or non-public)
            try
            {
                ConstructorInfo? paramCtor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (paramCtor != null)
                {
                    object obj = paramCtor.Invoke(null);
                    MelonLogger.Msg($"[Instantiate] Created {type.Name} using empty constructor");
                    return obj;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Instantiate] Empty constructor failed for {type.Name}: {ex.Message}");
            }

            // Try 3: Call Activator.CreateInstance
            try
            {
                object obj = Activator.CreateInstance(type);
                MelonLogger.Msg($"[Instantiate] Created {type.Name} using Activator.CreateInstance");
                return obj;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Instantiate] Activator.CreateInstance failed for {type.Name}: {ex.Message}");
            }

            // Try 4: Log constructors to help debug
            try
            {
                ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MelonLogger.Msg($"[Instantiate] Available constructors for {type.FullName}: {ctors.Length}");
                foreach (ConstructorInfo ctor in ctors)
                {
                    string paramsText = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                    MelonLogger.Msg($"  Ctor: {type.Name}({paramsText})");
                }
            }
            catch
            {
            }

            return null;
        }

        private static object? FindOwnerArea(object note)
        {
            Type type = note.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            
            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (prop.CanRead && prop.PropertyType.Name.Contains("Area") && prop.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        object? val = prop.GetValue(note);
                        if (val != null) return val;
                    }
                    catch {}
                }
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.FieldType.Name.Contains("Area"))
                {
                    try
                    {
                        object? val = field.GetValue(note);
                        if (val != null) return val;
                    }
                    catch {}
                }
            }

            return TryGetMemberValue(note, type, "Owner")
                ?? TryGetMemberValue(note, type, "owner")
                ?? TryGetMemberValue(note, type, "_owner");
        }

        private static object? FindBeatInfo(object note)
        {
            Type type = note.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            
            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (prop.CanRead && prop.PropertyType.Name.Contains("BeatInfo") && prop.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        object? val = prop.GetValue(note);
                        if (val != null) return val;
                    }
                    catch {}
                }
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.FieldType.Name.Contains("BeatInfo"))
                {
                    try
                    {
                        object? val = field.GetValue(note);
                        if (val != null) return val;
                    }
                    catch {}
                }
            }

            return TryGetMemberValue(note, type, "beatInfo")
                ?? TryGetMemberValue(note, type, "BeatInfo");
        }

        private static bool TryDuplicateAndLinkAsLongNote(object notesValue, object note1, out object? note2)
        {
            note2 = null;
            try
            {
                Type noteType = note1.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Extract properties/fields from note1
                object? owner = FindOwnerArea(note1);
                object? laneUid = TryGetMemberValue(note1, noteType, "TargetLaneUID")
                    ?? TryGetMemberValue(note1, noteType, "targetLaneUID")
                    ?? TryGetMemberValue(note1, noteType, "targetlaneuid");
                object? beatInfo1 = FindBeatInfo(note1);

                if (owner == null || laneUid == null || beatInfo1 == null)
                {
                    MelonLogger.Warning($"[LongNoteTest] Failed to duplicate note: missing owner={owner != null}, laneUid={laneUid != null}, beatInfo1={beatInfo1 != null}");
                    return false;
                }

                // Create beatInfo2
                Type beatInfoType = beatInfo1.GetType();
                object? beatInfo2 = InstantiateIl2CppObject(beatInfoType);
                if (beatInfo2 == null)
                {
                    MelonLogger.Warning("[LongNoteTest] Failed to instantiate beatInfo2.");
                    return false;
                }

                FieldInfo? splitField = beatInfoType.GetField("BeatSplit", flags);
                FieldInfo? indexField = beatInfoType.GetField("BeatIndex", flags);
                int splitVal = 192;
                int indexVal = 0;
                if (splitField != null && indexField != null)
                {
                    splitVal = Convert.ToInt32(splitField.GetValue(beatInfo1));
                    indexVal = Convert.ToInt32(indexField.GetValue(beatInfo1));
                    splitField.SetValue(beatInfo2, splitVal);
                    indexField.SetValue(beatInfo2, indexVal + (splitVal * 2));
                }

                // Try to create note2 using Constructor (Area, string, BeatInfo)
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

                if (noteCtor3 != null)
                {
                    try
                    {
                        note2 = noteCtor3.Invoke(new[] { owner, laneUid, beatInfo2 });
                        MelonLogger.Msg("[LongNoteTest] Successfully created note2 using Note(Area, string, BeatInfo) constructor.");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[LongNoteTest] Note(Area, string, BeatInfo) constructor failed: {ex.Message}");
                    }
                }

                // Try fallback 1: Note(Area, string, int, int)
                if (note2 == null)
                {
                    ConstructorInfo? noteCtor4 = null;
                    foreach (ConstructorInfo ctor in noteType.GetConstructors(flags))
                    {
                        ParameterInfo[] parameters = ctor.GetParameters();
                        if (parameters.Length == 4
                            && parameters[0].ParameterType.Name.Contains("Area")
                            && parameters[1].ParameterType == typeof(string)
                            && parameters[2].ParameterType == typeof(int)
                            && parameters[3].ParameterType == typeof(int))
                        {
                            noteCtor4 = ctor;
                            break;
                        }
                    }

                    if (noteCtor4 != null)
                    {
                        try
                        {
                            int newIndex = indexVal + (splitVal * 2);
                            note2 = noteCtor4.Invoke(new[] { owner, laneUid, splitVal, newIndex });
                            MelonLogger.Msg("[LongNoteTest] Successfully created note2 using Note(Area, string, int, int) constructor.");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[LongNoteTest] Note(Area, string, int, int) constructor failed: {ex.Message}");
                        }
                    }
                }

                // Try fallback 2: Parameterless instantiation (if any)
                if (note2 == null)
                {
                    note2 = InstantiateIl2CppObject(noteType);
                }

                if (note2 == null)
                {
                    MelonLogger.Warning("[LongNoteTest] All note instantiation strategies failed.");
                    return false;
                }

                // Set/Copy members just in case
                TrySetValueByNameCandidates(note2, new[] { "targetlaneuid" }, laneUid);
                TrySetValueByNameCandidates(note2, new[] { "owner" }, owner);
                TrySetValueByNameCandidates(note2, new[] { "beatinfo" }, beatInfo2);

                // 5. Create and copy NoteProperty, then set linked status
                object? property1 = TryGetMemberValue(note1, noteType, "property")
                    ?? TryGetMemberValue(note1, noteType, "Property")
                    ?? TryGetMemberValue(note1, noteType, "noteProperty")
                    ?? TryGetMemberValue(note1, noteType, "NoteProperty");
                
                if (property1 != null)
                {
                    Type propType = property1.GetType();
                    object? property2 = InstantiateIl2CppObject(propType);
                    if (property2 != null)
                    {
                        // Copy expressionHolder
                        object? exprHolder = TryGetMemberValue(property1, propType, "expressionHolder")
                            ?? TryGetMemberValue(property1, propType, "expressionholder")
                            ?? TryGetMemberValue(property1, propType, "ExpressionHolder");
                        TrySetValueByNameCandidates(property2, new[] { "expressionholder" }, exprHolder);

                        // Get linked enum type and parse values
                        PropertyInfo? linkedProp = propType.GetProperty("linked", flags)
                            ?? propType.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, "linked", StringComparison.OrdinalIgnoreCase));
                        
                        if (linkedProp != null && linkedProp.CanWrite)
                        {
                            Type enumType = linkedProp.PropertyType;
                            object startPointEnum = Enum.Parse(enumType, "StartPoint");
                            object endPointEnum = Enum.Parse(enumType, "EndPoint");

                            linkedProp.SetValue(property1, startPointEnum);
                            linkedProp.SetValue(property2, endPointEnum);
                        }
                        else
                        {
                            FieldInfo? linkedField = propType.GetField("linked", flags)
                                ?? propType.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, "linked", StringComparison.OrdinalIgnoreCase));
                            if (linkedField != null)
                            {
                                Type enumType = linkedField.FieldType;
                                object startPointEnum = Enum.Parse(enumType, "StartPoint");
                                object endPointEnum = Enum.Parse(enumType, "EndPoint");

                                linkedField.SetValue(property1, startPointEnum);
                                linkedField.SetValue(property2, endPointEnum);
                            }
                        }

                        // Write back both properties to their notes
                        TrySetValueByNameCandidates(note1, new[] { "property" }, property1);
                        TrySetValueByNameCandidates(note2, new[] { "property" }, property2);
                    }
                }

                // 6. Copy time / hitTime candidate fields and set for end note (+ 2.0 beats)
                double? time1 = TryExtractNoteTime(note1);
                if (time1.HasValue)
                {
                    double time2 = time1.Value + 2.0;
                    TrySetValueByNameCandidates(note2, new[] { "time", "timing", "start", "starttime", "hittime", "hittiming", "judge", "tick", "beat", "position", "ms" }, time2);
                }

                // 7. Add note2 to notesValue collection
                Type collectionType = notesValue.GetType();
                MethodInfo? addMethod = collectionType.GetMethods(flags)
                    .FirstOrDefault(method => string.Equals(method.Name, "Add", StringComparison.Ordinal) && method.GetParameters().Length == 1);
                
                if (addMethod != null)
                {
                    addMethod.Invoke(notesValue, new[] { note2 });
                    MelonLogger.Msg("[LongNoteTest] Successfully duplicated note, linked as long note (StartPoint -> EndPoint), and added to collection.");
                    return true;
                }
                else
                {
                    MelonLogger.Warning("[LongNoteTest] Add method not found on notes collection.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LongNoteTest] Failed to duplicate and link note: {ex.Message}");
            }
            return false;
        }
    }
}
