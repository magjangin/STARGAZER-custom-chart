using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    // savecustomkey/config.txt 설정. hwa 폴더와 같은 위치(게임 루트)에 둔다.
    // 형식은 info.txt와 같은 "키 = 값" 또는 "키 : 값" 한 줄씩. '#'으로 시작하는 줄은 주석.
    // 파일이 없으면 기본값으로 새로 만들어 준다 — 사용자가 뭘 바꿀 수 있는지 파일 자체로 알 수 있도록.
    // 이미 있는 파일에 새 설정이 추가됐다면, 빠진 항목만 주석과 함께 파일 끝에 덧붙인다.
    internal static class CustomConfig
    {
        private const string FolderName = "savecustomkey";
        private const string FileName = "config.txt";

        private const string HeaderText =
            "# STARGAZER Custom Chart 설정\n"
            + "# 값을 바꾼 뒤 게임을 다시 시작하면 적용됩니다.\n";

        // 파일 생성과 "빠진 항목 덧붙이기"가 같은 정의를 쓰도록 한곳에 모아 둔다.
        private static readonly ConfigEntry[] Entries =
        {
            new ConfigEntry("autoplay", "false",
                "오토플레이 강제 사용 여부 (true = 자동 연주, false = 직접 플레이)"),

            new ConfigEntry("NoteSway", "0",
                "노트가 눈송이처럼 좌우로 흔들리며 내려오는 연출 (1 = 켜짐, 0 = 꺼짐)",
                "판정에는 전혀 영향이 없는 순수 시각 효과입니다."),
            new ConfigEntry("NoteSwayAmplitude", "20",
                "흔들림 폭 (픽셀). 너무 크면 레인 밖으로 나가 잘릴 수 있습니다."),
            new ConfigEntry("NoteSwaySpeed", "0.8",
                "흔들림 속도 (초당 왕복 횟수)"),
            new ConfigEntry("NoteSwayDamping", "1",
                "판정선에 가까워지면 흔들림을 잦아들게 함 (1 = 켜짐, 0 = 꺼짐)",
                "끄면 판정선에 닿는 순간까지 계속 흔들립니다."),
            new ConfigEntry("NoteSwayDampingTime", "0.4",
                "판정선 도달 몇 초 전부터 흔들림이 잦아들지"),

            new ConfigEntry("NoteSpeedChaos", "0",
                "[챌린지] 노트마다 낙하 속도를 제각각으로 (1 = 켜짐, 0 = 꺼짐)",
                "노트끼리 서로 추월하므로 읽기가 매우 어려워집니다. 판정에는 영향이 없습니다."),
            new ConfigEntry("NoteSpeedChaosMin", "0.6",
                "속도 배율 범위 (1 = 원래 속도). 예: 0.6 ~ 1.8"),
            new ConfigEntry("NoteSpeedChaosMax", "1.8") { GroupWithPrevious = true },
            new ConfigEntry("NoteSpeedChaosPerLane", "1",
                "1 = 레인마다 속도가 다름(같은 레인 안에서는 순서 유지, 읽을 수는 있음)",
                "0 = 노트마다 속도가 다름(완전 카오스)"),
        };

        private static bool _loaded;
        private static bool _autoPlay = true;
        private static bool _noteSway;
        private static float _noteSwayAmplitude = 20f;
        private static float _noteSwaySpeed = 0.8f;
        private static bool _noteSwayDamping = true;
        private static float _noteSwayDampingTime = 0.4f;
        private static bool _noteSpeedChaos;
        private static float _noteSpeedChaosMin = 0.6f;
        private static float _noteSpeedChaosMax = 1.8f;
        private static bool _noteSpeedChaosPerLane = true;

        // 기본값 true는 기존 동작(오토플레이 강제)과 같다. config.txt가 생기면 그 값이 우선한다.
        public static bool AutoPlay
        {
            get
            {
                EnsureLoaded();
                return _autoPlay;
            }
        }

        public static bool NoteSway
        {
            get
            {
                EnsureLoaded();
                return _noteSway;
            }
        }

        public static float NoteSwayAmplitude
        {
            get
            {
                EnsureLoaded();
                return _noteSwayAmplitude;
            }
        }

        public static float NoteSwaySpeed
        {
            get
            {
                EnsureLoaded();
                return _noteSwaySpeed;
            }
        }

        public static bool NoteSwayDamping
        {
            get
            {
                EnsureLoaded();
                return _noteSwayDamping;
            }
        }

        public static float NoteSwayDampingTime
        {
            get
            {
                EnsureLoaded();
                return _noteSwayDampingTime;
            }
        }

        public static bool NoteSpeedChaos
        {
            get
            {
                EnsureLoaded();
                return _noteSpeedChaos;
            }
        }

        public static float NoteSpeedChaosMin
        {
            get
            {
                EnsureLoaded();
                return _noteSpeedChaosMin;
            }
        }

        public static float NoteSpeedChaosMax
        {
            get
            {
                EnsureLoaded();
                return _noteSpeedChaosMax;
            }
        }

        public static bool NoteSpeedChaosPerLane
        {
            get
            {
                EnsureLoaded();
                return _noteSpeedChaosPerLane;
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
                    File.WriteAllText(FilePath, BuildDefaultContents());
                    MelonLogger.Msg($"[Config] 기본 설정 파일을 만들었습니다: {FilePath}");
                }

                Dictionary<string, string> values = ParseFile(FilePath);
                AppendMissingEntries(values);

                _autoPlay = ReadBool(values, "autoplay", _autoPlay);

                _noteSway = ReadBool(values, "NoteSway", _noteSway);
                _noteSwayAmplitude = Math.Max(0f, ReadFloat(values, "NoteSwayAmplitude", _noteSwayAmplitude));
                _noteSwaySpeed = Math.Max(0f, ReadFloat(values, "NoteSwaySpeed", _noteSwaySpeed));
                _noteSwayDamping = ReadBool(values, "NoteSwayDamping", _noteSwayDamping);
                _noteSwayDampingTime = Math.Max(0f, ReadFloat(values, "NoteSwayDampingTime", _noteSwayDampingTime));

                _noteSpeedChaos = ReadBool(values, "NoteSpeedChaos", _noteSpeedChaos);
                _noteSpeedChaosMin = ReadFloat(values, "NoteSpeedChaosMin", _noteSpeedChaosMin);
                _noteSpeedChaosMax = ReadFloat(values, "NoteSpeedChaosMax", _noteSpeedChaosMax);
                _noteSpeedChaosPerLane = ReadBool(values, "NoteSpeedChaosPerLane", _noteSpeedChaosPerLane);
                NormalizeSpeedChaosRange();

                MelonLogger.Msg($"[Config] autoplay={_autoPlay}");
                MelonLogger.Msg($"[Config] NoteSway={_noteSway} amplitude={_noteSwayAmplitude} speed={_noteSwaySpeed} damping={_noteSwayDamping}/{_noteSwayDampingTime}s");
                MelonLogger.Msg($"[Config] NoteSpeedChaos={_noteSpeedChaos} range={_noteSpeedChaosMin}~{_noteSpeedChaosMax} perLane={_noteSpeedChaosPerLane}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config] 설정을 읽지 못해 기본값을 사용합니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 배율이 0 이하면 노트가 멈추거나 뒤집혀 날아간다. 뒤집힌 범위도 여기서 바로잡는다.
        private static void NormalizeSpeedChaosRange()
        {
            const float MinAllowed = 0.05f;

            if (_noteSpeedChaosMin < MinAllowed || _noteSpeedChaosMax < MinAllowed)
            {
                MelonLogger.Warning($"[Config] NoteSpeedChaos 배율은 {MinAllowed} 이상이어야 합니다. 값을 올려서 씁니다.");
                _noteSpeedChaosMin = Math.Max(MinAllowed, _noteSpeedChaosMin);
                _noteSpeedChaosMax = Math.Max(MinAllowed, _noteSpeedChaosMax);
            }

            if (_noteSpeedChaosMin > _noteSpeedChaosMax)
            {
                MelonLogger.Warning("[Config] NoteSpeedChaosMin이 Max보다 큽니다. 두 값을 바꿔서 씁니다.");
                (_noteSpeedChaosMin, _noteSpeedChaosMax) = (_noteSpeedChaosMax, _noteSpeedChaosMin);
            }
        }

        private static string BuildDefaultContents()
        {
            var builder = new StringBuilder();
            builder.Append(HeaderText);
            AppendEntries(builder, Entries);
            return builder.ToString();
        }

        // 항목 사이에 빈 줄을 넣되, 앞 항목과 설명을 공유하는 항목(Min/Max 같은 짝)은 붙여 쓴다.
        private static void AppendEntries(StringBuilder builder, IReadOnlyList<ConfigEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ConfigEntry entry = entries[i];
                if (i == 0 || !entry.GroupWithPrevious)
                {
                    builder.Append('\n');
                }

                entry.AppendTo(builder);
            }
        }

        // 모드를 업데이트해서 설정이 늘어나도 사용자가 파일을 지웠다 다시 만들지 않아도 되게 한다.
        private static void AppendMissingEntries(Dictionary<string, string> values)
        {
            var missing = new List<ConfigEntry>();
            foreach (ConfigEntry entry in Entries)
            {
                if (!values.ContainsKey(entry.Key))
                {
                    missing.Add(entry);
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append("\n# --- 아래 항목은 모드가 업데이트되면서 추가됐습니다 ---\n");
            AppendEntries(builder, missing);
            foreach (ConfigEntry entry in missing)
            {
                values[entry.Key] = entry.DefaultValue;
            }

            try
            {
                // 기존 파일이 줄바꿈으로 끝나지 않으면 마지막 설정 줄에 붙어 버린다.
                if (!EndsWithNewLine(FilePath))
                {
                    File.AppendAllText(FilePath, "\n");
                }

                File.AppendAllText(FilePath, builder.ToString());
                MelonLogger.Msg($"[Config] 새로 생긴 설정 {missing.Count}개를 config.txt에 추가했습니다.");
            }
            catch (Exception ex)
            {
                // 파일에 못 써도 기본값으로 계속 돌아가면 되므로 치명적이지 않다.
                MelonLogger.Warning($"[Config] 새 설정을 파일에 추가하지 못했습니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool EndsWithNewLine(string path)
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length == 0)
            {
                return true;
            }

            stream.Seek(-1, SeekOrigin.End);
            return stream.ReadByte() == '\n';
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

        private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out string? text) ? ParseBool(text, key, fallback) : fallback;
        }

        private static float ReadFloat(Dictionary<string, string> values, string key, float fallback)
        {
            return values.TryGetValue(key, out string? text) ? ParseFloat(text, key, fallback) : fallback;
        }

        private static bool ParseBool(string text, string key, bool fallback)
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
                    MelonLogger.Warning($"[Config] {key} 값을 해석하지 못해 기본값({fallback})을 씁니다: {text}");
                    return fallback;
            }
        }

        // 사용자가 어느 지역 설정을 쓰든 "0.8"이 그대로 읽히도록 InvariantCulture로 고정한다.
        private static float ParseFloat(string text, string key, float fallback)
        {
            if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                && !float.IsNaN(value)
                && !float.IsInfinity(value))
            {
                return value;
            }

            MelonLogger.Warning($"[Config] {key} 값을 숫자로 읽지 못해 기본값({fallback})을 씁니다: {text}");
            return fallback;
        }

        private sealed class ConfigEntry
        {
            public ConfigEntry(string key, string defaultValue, params string[] comments)
            {
                Key = key;
                DefaultValue = defaultValue;
                Comments = comments;
            }

            public string Key { get; }
            public string DefaultValue { get; }
            public string[] Comments { get; }

            // 앞 항목의 설명이 이 항목까지 설명하는 경우(Min/Max 짝) 빈 줄 없이 붙여 쓴다.
            public bool GroupWithPrevious { get; init; }

            public void AppendTo(StringBuilder builder)
            {
                foreach (string comment in Comments)
                {
                    builder.Append("# ").Append(comment).Append('\n');
                }

                builder.Append(Key).Append('=').Append(DefaultValue).Append('\n');
            }
        }
    }
}
