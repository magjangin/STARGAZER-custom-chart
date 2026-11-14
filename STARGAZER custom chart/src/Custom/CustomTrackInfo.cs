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

        private static readonly HashSet<string> TitleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "곡 제목", "제목", "title", "name", "曲名", "歌曲名称", "歌名", "标题", "曲目",
        };

        private static readonly HashSet<string> ArtistKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "아티스트", "artist", "composer", "작곡", "艺术家", "作曲", "曲师", "曲目作者", "作者",
        };

        private static readonly Dictionary<string, string> LevelKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "cosmic", "cosmic" },
            { "宇宙", "cosmic" },
            { "星空", "cosmic" },
            { "stellar", "stellar" },
            { "恒星", "stellar" },
            { "void", "void" },
            { "虚空", "void" },
        };

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
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
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

                if (TitleKeys.Contains(key))
                {
                    info.Title = value;
                }
                else if (ArtistKeys.Contains(key))
                {
                    info.Artist = value;
                }
                else if (LevelKeyMap.TryGetValue(key, out string? canonicalLevel))
                {
                    info.Levels[canonicalLevel] = value;
                }
            }

            return info;
        }
    }
}
