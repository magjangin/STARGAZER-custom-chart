using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STARGAZER_custom_chart
{
    // 순수 BMS 텍스트 파서. 게임/Harmony/Unity 의존성이 전혀 없어 독립적으로 검증 가능하다.
    // 뼈대 단계 범위: 고정 BPM(#BPM 헤더 하나), 고정 4/4 마디 길이(채널 02 미지원).
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

        // 노트로 취급하지 않는 채널: 01=BGM, 02=마디 길이, 03/08=BPM변경, 09=STOP.
        private static readonly HashSet<string> IgnoredChannels = new HashSet<string> { "01", "02", "03", "08", "09" };

        public double Bpm { get; private set; } = 120;
        public List<BmsMeasure> Measures { get; } = new List<BmsMeasure>();

        // 사운드 ID -> 노트 종류. 로그로 매핑 결과를 확인할 수 있게 공개해 둔다.
        public IReadOnlyDictionary<string, BmsNoteKind> SoundKinds { get; private set; }
            = new Dictionary<string, BmsNoteKind>();

        // 확장(3자리) 포맷이면 앞자리 0을 떼어 기존 2자리 체계로 맞춘다.
        private static string NormalizeSoundId(string id, bool extended)
        {
            return extended && id.Length == 3 && id[0] == '0' ? id.Substring(1) : id;
        }

        // 롱노트 마커는 #WAV 파일명으로 판단한다. 사용자 규칙 예시:
        //   #WAV002 hold 시작.wav / #WAV003 hold 끝.wav
        private static BmsNoteKind ClassifyNoteKind(string fileName)
        {
            if (fileName.IndexOf("hold", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return BmsNoteKind.Normal;
            }

            if (fileName.IndexOf("시작", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return BmsNoteKind.HoldStart;
            }

            if (fileName.IndexOf("끝", StringComparison.Ordinal) >= 0
                || fileName.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return BmsNoteKind.HoldEnd;
            }

            return BmsNoteKind.Normal;
        }

        public static BmsChart? TryParse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string[] rawLines = File.ReadAllLines(filePath);

            var wavDefinitions = new List<(string RawId, string FileName)>();
            double bpm = 120;

            foreach (string rawLine in rawLines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] != '#')
                {
                    continue;
                }

                if (line.StartsWith("#WAV", StringComparison.OrdinalIgnoreCase) && line.Length > 4)
                {
                    int spaceIndex = line.IndexOf(' ');
                    string idPart = spaceIndex > 4 ? line.Substring(4, spaceIndex - 4) : line.Substring(4);
                    string fileName = spaceIndex > 4 ? line.Substring(spaceIndex + 1).Trim() : string.Empty;
                    wavDefinitions.Add((idPart.Trim(), fileName));
                    continue;
                }

                if (line.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring(5).Trim();
                    if (double.TryParse(value, out double parsedBpm))
                    {
                        bpm = parsedBpm;
                    }
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
                kindBySoundId[NormalizeSoundId(rawId, extended)] = ClassifyNoteKind(fileName);
            }

            var chart = new BmsChart { Bpm = bpm };
            chart.SoundKinds = kindBySoundId;
            var measuresByIndex = new Dictionary<int, BmsMeasure>();

            foreach (string rawLine in rawLines)
            {
                string line = rawLine.Trim();
                // "#mmmcc:data" 최소 길이: # + 3자리 마디 + 2자리 채널 + ':' = 7글자
                if (line.Length < 7 || line[0] != '#' || line[6] != ':')
                {
                    continue;
                }

                string measureText = line.Substring(1, 3);
                string channelText = line.Substring(4, 2);
                string data = line.Substring(7);

                if (!int.TryParse(measureText, out int measureIndex) || !int.TryParse(channelText, out int channel))
                {
                    continue;
                }

                if (IgnoredChannels.Contains(channelText))
                {
                    continue;
                }

                if (data.Length == 0 || data.Length % chunkWidth != 0)
                {
                    continue;
                }

                if (!measuresByIndex.TryGetValue(measureIndex, out BmsMeasure? measure))
                {
                    measure = new BmsMeasure(measureIndex);
                    measuresByIndex[measureIndex] = measure;
                }

                int slotCount = data.Length / chunkWidth;
                for (int slot = 0; slot < slotCount; slot++)
                {
                    string chunk = data.Substring(slot * chunkWidth, chunkWidth);
                    string normalized = NormalizeSoundId(chunk, extended);
                    if (normalized == "00" || normalized == "0")
                    {
                        continue;
                    }

                    int numerator = slot * MeasureBeats;
                    int denominator = slotCount;
                    int gcd = Gcd(numerator, denominator);
                    numerator /= gcd;
                    denominator /= gcd;

                    BmsNoteKind kind = kindBySoundId.TryGetValue(normalized, out BmsNoteKind found)
                        ? found
                        : BmsNoteKind.Normal;

                    measure.Notes.Add(new BmsNoteEvent(channel, numerator, denominator, normalized, kind));
                }
            }

            chart.Measures.AddRange(measuresByIndex.Values.OrderBy(m => m.Index));
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
