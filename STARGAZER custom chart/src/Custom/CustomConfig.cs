using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    // savecustomkey/config.txt 설정. hwa 폴더와 같은 위치(게임 루트)에 둔다.
    // 형식은 info.txt와 같은 "키 = 값" 또는 "키 : 값" 한 줄씩. '#'으로 시작하는 줄은 주석.
    // 파일이 없으면 기본값으로 새로 만들어 준다 — 사용자가 뭘 바꿀 수 있는지 파일 자체로 알 수 있도록.
    internal static class CustomConfig
    {
        private const string FolderName = "savecustomkey";
        private const string FileName = "config.txt";

        private const string DefaultContents =
            "# STARGAZER Custom Chart 설정\n"
            + "# 값을 바꾼 뒤 게임을 다시 시작하면 적용됩니다.\n"
            + "\n"
            + "# 오토플레이 강제 사용 여부 (true = 자동 연주, false = 직접 플레이)\n"
            + "autoplay = false\n";

        private static bool _loaded;
        private static bool _autoPlay = true;

        // 기본값 true는 기존 동작(오토플레이 강제)과 같다. config.txt가 생기면 그 값이 우선한다.
        public static bool AutoPlay
        {
            get
            {
                EnsureLoaded();
                return _autoPlay;
            }
        }

        public static string FolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName);

        public static string FilePath => Path.Combine(FolderPath, FileName);

        public static void Reset()
        {
            _loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;

            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                    MelonLogger.Msg($"[Config] 설정 폴더를 만들었습니다: {FolderPath}");
                }

                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, DefaultContents);
                    MelonLogger.Msg($"[Config] 기본 설정 파일을 만들었습니다: {FilePath}");
                }

                Dictionary<string, string> values = ParseFile(FilePath);
                if (values.TryGetValue("autoplay", out string? autoPlayText))
                {
                    _autoPlay = ParseBool(autoPlayText, _autoPlay);
                }

                MelonLogger.Msg($"[Config] autoplay={_autoPlay}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config] 설정을 읽지 못해 기본값을 사용합니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Dictionary<string, string> ParseFile(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int sepIndex = line.IndexOfAny(new[] { '=', ':' });
                if (sepIndex <= 0 || sepIndex >= line.Length - 1)
                {
                    continue;
                }

                string key = line.Substring(0, sepIndex).Trim();
                string value = line.Substring(sepIndex + 1).Trim();
                if (key.Length > 0 && value.Length > 0)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "true":
                case "on":
                case "yes":
                case "1":
                case "켜기":
                case "사용":
                    return true;
                case "false":
                case "off":
                case "no":
                case "0":
                case "끄기":
                case "사용안함":
                    return false;
                default:
                    MelonLogger.Warning($"[Config] 값을 해석하지 못해 기본값({fallback})을 씁니다: {text}");
                    return fallback;
            }
        }
    }
}
