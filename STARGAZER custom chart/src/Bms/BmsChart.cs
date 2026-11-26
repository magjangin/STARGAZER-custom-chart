using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace STARGAZER_custom_chart
{
    // BMS 텍스트 파서. 게임/Harmony/Unity 객체는 건드리지 않는다.
    // 범위: 고정 BPM(#BPM 헤더 하나), 마디 길이 변경(채널 02, 기본 4/4).
    // 롱노트는 #WAV 파일명으로 구분한다(아래 BmsNoteKind 참고).
    internal enum BmsNoteKind
    {
        Normal,
        HoldStart,
        HoldEnd,
    }

    internal sealed class BmsNoteEvent
    {
        public BmsNoteEvent(int channel, int beatNumerator, int beatDenominator, string soundId, BmsNoteKind kind)
        {
            Channel = channel;
            BeatNumerator = beatNumerator;
            BeatDenominator = beatDenominator;
            SoundId = soundId;
            Kind = kind;
        }

        public int Channel { get; }
        public int BeatNumerator { get; }
        public int BeatDenominator { get; }
        public string SoundId { get; }
        public BmsNoteKind Kind { get; }
    }

    internal sealed class BmsMeasure
    {
        public BmsMeasure(int index, double lengthRatio = 1.0)
        {
            Index = index;
            LengthRatio = lengthRatio;
        }

        public int Index { get; }
        public double LengthRatio { get; internal set; }
        public List<BmsNoteEvent> Notes { get; } = new List<BmsNoteEvent>();
    }

    internal sealed class BmsChart
    {
        // 기본 마디 길이(4/4 = 4비트). 채널 02(#mmm02)가 있는 마디는 LengthRatio 배율로 스케일한다.
        private const int MeasureBeats = 4;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, (DateTime LastWriteUtc, BmsChart Chart)> ChartCache =
            new Dictionary<string, (DateTime, BmsChart)>(StringComparer.OrdinalIgnoreCase);

        public double Bpm { get; private set; } = 120;
        public List<BmsMeasure> Measures { get; } = new List<BmsMeasure>();
        public List<string> Warnings { get; } = new List<string>();

        // 사운드 ID -> 노트 종류. 로그로 매핑 결과를 확인할 수 있게 공개해 둔다.
        public IReadOnlyDictionary<string, BmsNoteKind> SoundKinds { get; private set; }
            = new Dictionary<string, BmsNoteKind>();

        public static BmsChart? TryGetOrParse(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                lock (CacheLock)
                {
                    ChartCache.Remove(fullPath);
                }
                return null;
            }

            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);
            lock (CacheLock)
            {
                if (ChartCache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == lastWriteUtc)
                {
                    return cached.Chart;
                }
            }

            BmsChart? parsed = TryParse(fullPath);
            if (parsed is not null)
            {
                lock (CacheLock)
                {
                    ChartCache[fullPath] = (lastWriteUtc, parsed);
                }
            }

            return parsed;
        }

        // 확장(3자리) 포맷이면 앞자리 0을 떼어 기존 2자리 체계로 맞춘다.
        private static string NormalizeSoundId(ReadOnlySpan<char> id, bool extended)
        {
            if (extended && id.Length == 3 && id[0] == '0')
            {
                id = id.Slice(1);
            }

            return id.ToString();
        }

        private static bool IsZeroSoundChunk(ReadOnlySpan<char> chunk)
        {
            for (int i = 0; i < chunk.Length; i++)
            {
                if (chunk[i] != '0')
                {
                    return false;
                }
            }

            return true;
        }

        // 롱노트 마커는 #WAV 파일명으로 판단한다. 한국어/영어/중국어 키워드를 모두 지원:
        //   #WAV002 hold 시작.wav / #WAV003 hold 끝.wav / #WAV004 hold 开始.wav / #WAV005 hold 结束.wav
        private static BmsNoteKind ClassifyNoteKind(string fileName)
        {
            if (fileName.IndexOf("hold", StringComparison.OrdinalIgnoreCase) < 0
                && fileName.IndexOf("long", StringComparison.OrdinalIgnoreCase) < 0
                && fileName.IndexOf("롱", StringComparison.Ordinal) < 0
                && fileName.IndexOf("长", StringComparison.Ordinal) < 0
                && fileName.IndexOf("長", StringComparison.Ordinal) < 0)
            {
                return BmsNoteKind.Normal;
            }

            if (fileName.IndexOf("시작", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0
                || fileName.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0
                || fileName.IndexOf("开始", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("開始", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("起始", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("起点", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("头", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("頭", StringComparison.Ordinal) >= 0)
            {
                return BmsNoteKind.HoldStart;
            }

            if (fileName.IndexOf("끝", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0
                || fileName.IndexOf("tail", StringComparison.OrdinalIgnoreCase) >= 0
                || fileName.IndexOf("结束", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("終了", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("终止", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("终点", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("尾", StringComparison.Ordinal) >= 0)
            {
                return BmsNoteKind.HoldEnd;
            }

            return BmsNoteKind.Normal;
        }

        // BMS 헤더의 #TITLE / #ARTIST만 가볍게 읽는다. info.txt가 없는 앨범이 곡 제목·아티스트를 BMS 파일에서
        // 폴백으로 얻는 데 쓴다. 노트 데이터는 파싱하지 않으므로 앨범 스캔 시점에 불러도 부담이 없다.
        // "#TITLE"만 인정하고 "#SUBTITLE" 같은 접두 변형은 무시한다(대소문자 무시, 키 뒤에 공백/탭 필요).
        // 지연 열거(EnumerateLines)를 사용하고 마디 데이터(#mmmcc:)가 시작되면 즉시 탈출해 대용량 차트 스캔 비용을 최소화한다.
        public static (string? Title, string? Artist) ReadHeaderMetadata(string filePath)
        {
            string? title = null;
            string? artist = null;

            foreach (string rawLine in SafeTextReader.EnumerateLines(filePath))
            {
                ReadOnlySpan<char> line = rawLine.AsSpan().Trim();
                if (line.Length < 7 || line[0] != '#')
                {
                    continue;
                }

                // 마디 데이터(#mmmcc:)가 시작되면 헤더 영역이 끝난 것이므로 조기 탈출
                if (line.Length >= 7 && line[6] == ':' && char.IsDigit(line[1]) && char.IsDigit(line[2]) && char.IsDigit(line[3]))
                {
                    break;
                }

                if (title is null && TryReadHeaderValue(line, "#TITLE", out string? titleValue))
                {
                    title = titleValue;
                }
                else if (artist is null && TryReadHeaderValue(line, "#ARTIST", out string? artistValue))
                {
                    artist = artistValue;
                }

                if (title is not null && artist is not null)
                {
                    break;
                }
            }

            return (title, artist);
        }

        private static bool TryReadHeaderValue(ReadOnlySpan<char> line, string key, out string? value)
        {
            value = null;
            if (line.Length <= key.Length
                || !line.Slice(0, key.Length).Equals(key.AsSpan(), StringComparison.OrdinalIgnoreCase)
                || !char.IsWhiteSpace(line[key.Length]))
            {
                return false;
            }

            string text = line.Slice(key.Length).Trim().ToString();
            if (text.Length == 0)
            {
                return false;
            }

            value = text;
            return true;
        }

        public static BmsChart? TryParse(string filePath)
        {
            // 상한을 넘는 파일은 읽지 않는다(엉뚱한 대용량 파일을 .bms로 두었을 때의 메모리/시간 낭비 방지).
            if (!File.Exists(filePath) || SafeTextReader.IsTooLarge(filePath))
            {
                return null;
            }

            string[] rawLines = SafeTextReader.ReadAllLines(filePath);
            var dataLineIndices = new List<int>(rawLines.Length);
            var wavDefinitions = new List<(string RawId, string FileName)>();
            var warnings = new List<string>();
            double bpm = 120;
            bool warnedNoSpaceBpm = false;

            for (int lineIdx = 0; lineIdx < rawLines.Length; lineIdx++)
            {
                ReadOnlySpan<char> line = rawLines[lineIdx].AsSpan().Trim();
                if (line.Length == 0 || line[0] != '#')
                {
                    continue;
                }

                if (line.Length > 4 && line.Slice(0, 4).Equals("#WAV".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> rest = line.Slice(4);
                    int spaceIndex = rest.IndexOfAny(' ', '\t');
                    string idPart = (spaceIndex > 0 ? rest.Slice(0, spaceIndex) : rest).Trim().ToString();
                    string fileName = spaceIndex >= 0 ? rest.Slice(spaceIndex + 1).Trim().ToString() : string.Empty;
                    wavDefinitions.Add((idPart, fileName));
                    continue;
                }

                // 곡 BPM 헤더는 "#BPM 140" 형태만이다. "#BPM01 180"처럼 번호가 붙은 줄은 채널 08(BPM 변경)이
                // 참조하는 정의라, 같이 읽으면 마지막 정의 값이 곡 전체 BPM을 덮어쓴다.
                if (line.Length > 4 && line.Slice(0, 4).Equals("#BPM".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (char.IsWhiteSpace(line[4]))
                    {
                        ReadOnlySpan<char> valueSpan = line.Slice(4).Trim();
                        if (double.TryParse(valueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedBpm) && parsedBpm > 0)
                        {
                            bpm = parsedBpm;
                        }
                        continue;
                    }

                    ReadOnlySpan<char> afterBpm = line.Slice(4);
                    if (afterBpm.IndexOfAny(' ', '\t') < 0
                        && double.TryParse(afterBpm, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                        && !warnedNoSpaceBpm)
                    {
                        warnedNoSpaceBpm = true;
                        warnings.Add("#BPM 헤더에 공백이 없어 무시되었습니다('#BPM 140' 형식만 지원).");
                    }
                    continue;
                }

                // "#mmmcc:data" 최소 길이: # + 3자리 마디 + 2자리 채널 + ':' = 7글자
                if (line.Length >= 7 && line[6] == ':')
                {
                    dataLineIndices.Add(lineIdx);
                }
            }

            // 사용자 규칙: #WAV 헤더의 ID 부분이 3글자면 확장(3자리) 사운드ID 포맷으로 판단한다.
            bool extended = wavDefinitions.Any(def => def.RawId.Length == 3);
            int chunkWidth = extended ? 3 : 2;

            // 사운드 ID -> 노트 종류. 롱노트 여부는 ID 번호가 아니라 #WAV 파일명으로 판단한다
            // (예: "hold 시작.wav" / "hold 끝.wav"). 번호를 바꿔도 의미가 유지되도록.
            var kindBySoundId = new Dictionary<string, BmsNoteKind>(StringComparer.OrdinalIgnoreCase);
            foreach ((string rawId, string fileName) in wavDefinitions)
            {
                kindBySoundId[NormalizeSoundId(rawId.AsSpan(), extended)] = ClassifyNoteKind(fileName);
            }

            var chart = new BmsChart { Bpm = bpm, SoundKinds = kindBySoundId };
            chart.Warnings.AddRange(warnings);
            var measuresByIndex = new Dictionary<int, BmsMeasure>();
            var measureLengthRatios = new Dictionary<int, double>();
            var noteDataLines = new List<(int LineIdx, int MeasureIndex, int Channel)>(dataLineIndices.Count);
            bool hasBpmChange = false;
            bool hasStop = false;

            // 1차 스캔: 마디/채널 번호를 한 번만 파싱해 채널 02(마디 길이 배율)와 미지원 채널을 먼저 분류하고,
            // 노트 데이터 줄만 추려 둔다(중복 Trim/int.TryParse 제거).
            foreach (int lineIdx in dataLineIndices)
            {
                ReadOnlySpan<char> line = rawLines[lineIdx].AsSpan().Trim();
                ReadOnlySpan<char> measureSpan = line.Slice(1, 3);
                ReadOnlySpan<char> channelSpan = line.Slice(4, 2);

                if (!int.TryParse(measureSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int measureIndex)
                    || measureIndex < 0
                    || !int.TryParse(channelSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int channel))
                {
                    continue;
                }

                if (channel == 2)
                {
                    ReadOnlySpan<char> dataSpan = line.Slice(7);
                    if (double.TryParse(dataSpan.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio > 0)
                    {
                        measureLengthRatios[measureIndex] = ratio;
                    }
                    continue;
                }

                if (channel == 1)
                {
                    continue;
                }

                if (channel is 3 or 8)
                {
                    hasBpmChange = true;
                    continue;
                }

                if (channel == 9)
                {
                    hasStop = true;
                    continue;
                }

                noteDataLines.Add((lineIdx, measureIndex, channel));
            }

            // 마디 길이 분수는 같은 마디의 여러 채널 줄에서 공유하므로 마디당 1회만 계산한다.
            var measureRatioFractions = new Dictionary<int, (int Num, int Denom)>(measureLengthRatios.Count);
            foreach (KeyValuePair<int, double> pair in measureLengthRatios)
            {
                measureRatioFractions[pair.Key] = ToFraction(pair.Value);
            }

            foreach ((int lineIdx, int measureIndex, int channel) in noteDataLines)
            {
                ReadOnlySpan<char> line = rawLines[lineIdx].AsSpan().Trim();
                ReadOnlySpan<char> dataSpan = line.Slice(7);
                if (dataSpan.IsEmpty || dataSpan.Length % chunkWidth != 0)
                {
                    continue;
                }

                double lengthRatio = measureLengthRatios.TryGetValue(measureIndex, out double r) ? r : 1.0;
                if (!measuresByIndex.TryGetValue(measureIndex, out BmsMeasure? targetMeasure))
                {
                    targetMeasure = new BmsMeasure(measureIndex, lengthRatio);
                    measuresByIndex[measureIndex] = targetMeasure;
                }

                (int ratioNum, int ratioDenom) = measureRatioFractions.TryGetValue(measureIndex, out var frac) ? frac : (1, 1);
                int slotCount = dataSpan.Length / chunkWidth;

                for (int slot = 0; slot < slotCount; slot++)
                {
                    ReadOnlySpan<char> chunk = dataSpan.Slice(slot * chunkWidth, chunkWidth);
                    if (IsZeroSoundChunk(chunk))
                    {
                        continue;
                    }

                    string normalized = NormalizeSoundId(chunk, extended);

                    long numerator = (long)slot * MeasureBeats * ratioNum;
                    long denominator = (long)slotCount * ratioDenom;
                    long gcd = Gcd(numerator, denominator);
                    numerator /= gcd;
                    denominator /= gcd;

                    BmsNoteKind kind = kindBySoundId.TryGetValue(normalized, out BmsNoteKind found)
                        ? found
                        : BmsNoteKind.Normal;

                    targetMeasure.Notes.Add(new BmsNoteEvent(channel, (int)numerator, (int)denominator, normalized, kind));
                }
            }

            if (hasBpmChange)
            {
                chart.Warnings.Add("곡 중 BPM 변경(채널 03/08)은 지원하지 않아 초기 #BPM으로 고정 처리됩니다.");
            }

            if (hasStop)
            {
                chart.Warnings.Add("STOP 기믹(채널 09)은 지원하지 않아 무시됩니다.");
            }

            // 노트가 하나도 없는 빈/주석 전용 파일은 Measures를 비워 둬서 주입부가 0노트 차트로 덮어쓰지 않게 한다.
            if (measuresByIndex.Count == 0)
            {
                return chart;
            }

            // 중간에 비어있는 마디(노트가 없는 마디)가 누락되면 타임라인 영역(Area) 순서가 당겨져
            // 노트가 곡보다 일찍 끝나고 박자가 어긋나게 된다. 0부터 maxIndex까지 모든 마디를 순서대로 채운다.
            int maxMeasureIndex = Math.Max(
                measuresByIndex.Keys.Max(),
                measureLengthRatios.Count > 0 ? measureLengthRatios.Keys.Max() : 0);
            for (int i = 0; i <= maxMeasureIndex; i++)
            {
                if (measuresByIndex.TryGetValue(i, out BmsMeasure? measure))
                {
                    chart.Measures.Add(measure);
                }
                else
                {
                    double ratio = measureLengthRatios.TryGetValue(i, out double r) ? r : 1.0;
                    chart.Measures.Add(new BmsMeasure(i, ratio));
                }
            }

            return chart;
        }

        internal static (int Numerator, int Denominator) ToFraction(double value, int maxDenominator = 192)
        {
            if (Math.Abs(value - 1.0) < 1e-6)
            {
                return (1, 1);
            }

            int bestNum = Math.Max(1, (int)Math.Round(value));
            int bestDenom = 1;
            double bestError = Math.Abs(value - bestNum);

            for (int denom = 1; denom <= maxDenominator; denom++)
            {
                int num = (int)Math.Round(value * denom);
                if (num <= 0)
                {
                    continue;
                }

                double error = Math.Abs(value - (double)num / denom);
                if (error < bestError - 1e-9)
                {
                    bestError = error;
                    bestNum = num;
                    bestDenom = denom;
                    if (error < 1e-6)
                    {
                        break;
                    }
                }
            }

            int gcd = (int)Gcd(bestNum, bestDenom);
            return (bestNum / gcd, bestDenom / gcd);
        }

        private static long Gcd(long a, long b)
        {
            if (a == 0)
            {
                return Math.Max(b, 1);
            }

            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            return Math.Max(a, 1);
        }
    }
}
