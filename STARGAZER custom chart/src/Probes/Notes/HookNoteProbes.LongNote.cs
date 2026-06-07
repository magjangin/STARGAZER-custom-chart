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
