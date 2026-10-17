using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static string TryScanForNumericDifficulty(object? obj)
        {
            if (obj is null) return "?";
            try
            {
                Type type = obj.GetType();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                
                // 1. level/difficulty를 포함하는 정확/부분 이름의 필드 또는 프로퍼티를 찾습니다.
                string[] targetCandidates = { "level", "difficulty", "lv", "val", "value" };
                
                // 먼저 프로퍼티를 시도합니다.
                foreach (var prop in type.GetProperties(Flags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    string name = prop.Name.ToLowerInvariant();
                    bool matches = false;
                    foreach (string candidate in targetCandidates)
                    {
                        if (name.Contains(candidate, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches) continue;
                    
                    try
                    {
                        object? val = prop.GetValue(obj);
                        if (val is not null && IsNumericAndPositive(val, out string strVal))
                        {
                            return strVal;
                        }
                    }
                    catch {}
                }
                
                // 필드를 시도합니다.
                foreach (var field in type.GetFields(Flags))
                {
                    string name = field.Name.ToLowerInvariant();
                    bool matches = false;
                    foreach (string candidate in targetCandidates)
                    {
                        if (name.Contains(candidate, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches) continue;
                    
                    try
                    {
                        object? val = field.GetValue(obj);
                        if (val is not null && IsNumericAndPositive(val, out string strVal))
                        {
                            return strVal;
                        }
                    }
                    catch {}
                }
                
                // 2. TextProvider처럼 보이면 텍스트 값을 추출해 봅니다.
                object? textVal = TryGetMemberValue(obj, type, "text")
                                  ?? TryGetMemberValue(obj, type, "Text")
                                  ?? TryGetMemberValue(obj, type, "string");
                if (textVal is not null)
                {
                    string textStr = textVal.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(textStr) && textStr != "?" && int.TryParse(textStr, out int num) && num > 0)
                    {
                        return textStr;
                    }
                }
            }
            catch {}
            
            return "?";
        }
        
        private static bool IsNumericAndPositive(object val, out string stringValue)
        {
            stringValue = "?";
            if (val is int iVal)
            {
                if (iVal > 0 && iVal < 100) { stringValue = iVal.ToString(); return true; }
            }
            else if (val is long lVal)
            {
                if (lVal > 0 && lVal < 100) { stringValue = lVal.ToString(); return true; }
            }
            else if (val is short sVal)
            {
                if (sVal > 0 && sVal < 100) { stringValue = sVal.ToString(); return true; }
            }
            else if (val is byte bVal)
            {
                if (bVal > 0 && bVal < 100) { stringValue = bVal.ToString(); return true; }
            }
            else if (val is float fVal)
            {
                if (fVal > 0f && fVal < 100f) { stringValue = ((int)fVal).ToString(); return true; }
            }
            else if (val is double dVal)
            {
                if (dVal > 0d && dVal < 100d) { stringValue = ((int)dVal).ToString(); return true; }
            }
            else if (val is string str)
            {
                if (int.TryParse(str, out int num) && num > 0 && num < 100)
                {
                    stringValue = str;
                    return true;
                }
            }
            return false;
        }
    }
}
