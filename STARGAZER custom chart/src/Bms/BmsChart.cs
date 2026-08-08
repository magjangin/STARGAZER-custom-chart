using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STARGAZER_custom_chart
{
    // 순수 BMS 텍스트 파서. 게임/Harmony/Unity 의존성이 전혀 없어 독립적으로 검증 가능하다.
    // 뼈대 단계 범위: 고정 BPM(#BPM 헤더 하나), 고정 4/4 마디 길이(채널 02 미지원),
    // 롱노트 미지원(전부 숏노트로 취급) — hwa2.bms 테스트 파일로 검증한 범위와 동일하다.
    internal sealed class BmsNoteEvent
    {
        public BmsNoteEvent(int channel, int beatNumerator, int beatDenominator)
        {
            Channel = channel;
            BeatNumerator = beatNumerator;
            BeatDenominator = beatDenominator;
        }

        public int Channel { get; }
        public int BeatNumerator { get; }
        public int BeatDenominator { get; }
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

        public static BmsChart? TryParse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string[] rawLines = File.ReadAllLines(filePath);

            var wavIdWidths = new List<int>();
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
                    wavIdWidths.Add(idPart.Trim().Length);
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
            bool extended = wavIdWidths.Any(width => width == 3);
            int chunkWidth = extended ? 3 : 2;

            var chart = new BmsChart { Bpm = bpm };
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
                    string normalized = extended && chunk.Length == 3 && chunk[0] == '0' ? chunk.Substring(1) : chunk;
                    if (normalized == "00" || normalized == "0")
                    {
                        continue;
                    }

                    int numerator = slot * MeasureBeats;
                    int denominator = slotCount;
                    int gcd = Gcd(numerator, denominator);
                    numerator /= gcd;
                    denominator /= gcd;

                    measure.Notes.Add(new BmsNoteEvent(channel, numerator, denominator));
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
