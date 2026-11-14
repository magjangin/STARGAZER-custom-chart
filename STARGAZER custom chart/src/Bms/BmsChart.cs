using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace STARGAZER_custom_chart
{
    // BMS 텍스트 파서. 게임/Harmony/Unity 객체는 건드리지 않는다.
    // 범위: 고정 BPM(#BPM 헤더 하나), 고정 4/4 마디 길이(채널 02 미지원).
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
        public BmsMeasure(int index)
        {
            Index = index;
        }

        public int Index { get; }
        public List<BmsNoteEvent> Notes { get; } = new List<BmsNoteEvent>();
    }

    internal sealed class BmsChart
    {
        // 마디 길이 변경(채널 02)을 아직 안 읽으므로 모든 마디를 4/4(4비트)로 고정한다.
        private const int MeasureBeats = 4;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, (DateTime LastWriteUtc, BmsChart Chart)> ChartCache =
            new Dictionary<string, (DateTime, BmsChart)>(StringComparer.OrdinalIgnoreCase);

        public double Bpm { get; private set; } = 120;
        public int SuppressedNoiseCount { get; private set; }
        public List<BmsMeasure> Measures { get; } = new List<BmsMeasure>();

        // 사운드 ID -> 노트 종류. 로그로 매핑 결과를 확인할 수 있게 공개해 둔다.
        public IReadOnlyDictionary<string, BmsNoteKind> SoundKinds { get; private set; }
            = new Dictionary<string, BmsNoteKind>();

        public static BmsChart? TryGetOrParse(string filePath, bool enableNoiseSuppression = false)
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

            BmsChart? parsed = TryParse(fullPath, enableNoiseSuppression);
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
                || fileName.IndexOf("终止", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("终点", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("尾", StringComparison.Ordinal) >= 0)
            {
                return BmsNoteKind.HoldEnd;
            }

            return BmsNoteKind.Normal;
        }

        public static BmsChart? TryParse(string filePath, bool enableNoiseSuppression = false)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string[] rawLines = SafeTextReader.ReadAllLines(filePath);
            var dataLineIndices = new List<int>(rawLines.Length);
            var wavDefinitions = new List<(string RawId, string FileName)>();
            double bpm = 120;

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
                if (line.Length > 4 && line.Slice(0, 4).Equals("#BPM".AsSpan(), StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(line[4]))
                {
                    ReadOnlySpan<char> valueSpan = line.Slice(4).Trim();
                    if (double.TryParse(valueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedBpm) && parsedBpm > 0)
                    {
                        bpm = parsedBpm;
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
            var measuresByIndex = new Dictionary<int, BmsMeasure>();

            foreach (int lineIdx in dataLineIndices)
            {
                ReadOnlySpan<char> line = rawLines[lineIdx].AsSpan().Trim();
                ReadOnlySpan<char> measureSpan = line.Slice(1, 3);
                ReadOnlySpan<char> channelSpan = line.Slice(4, 2);
                ReadOnlySpan<char> dataSpan = line.Slice(7);

                if (!int.TryParse(measureSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int measureIndex)
                    || measureIndex < 0
                    || !int.TryParse(channelSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int channel))
                {
                    continue;
                }

                // 노트로 취급하지 않는 채널: 01=BGM, 02=마디 길이, 03/08=BPM변경, 09=STOP.
                if (channel is 1 or 2 or 3 or 8 or 9)
                {
                    continue;
                }

                if (dataSpan.IsEmpty || dataSpan.Length % chunkWidth != 0)
                {
                    continue;
                }

                if (!measuresByIndex.TryGetValue(measureIndex, out BmsMeasure? targetMeasure))
                {
                    targetMeasure = new BmsMeasure(measureIndex);
                    measuresByIndex[measureIndex] = targetMeasure;
                }

                int slotCount = dataSpan.Length / chunkWidth;

                for (int slot = 0; slot < slotCount; slot++)
                {
                    ReadOnlySpan<char> chunk = dataSpan.Slice(slot * chunkWidth, chunkWidth);
                    if (IsZeroSoundChunk(chunk))
                    {
                        continue;
                    }

                    string normalized = NormalizeSoundId(chunk, extended);

                    int numerator = slot * MeasureBeats;
                    int denominator = slotCount;
                    int gcd = Gcd(numerator, denominator);
                    numerator /= gcd;
                    denominator /= gcd;

                    BmsNoteKind kind = kindBySoundId.TryGetValue(normalized, out BmsNoteKind found)
                        ? found
                        : BmsNoteKind.Normal;

                    targetMeasure.Notes.Add(new BmsNoteEvent(channel, numerator, denominator, normalized, kind));
                }
            }

            int suppressedNoiseCount = 0;
            if (enableNoiseSuppression)
            {
                foreach (BmsMeasure measure in measuresByIndex.Values)
                {
                    if (measure.Notes.Count > 1)
                    {
                        var uniqueNotes = new List<BmsNoteEvent>(measure.Notes.Count);
                        var seenPositions = new HashSet<(int Channel, int Num, int Denom)>();

                        foreach (BmsNoteEvent note in measure.Notes)
                        {
                            var key = (note.Channel, note.BeatNumerator, note.BeatDenominator);
                            if (seenPositions.Add(key))
                            {
                                uniqueNotes.Add(note);
                            }
                            else
                            {
                                suppressedNoiseCount++;
                            }
                        }

                        measure.Notes.Clear();
                        measure.Notes.AddRange(uniqueNotes);
                    }
                }
            }

            chart.SuppressedNoiseCount = suppressedNoiseCount;

            // 중간에 비어있는 마디(노트가 없는 마디)가 누락되면 타임라인 영역(Area) 순서가 당겨져
            // 노트가 곡보다 일찍 끝나고 박자가 어긋나게 된다. 0부터 maxIndex까지 모든 마디를 순서대로 채운다.
            int maxMeasureIndex = measuresByIndex.Count > 0 ? measuresByIndex.Keys.Max() : 0;
            for (int i = 0; i <= maxMeasureIndex; i++)
            {
                chart.Measures.Add(measuresByIndex.TryGetValue(i, out BmsMeasure? measure)
                    ? measure
                    : new BmsMeasure(i));
            }

            return chart;
        }

        private static int Gcd(int a, int b)
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
