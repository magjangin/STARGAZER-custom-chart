using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static object? InstantiateIl2CppObject(Type type)
        {
            // 시도 1: ScriptableObject.CreateInstance (스크립터블 오브젝트인 경우)
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

            // 시도 2: 매개변수 없는 생성자 (public 또는 non-public)
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

            // 시도 3: Activator.CreateInstance 호출
            try
            {
                object? obj = Activator.CreateInstance(type);
                if (obj is not null)
                {
                    MelonLogger.Msg($"[Instantiate] Created {type.Name} using Activator.CreateInstance");
                    return obj;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Instantiate] Activator.CreateInstance failed for {type.Name}: {ex.Message}");
            }

            // 시도 4: 디버깅을 위해 생성자 정보를 출력합니다.
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

        private static bool TryReadBeatInfoPosition(object beatInfo, out int beatIndex, out int beatSplit)
        {
            beatIndex = 0;
            beatSplit = 0;
            Type beatInfoType = beatInfo.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? splitField = beatInfoType.GetField("BeatSplit", flags);
            FieldInfo? indexField = beatInfoType.GetField("BeatIndex", flags);
            if (splitField is null || indexField is null)
            {
                return false;
            }

            beatSplit = Convert.ToInt32(splitField.GetValue(beatInfo));
            beatIndex = Convert.ToInt32(indexField.GetValue(beatInfo));
            return true;
        }

        private static bool TryWriteBeatInfoPosition(object beatInfo, int beatIndex, int beatSplit)
        {
            Type beatInfoType = beatInfo.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? splitField = beatInfoType.GetField("BeatSplit", flags);
            FieldInfo? indexField = beatInfoType.GetField("BeatIndex", flags);
            if (splitField is null || indexField is null)
            {
                return false;
            }

            splitField.SetValue(beatInfo, beatSplit);
            indexField.SetValue(beatInfo, beatIndex);
            return true;
        }

        private static bool TryCalculateOffsetBeatInfoPosition(object beatInfo1, int beatOffset, int splitOffset, out int beatIndex2, out int beatSplit2)
        {
            beatIndex2 = 0;
            beatSplit2 = 0;
            if (!TryReadBeatInfoPosition(beatInfo1, out int beatIndex1, out int beatSplit1))
            {
                return false;
            }

            beatSplit2 = beatSplit1;
            beatIndex2 = beatIndex1 + (beatSplit1 * beatOffset) + splitOffset;

            return true;
        }

        private static bool TrySetLinkedState(object property, string stateName)
        {
            Type propType = property.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? linkedProp = propType.GetProperty("linked", flags)
                ?? propType.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, "linked", StringComparison.OrdinalIgnoreCase));

            if (linkedProp is not null && linkedProp.CanWrite)
            {
                try
                {
                    object state = Enum.Parse(linkedProp.PropertyType, stateName);
                    linkedProp.SetValue(property, state);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            FieldInfo? linkedField = propType.GetField("linked", flags)
                ?? propType.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, "linked", StringComparison.OrdinalIgnoreCase));
            if (linkedField is null)
            {
                return false;
            }

            try
            {
                object state = Enum.Parse(linkedField.FieldType, stateName);
                linkedField.SetValue(property, state);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryAddToNotesCollection(object notesValue, object note)
        {
            Type collectionType = notesValue.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo? addMethod = collectionType.GetMethods(flags)
                .FirstOrDefault(method => string.Equals(method.Name, "Add", StringComparison.Ordinal) && method.GetParameters().Length == 1);
            if (addMethod is null)
            {
                MelonLogger.Warning("[ExperimentChart] Add method not found on notes collection.");
                return false;
            }

            addMethod.Invoke(notesValue, new[] { note });
            return true;
        }

        private static bool TryApplyNotePropertyLinkedState(object sourceNote, object targetNote, string linkedState)
        {
            Type noteType = sourceNote.GetType();
            object? property1 = TryGetMemberValue(sourceNote, noteType, "property")
                ?? TryGetMemberValue(sourceNote, noteType, "Property")
                ?? TryGetMemberValue(sourceNote, noteType, "noteProperty")
                ?? TryGetMemberValue(sourceNote, noteType, "NoteProperty");

            if (property1 is null)
            {
                MelonLogger.Warning($"[ExperimentChart] source note property not found for linked={linkedState}.");
                return false;
            }

            Type propType = property1.GetType();
            object? property2 = InstantiateIl2CppObject(propType);
            if (property2 is null)
            {
                MelonLogger.Warning($"[ExperimentChart] failed to instantiate note property for linked={linkedState}.");
                return false;
            }

            object? exprHolder = TryGetMemberValue(property1, propType, "expressionHolder")
                ?? TryGetMemberValue(property1, propType, "expressionholder")
                ?? TryGetMemberValue(property1, propType, "ExpressionHolder");
            TrySetValueByNameCandidates(property2, new[] { "expressionholder" }, exprHolder);

            bool linked = TrySetLinkedState(property2, linkedState);
            bool written = TrySetValueByNameCandidates(targetNote, new[] { "property" }, property2);
            MelonLogger.Msg($"[ExperimentChart] property linked={linkedState} linkedSet={linked} written={written}");
            return linked && written;
        }

        private static bool TryCreateNoteAtOffset(object sourceNote, int beatOffset, int splitOffset, string linkedState, out object? newNote)
        {
            newNote = null;
            try
            {
                Type noteType = sourceNote.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                object? owner = FindOwnerArea(sourceNote);
                object? laneUid = TryGetMemberValue(sourceNote, noteType, "TargetLaneUID")
                    ?? TryGetMemberValue(sourceNote, noteType, "targetLaneUID")
                    ?? TryGetMemberValue(sourceNote, noteType, "targetlaneuid");
                object? beatInfo1 = FindBeatInfo(sourceNote);

                if (owner == null || laneUid == null || beatInfo1 == null)
                {
                    MelonLogger.Warning($"[ExperimentChart] Failed to duplicate note: missing owner={owner != null}, laneUid={laneUid != null}, beatInfo1={beatInfo1 != null}");
                    return false;
                }

                Type beatInfoType = beatInfo1.GetType();
                object? beatInfo2 = InstantiateIl2CppObject(beatInfoType);
                if (beatInfo2 == null)
                {
                    MelonLogger.Warning("[ExperimentChart] Failed to instantiate beatInfo2.");
                    return false;
                }

                if (!TryReadBeatInfoPosition(beatInfo1, out int indexVal, out int splitVal)
                    || !TryCalculateOffsetBeatInfoPosition(beatInfo1, beatOffset, splitOffset, out int newIndex, out int newSplit))
                {
                    MelonLogger.Warning("[ExperimentChart] BeatInfo BeatIndex/BeatSplit fields not found.");
                    return false;
                }

                TryWriteBeatInfoPosition(beatInfo2, newIndex, newSplit);

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
                        newNote = noteCtor3.Invoke(new[] { owner, laneUid, beatInfo2 });
                        MelonLogger.Msg("[ExperimentChart] Successfully created note using Note(Area, string, BeatInfo) constructor.");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[ExperimentChart] Note(Area, string, BeatInfo) constructor failed: {ex.Message}");
                    }
                }

                if (newNote == null)
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
                            newNote = noteCtor4.Invoke(new[] { owner, laneUid, newSplit, newIndex });
                            MelonLogger.Msg("[ExperimentChart] Successfully created note using Note(Area, string, int, int) constructor.");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[ExperimentChart] Note(Area, string, int, int) constructor failed: {ex.Message}");
                        }
                    }
                }

                if (newNote == null)
                {
                    newNote = InstantiateIl2CppObject(noteType);
                }

                if (newNote == null)
                {
                    MelonLogger.Warning("[ExperimentChart] All note instantiation strategies failed.");
                    return false;
                }

                TrySetValueByNameCandidates(newNote, new[] { "targetlaneuid" }, laneUid);
                TrySetValueByNameCandidates(newNote, new[] { "owner" }, owner);
                bool beatInfoWritten = TrySetValueByNameCandidates(newNote, new[] { "beatinfo" }, beatInfo2);
                bool propertyWritten = TryApplyNotePropertyLinkedState(sourceNote, newNote, linkedState);

                MelonLogger.Msg($"[ExperimentChart] created linked={linkedState} BeatIndex {indexVal}->{newIndex}, BeatSplit {splitVal}->{newSplit}, beatInfoWritten={beatInfoWritten}, propertyWritten={propertyWritten}");
                return beatInfoWritten && propertyWritten;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ExperimentChart] Failed to duplicate note: {ex.Message}");
            }

            return false;
        }

        private static bool TryAddExperimentChartNotes(object notesValue, object sourceNote)
        {
            int added = 0;

            if (ExperimentChartSettings.EnableShortNoteTest
                && TryCreateNoteAtOffset(
                    sourceNote,
                    ExperimentChartSettings.ShortNoteBeatOffset,
                    ExperimentChartSettings.ShortNoteSplitOffset,
                    "None",
                    out object? shortNote)
                && shortNote is not null
                && TryAddToNotesCollection(notesValue, shortNote))
            {
                added++;
                MelonLogger.Msg("[ExperimentChart] Added short note.");
            }

            if (ExperimentChartSettings.EnableLongNoteTest)
            {
                bool createdLongStart = TryCreateNoteAtOffset(
                    sourceNote,
                    ExperimentChartSettings.LongNoteStartBeatOffset,
                    ExperimentChartSettings.LongNoteStartSplitOffset,
                    "StartPoint",
                    out object? longStart);
                bool createdLongEnd = TryCreateNoteAtOffset(
                    sourceNote,
                    ExperimentChartSettings.LongNoteEndBeatOffset,
                    ExperimentChartSettings.LongNoteEndSplitOffset,
                    "EndPoint",
                    out object? longEnd);

                if (createdLongStart && longStart is not null && createdLongEnd && longEnd is not null)
                {
                    bool startAdded = TryAddToNotesCollection(notesValue, longStart);
                    bool endAdded = TryAddToNotesCollection(notesValue, longEnd);
                    if (startAdded && endAdded)
                    {
                        added += 2;
                        MelonLogger.Msg("[ExperimentChart] Added long note pair.");
                    }
                    else
                    {
                        MelonLogger.Warning($"[ExperimentChart] Long note pair add failed startAdded={startAdded} endAdded={endAdded}.");
                    }
                }
                else
                {
                    MelonLogger.Warning($"[ExperimentChart] Long note pair creation failed start={createdLongStart} end={createdLongEnd}.");
                }
            }

            MelonLogger.Msg($"[ExperimentChart] addedNotes={added} short={ExperimentChartSettings.EnableShortNoteTest} long={ExperimentChartSettings.EnableLongNoteTest}");
            return added > 0;
        }
    }
}
