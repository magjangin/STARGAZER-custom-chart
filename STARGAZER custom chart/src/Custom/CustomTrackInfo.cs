using System;
using System.Collections.Generic;
using System.IO;

namespace STARGAZER_custom_chart
{
    // hwa/info.txt 파서. 게임/리플렉션 의존성 없는 순수 텍스트 파싱.
    // 형식: 한 줄에 "키 : 값" 또는 "키 = 값". 곡 제목/아티스트/난이도(cosmic,stellar,void)를 인식한다.
    internal sealed class CustomTrackInfo
    {
        private static readonly char[] InfoSeparators = { ':', '=' };

        public string? Title { get; private set; }
        public string? Artist { get; private set; }
        public Dictionary<string, string> Levels { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static CustomTrackInfo? TryParse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var info = new CustomTrackInfo();
            foreach (string rawLine in SafeTextReader.ReadAllLines(filePath))
            {
                string line = rawLine.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                int sepIndex = line.IndexOfAny(InfoSeparators);
                if (sepIndex <= 0 || sepIndex >= line.Length - 1)
                {
                    continue;
                }

                string key = line.Substring(0, sepIndex).Trim();
                string value = line.Substring(sepIndex + 1).Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (string.Equals(key, "곡 제목", StringComparison.Ordinal)
                    || string.Equals(key, "제목", StringComparison.Ordinal)
                    || string.Equals(key, "title", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "曲名", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "歌曲名称", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "歌名", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "标题", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "曲目", StringComparison.OrdinalIgnoreCase))
                {
                    info.Title = value;
                }
                else if (string.Equals(key, "아티스트", StringComparison.Ordinal)
                    || string.Equals(key, "artist", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "composer", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "작곡", StringComparison.Ordinal)
                    || string.Equals(key, "艺术家", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "作曲", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "曲师", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "曲目作者", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "作者", StringComparison.OrdinalIgnoreCase))
                {
                    info.Artist = value;
                }
                else if (string.Equals(key, "cosmic", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "stellar", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "void", StringComparison.OrdinalIgnoreCase))
                {
                    info.Levels[key.ToLowerInvariant()] = value;
                }
                else if (string.Equals(key, "宇宙", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "星空", StringComparison.OrdinalIgnoreCase))
                {
                    info.Levels["cosmic"] = value;
                }
                else if (string.Equals(key, "恒星", StringComparison.OrdinalIgnoreCase))
                {
                    info.Levels["stellar"] = value;
                }
                else if (string.Equals(key, "虚空", StringComparison.OrdinalIgnoreCase))
                {
                    info.Levels["void"] = value;
                }
            }

            return info;
        }
    }
}
