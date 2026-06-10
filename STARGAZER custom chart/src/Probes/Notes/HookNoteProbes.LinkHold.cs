using System;
using System.Collections.Generic;
using System.Reflection;

namespace STARGAZER_custom_chart
{
    public sealed partial class GameTypeEnumeratorMod
    {
        private static bool TryExtractLinkGroupKey(object note, out string? key)
        {
            key = null;
            string[] keyNames =
            {
                "linkid", "linkgroup", "groupid", "chainid", "holdid", "pairid",
                "headid", "tailid", "nextid", "previd", "linkedid", "group",
                "next", "prev", "head", "tail", "parent", "child", "joint", "connect", "line"
            };

            if (TryGetValueByNameCandidates(note, keyNames, out object? value) && value is not null)
            {
                string text = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, "0", StringComparison.Ordinal))
                {
                    key = text;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractHoldTiming(object note, out double? start, out double? end, out double? duration)
        {
            start = null;
            end = null;
            duration = null;

            TryGetDoubleByNameCandidates(note, new[] { "start", "starttime", "starttiming", "headtime", "headoffset", "from", "begin", "spos" }, out start);
            TryGetDoubleByNameCandidates(note, new[] { "end", "endtime", "endtiming", "tailtime", "tailoffset", "to", "finish", "epos" }, out end);
            TryGetDoubleByNameCandidates(note, new[] { "duration", "length", "holdtime", "holdlength", "len", "span" }, out duration);

            if (duration.HasValue && duration.Value > 0.0001)
            {
                return true;
            }

            if (start.HasValue && end.HasValue && end.Value - start.Value > 0.0001)
            {
                duration = end.Value - start.Value;
                return true;
            }

            if (TryGetValueByNameCandidates(note, new[] { "type", "notetype", "ticktype", "kind" }, out object? typeObj) && typeObj is not null)
            {
                string typeText = typeObj.ToString() ?? string.Empty;
                if (typeText.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeText.IndexOf("long", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeText.IndexOf("link", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // 일부 게임은 enum 값을 ToString()에서 숫자로 노출합니다.
                if (double.TryParse(typeText, out double numericType) && numericType > 0)
                {
                    return true;
                }
            }

            if (TryGetValueByNameCandidates(note, new[] { "islong", "ishold", "islink", "linked", "connect" }, out object? linkedBool) && linkedBool is bool linked && linked)
            {
                return true;
            }

            return false;
        }

        private static string BuildHoldSampleText(object note, double? start, double? end, double? duration)
        {
            string typeName = note.GetType().Name;
            string startText = start.HasValue ? start.Value.ToString("0.###") : "?";
            string endText = end.HasValue ? end.Value.ToString("0.###") : "?";
            string durationText = duration.HasValue ? duration.Value.ToString("0.###") : "?";
            string linkKey = TryExtractLinkGroupKey(note, out string? key) && key is not null ? key : "-";
            return $"{typeName}(start={startText},end={endText},dur={durationText},group={linkKey})";
        }

    }
}
