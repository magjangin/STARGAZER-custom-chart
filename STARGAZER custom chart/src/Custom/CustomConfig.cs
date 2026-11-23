using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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

        // 설정은 [HarmonyPatch] 클래스의 Prepare(예: NoteMotionPatches → NoteSway)가 처음 읽는데, 그 시점은
        // ModEntry가 로거를 연결하기 전이다(2026-11-24 실측: [Config] 로그가 한 줄도 남지 않음 — 값 해석 경고까지 버려졌다).
        // 그래서 로거가 붙기 전 메시지는 모아 두었다가 AttachLogger에서 한꺼번에 내보낸다.
        private static readonly object PendingLogsLock = new object();
        private static List<(bool IsWarning, string Message)>? _pendingLogs = new List<(bool, string)>();
        private static Action<string>? _logInfoSink;
        private static Action<string>? _logWarningSink;

        internal static void LogInfo(string message) => Emit(false, message);

        internal static void LogWarning(string message) => Emit(true, message);

        private static void Emit(bool isWarning, string message)
        {
            lock (PendingLogsLock)
            {
                Action<string>? sink = isWarning ? _logWarningSink : _logInfoSink;
                if (sink is null)
                {
                    // 상한: 로거가 끝내 안 붙는 환경(단위 테스트)에서 무한히 쌓이지 않게.
                    if (_pendingLogs is { Count: < 256 })
                    {
                        _pendingLogs.Add((isWarning, message));
                    }

                    return;
                }

                sink(message);
            }
        }

        internal static void AttachLogger(Action<string> info, Action<string> warning)
        {
            lock (PendingLogsLock)
            {
                _logInfoSink = info;
                _logWarningSink = warning;
                if (_pendingLogs is null)
                {
                    return;
                }

                foreach ((bool isWarning, string message) in _pendingLogs)
                {
                    (isWarning ? warning : info)(message);
                }

                _pendingLogs = null;
            }
        }

        // MelonLoader 0.7(net6)에서는 AppDomain.BaseDirectory가 빈 문자열이므로 ModEntry에서
        // HookHealth.ResolveGameDirectory로 연결한다. 연결 전에 읽혀도 절대 경로가 되도록 현재 디렉터리로 폴백한다
        // (게임은 게임 폴더를 작업 디렉터리로 시작한다). xUnit 테스트는 LoadFromFolder로 폴더를 직접 넘긴다.
        internal static Func<string> ResolveBaseDirectory = () =>
            string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.BaseDirectory)
                ? Environment.CurrentDirectory
                : AppDomain.CurrentDomain.BaseDirectory;

        // 파일 생성·누락 항목 추가·기본값 조회가 모두 이 단일 정의를 사용한다.
        private static readonly ConfigEntry[] Entries =
        {
            new ConfigEntry("autoplay", "false",
                "오토플레이 강제 사용 여부 (true = 자동 연주 및 공식 기록 저장 차단, false = 직접 플레이)"),

            new ConfigEntry("AllPerfect", "false",
                "올 퍼펙트 판정 모드 (true = 키 입력 시 타이밍 오차 없이 무조건 BLUE 최고 판정 및 결과 100% 반영, false = 기본 판정)"),
            new ConfigEntry("AllPerfectCustomOnly", "true",
                "올 퍼펙트 판정을 커스텀 곡에서만 적용할지 여부 (true = 커스텀 곡 전용, false = 공식 곡에도 적용 및 공식 기록 저장 차단)"),

            new ConfigEntry("UnlockAll", "true",
                "전곡 해금 및 볼트 제한 해제 (true = 볼트/DLC/어드벤처 잠금 없이 모든 공식 수록곡 즉시 플레이 가능, false = 기본 잠금 유지)"),

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

            new ConfigEntry("VerboseInvocationLogging", "false",
                "[디버그] Harmony 훅 호출 로그 출력 여부 (true = 켜짐, false = 꺼짐)"),
            new ConfigEntry("FocusedHarmonyInvocationLogging", "true",
                "[디버그] VerboseInvocationLogging 활성 시 주요 훅만 로그 출력 (true = 주요 훅만, false = 전체)"),
            new ConfigEntry("RuntimeProbeLogging", "false",
                "[디버그] 런타임 프로브 상세 로그 출력 여부"),
            new ConfigEntry("TrackSelectorVerboseLogging", "false",
                "[디버그] TrackSelector 주입 상세 로그 출력 여부"),

            new ConfigEntry("BgmCacheMaxEntries", "24",
                "메모리에 유지할 커스텀 BGM AudioClip 최대 개수 (0 = 무제한, 초과 시 가장 오래 안 쓴 클립부터 해제)"),
        };

        private static readonly Dictionary<string, string> DefaultsByKey = BuildDefaultsMap();

        private static Dictionary<string, string> BuildDefaultsMap()
        {
            var map = new Dictionary<string, string>(Entries.Length, StringComparer.OrdinalIgnoreCase);
            foreach (ConfigEntry entry in Entries)
            {
                map[entry.Key] = entry.DefaultValue;
            }
            return map;
        }

        // 파일을 한 번만 읽어 만든 값 묶음. 설정은 게임 재시작 시에만 바뀌므로(HeaderText 안내) 다시 읽지 않는다.
        // 노트 연출/판정 훅이 프레임·노트마다 읽으므로 접근은 null 검사 1회로 끝나야 한다.
        private static Settings? _current;

        private static Settings Current => _current ??= Load(FolderPath, FilePath);

        public static bool AutoPlay => Current.AutoPlay;
        public static bool AllPerfect => Current.AllPerfect;
        public static bool AllPerfectCustomOnly => Current.AllPerfectCustomOnly;
        public static bool UnlockAll => Current.UnlockAll;
        public static bool NoteSway => Current.NoteSway;
        public static float NoteSwayAmplitude => Current.NoteSwayAmplitude;
        public static float NoteSwaySpeed => Current.NoteSwaySpeed;
        public static bool NoteSwayDamping => Current.NoteSwayDamping;
        public static float NoteSwayDampingTime => Current.NoteSwayDampingTime;
        public static bool NoteSpeedChaos => Current.NoteSpeedChaos;
        public static float NoteSpeedChaosMin => Current.NoteSpeedChaosMin;
        public static float NoteSpeedChaosMax => Current.NoteSpeedChaosMax;
        public static bool NoteSpeedChaosPerLane => Current.NoteSpeedChaosPerLane;
        public static bool VerboseInvocationLogging => Current.VerboseInvocationLogging;
        public static bool FocusedHarmonyInvocationLogging => Current.FocusedHarmonyInvocationLogging;
        public static bool RuntimeProbeLogging => Current.RuntimeProbeLogging;
        public static bool TrackSelectorVerboseLogging => Current.TrackSelectorVerboseLogging;
        public static int BgmCacheMaxEntries => Current.BgmCacheMaxEntries;

        public static string FolderPath => Path.Combine(ResolveBaseDirectory(), FolderName);

        public static string FilePath => Path.Combine(FolderPath, FileName);

        internal static Settings LoadFromFolder(string folderPath)
        {
            return Load(folderPath, Path.Combine(folderPath, FileName));
        }

        private static Settings Load(string folderPath, string filePath)
        {
            // Entries 기본값으로 초기화된 Settings를 만든 뒤 파일 값으로 덮어쓴다.
            var settings = CreateDefaultSettings();

            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    LogInfo($"[Config] 설정 폴더를 만들었습니다: {folderPath}");
                }

                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, BuildDefaultContents());
                    LogInfo($"[Config] 기본 설정 파일을 만들었습니다: {filePath}");
                }

                Dictionary<string, string> values = ParseFile(filePath);
                WarnUnknownKeys(values.Keys);
                AppendMissingEntries(filePath, values);

                settings.AutoPlay = ReadBool(values, "autoplay", settings.AutoPlay);
                settings.AllPerfect = ReadBool(values, "AllPerfect", settings.AllPerfect);
                settings.AllPerfectCustomOnly = ReadBool(values, "AllPerfectCustomOnly", settings.AllPerfectCustomOnly);
                settings.UnlockAll = ReadBool(values, "UnlockAll", settings.UnlockAll);

                settings.NoteSway = ReadBool(values, "NoteSway", settings.NoteSway);
                settings.NoteSwayAmplitude = Math.Max(0f, ReadFloat(values, "NoteSwayAmplitude", settings.NoteSwayAmplitude));
                settings.NoteSwaySpeed = Math.Max(0f, ReadFloat(values, "NoteSwaySpeed", settings.NoteSwaySpeed));
                settings.NoteSwayDamping = ReadBool(values, "NoteSwayDamping", settings.NoteSwayDamping);
                settings.NoteSwayDampingTime = Math.Max(0f, ReadFloat(values, "NoteSwayDampingTime", settings.NoteSwayDampingTime));

                settings.NoteSpeedChaos = ReadBool(values, "NoteSpeedChaos", settings.NoteSpeedChaos);
                settings.NoteSpeedChaosMin = ReadFloat(values, "NoteSpeedChaosMin", settings.NoteSpeedChaosMin);
                settings.NoteSpeedChaosMax = ReadFloat(values, "NoteSpeedChaosMax", settings.NoteSpeedChaosMax);
                settings.NoteSpeedChaosPerLane = ReadBool(values, "NoteSpeedChaosPerLane", settings.NoteSpeedChaosPerLane);
                NormalizeSpeedChaosRange(settings);

                settings.VerboseInvocationLogging = ReadBool(values, "VerboseInvocationLogging", settings.VerboseInvocationLogging);
                settings.FocusedHarmonyInvocationLogging = ReadBool(values, "FocusedHarmonyInvocationLogging", settings.FocusedHarmonyInvocationLogging);
                settings.RuntimeProbeLogging = ReadBool(values, "RuntimeProbeLogging", settings.RuntimeProbeLogging);
                settings.TrackSelectorVerboseLogging = ReadBool(values, "TrackSelectorVerboseLogging", settings.TrackSelectorVerboseLogging);
                settings.BgmCacheMaxEntries = Math.Max(0, ReadInt(values, "BgmCacheMaxEntries", settings.BgmCacheMaxEntries));

                LogInfo($"[Config] autoplay={settings.AutoPlay} AllPerfect={settings.AllPerfect}(customOnly={settings.AllPerfectCustomOnly}) UnlockAll={settings.UnlockAll}");
                LogInfo($"[Config] NoteSway={settings.NoteSway} amplitude={settings.NoteSwayAmplitude} speed={settings.NoteSwaySpeed} damping={settings.NoteSwayDamping}/{settings.NoteSwayDampingTime}s");
                LogInfo($"[Config] NoteSpeedChaos={settings.NoteSpeedChaos} range={settings.NoteSpeedChaosMin}~{settings.NoteSpeedChaosMax} perLane={settings.NoteSpeedChaosPerLane}");
            }
            catch (Exception ex)
            {
                LogWarning($"[Config] 설정을 읽지 못해 기본값을 사용합니다: {ex.GetType().Name}: {ex.Message}");
            }

            return settings;
        }

        private static Settings CreateDefaultSettings()
        {
            return new Settings
            {
                AutoPlay = GetDefaultBool("autoplay"),
                AllPerfect = GetDefaultBool("AllPerfect"),
                AllPerfectCustomOnly = GetDefaultBool("AllPerfectCustomOnly"),
                UnlockAll = GetDefaultBool("UnlockAll"),
                NoteSway = GetDefaultBool("NoteSway"),
                NoteSwayAmplitude = GetDefaultFloat("NoteSwayAmplitude"),
                NoteSwaySpeed = GetDefaultFloat("NoteSwaySpeed"),
                NoteSwayDamping = GetDefaultBool("NoteSwayDamping"),
                NoteSwayDampingTime = GetDefaultFloat("NoteSwayDampingTime"),
                NoteSpeedChaos = GetDefaultBool("NoteSpeedChaos"),
                NoteSpeedChaosMin = GetDefaultFloat("NoteSpeedChaosMin"),
                NoteSpeedChaosMax = GetDefaultFloat("NoteSpeedChaosMax"),
                NoteSpeedChaosPerLane = GetDefaultBool("NoteSpeedChaosPerLane"),
                VerboseInvocationLogging = GetDefaultBool("VerboseInvocationLogging"),
                FocusedHarmonyInvocationLogging = GetDefaultBool("FocusedHarmonyInvocationLogging"),
                RuntimeProbeLogging = GetDefaultBool("RuntimeProbeLogging"),
                TrackSelectorVerboseLogging = GetDefaultBool("TrackSelectorVerboseLogging"),
                BgmCacheMaxEntries = GetDefaultInt("BgmCacheMaxEntries"),
            };
        }

        private static int GetDefaultInt(string key)
        {
            return DefaultsByKey.TryGetValue(key, out string? raw) ? ParseInt(raw, key, 0) : 0;
        }

        private static bool GetDefaultBool(string key)
        {
            return DefaultsByKey.TryGetValue(key, out string? raw) && ParseBool(raw, key, false);
        }

        private static float GetDefaultFloat(string key)
        {
            return DefaultsByKey.TryGetValue(key, out string? raw) ? ParseFloat(raw, key, 0f) : 0f;
        }

        // 배율이 0 이하면 노트가 멈추거나 뒤집혀 날아간다. 뒤집힌 범위도 여기서 바로잡는다.
        private static void NormalizeSpeedChaosRange(Settings settings)
        {
            const float MinAllowed = 0.05f;

            if (settings.NoteSpeedChaosMin < MinAllowed || settings.NoteSpeedChaosMax < MinAllowed)
            {
                LogWarning($"[Config] NoteSpeedChaos 배율은 {MinAllowed} 이상이어야 합니다. 값을 올려서 씁니다.");
                settings.NoteSpeedChaosMin = Math.Max(MinAllowed, settings.NoteSpeedChaosMin);
                settings.NoteSpeedChaosMax = Math.Max(MinAllowed, settings.NoteSpeedChaosMax);
            }

            if (settings.NoteSpeedChaosMin > settings.NoteSpeedChaosMax)
            {
                LogWarning("[Config] NoteSpeedChaosMin이 Max보다 큽니다. 두 값을 바꿔서 씁니다.");
                (settings.NoteSpeedChaosMin, settings.NoteSpeedChaosMax) = (settings.NoteSpeedChaosMax, settings.NoteSpeedChaosMin);
            }
        }

        private static string BuildDefaultContents()
        {
            var builder = new StringBuilder();
            builder.Append(HeaderText);
            AppendEntries(builder, Entries);
            return builder.ToString();
        }

        // 항목 사이에 빈 줄을 넣되, 앞 항목과 설명을 공유하는 항목(Min/Max 짝)은 붙여 쓴다.
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

        // 오타 난 키(예: "AllPerfet")는 조용히 무시되고 AppendMissingEntries가 기본값 줄을 따로 덧붙여,
        // 사용자는 켰다고 생각하는데 기본값으로 도는 상태가 된다. 모르는 키를 가까운 키 제안과 함께 알린다.
        // 예전 버전이 만들던 설정. 사용자 파일에 남아 있어도 오타가 아니므로 경고 대신 안내만 한다.
        private static readonly HashSet<string> RetiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EnableNoiseSuppression", // BmsChart의 미사용 노이즈 억제 옵션(2026-11 제거)
        };

        private static void WarnUnknownKeys(IEnumerable<string> keys)
        {
            foreach (string key in keys)
            {
                if (RetiredKeys.Contains(key))
                {
                    LogInfo($"[Config] '{key}' 은(는) 더 이상 쓰지 않는 설정입니다(무시됨, 지워도 됩니다).");
                }
            }

            foreach ((string key, string? suggestion) in FindUnknownKeys(keys))
            {
                LogWarning(suggestion is null
                    ? $"[Config] 알 수 없는 설정 '{key}' 은(는) 무시합니다."
                    : $"[Config] 알 수 없는 설정 '{key}' 은(는) 무시합니다 — '{suggestion}' 의 오타인지 확인하세요.");
            }
        }

        // 순수 로직(xUnit 대상). 키 비교는 대소문자를 무시하고(ParseFile과 동일), 제안은 밑줄/공백/하이픈을 뺀
        // 소문자 이름의 편집 거리 2 이하인 알려진 키 중 가장 가까운 것.
        internal static List<(string Key, string? Suggestion)> FindUnknownKeys(IEnumerable<string> keys)
        {
            var result = new List<(string, string?)>();
            foreach (string key in keys)
            {
                if (DefaultsByKey.ContainsKey(key) || RetiredKeys.Contains(key))
                {
                    continue;
                }

                string normalized = NormalizeKey(key);
                string? best = null;
                int bestDistance = int.MaxValue;
                foreach (ConfigEntry entry in Entries)
                {
                    int distance = EditDistance(normalized, NormalizeKey(entry.Key), 2);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = entry.Key;
                    }
                }

                result.Add((key, bestDistance <= 2 ? best : null));
            }

            return result;
        }

        private static string NormalizeKey(string key)
        {
            var builder = new StringBuilder(key.Length);
            foreach (char c in key)
            {
                if (c != '_' && c != '-' && !char.IsWhiteSpace(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        // 레벤슈타인 거리. limit를 넘으면 limit + 1을 돌려준다(키가 짧아 단순 구현으로 충분).
        private static int EditDistance(string a, string b, int limit)
        {
            if (Math.Abs(a.Length - b.Length) > limit)
            {
                return limit + 1;
            }

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                previous[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                int rowMin = current[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                    rowMin = Math.Min(rowMin, current[j]);
                }

                if (rowMin > limit)
                {
                    return limit + 1;
                }

                (previous, current) = (current, previous);
            }

            return Math.Min(previous[b.Length], limit + 1);
        }

        // 모드를 업데이트해서 설정이 늘어나도 사용자가 파일을 지웠다 다시 만들지 않아도 되게 한다.
        private static void AppendMissingEntries(string filePath, Dictionary<string, string> values)
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
                if (!EndsWithNewLine(filePath))
                {
                    File.AppendAllText(filePath, "\n");
                }

                File.AppendAllText(filePath, builder.ToString());
                LogInfo($"[Config] 새로 생긴 설정 {missing.Count}개를 config.txt에 추가했습니다.");
            }
            catch (Exception ex)
            {
                // 파일에 못 써도 기본값으로 계속 돌아가면 되므로 치명적이지 않다.
                LogWarning($"[Config] 새 설정을 파일에 추가하지 못했습니다: {ex.GetType().Name}: {ex.Message}");
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

        private static readonly char[] ConfigSeparators = { '=', ':' };
        private static Dictionary<string, string> ParseFile(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in SafeTextReader.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int sepIndex = line.IndexOfAny(ConfigSeparators);
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

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out string? text) ? ParseInt(text, key, fallback) : fallback;
        }

        private static int ParseInt(string text, string key, int fallback)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            LogWarning($"[Config] {key} 값을 정수로 읽지 못해 기본값({fallback})을 씁니다: {text}");
            return fallback;
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
                case "켜짐":
                case "예":
                case "是":
                case "开":
                case "开启":
                case "启用":
                case "对":
                    return true;
                case "false":
                case "off":
                case "no":
                case "0":
                case "끄기":
                case "꺼짐":
                case "아니오":
                case "사용안함":
                case "否":
                case "关":
                case "关闭":
                case "禁用":
                case "错":
                    return false;
                default:
                    LogWarning($"[Config] {key} 값을 해석하지 못해 기본값({fallback})을 씁니다: {text}");
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

            LogWarning($"[Config] {key} 값을 숫자로 읽지 못해 기본값({fallback})을 씁니다: {text}");
            return fallback;
        }

        internal sealed class Settings
        {
            public bool AutoPlay;
            public bool AllPerfect;
            public bool AllPerfectCustomOnly;
            public bool UnlockAll;
            public bool NoteSway;
            public float NoteSwayAmplitude;
            public float NoteSwaySpeed;
            public bool NoteSwayDamping;
            public float NoteSwayDampingTime;
            public bool NoteSpeedChaos;
            public float NoteSpeedChaosMin;
            public float NoteSpeedChaosMax;
            public bool NoteSpeedChaosPerLane;
            public bool VerboseInvocationLogging;
            public bool FocusedHarmonyInvocationLogging;
            public bool RuntimeProbeLogging;
            public bool TrackSelectorVerboseLogging;
            public int BgmCacheMaxEntries;
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
