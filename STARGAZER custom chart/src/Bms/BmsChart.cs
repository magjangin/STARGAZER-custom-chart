using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace STARGAZER_custom_chart
{
    // 순수 BMS 텍스트 파서. 게임/Harmony/Unity 의존성이 전혀 없어 독립적으로 검증 가능하다.
    // 지원 범위: 고정 BPM(#BPM 헤더 하나), 고정 4/4 마디 길이(채널 02 미지원).
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

        public BmsNoteEvent WithKind(BmsNoteKind kind) => new BmsNoteEvent(Channel, BeatNumerator, BeatDenominator, SoundId, kind);
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
        // 주입기가 Area.length를 이 값으로 맞추므로 internal로 공개한다.
        internal const int MeasureBeats = 4;

        // 노트로 취급하지 않는 채널: 01=BGM, 02=마디 길이, 03/08=BPM변경, 09=STOP.
        private static readonly HashSet<string> IgnoredChannels = new HashSet<string> { "01", "02", "03", "08", "09" };

        // 읽기는 하지만 반영하지 못하는 채널. 차트에 있으면 로그로 알려 준다.
        private static readonly Dictionary<string, string> UnsupportedChannelNames = new Dictionary<string, string>
        {
            ["02"] = "마디 길이 변경(02)",
            ["03"] = "BPM 변경(03)",
            ["08"] = "BPM 변경(08)",
            ["09"] = "STOP(09)",
        };

        public double Bpm { get; private set; } = 120;
        public int SuppressedNoiseCount { get; private set; }

        // 짝이 맞지 않아 일반 노트로 바꾼 롱노트 마커 수. 짝이 깨진 linked 노트는 게임에서 무결성 오류를 낸다.
        public int DemotedHoldCount { get; private set; }

        // 채널 01에 놓인 곡 음원(music.ogg)의 시작 위치(비트). 노트는 이만큼 앞으로 당겨졌다.
        public double MusicStartBeat { get; private set; }

        // 음원 시작보다 앞에 있어 버린 노트 수.
        public int DroppedBeforeMusicCount { get; private set; }

        public string EncodingName { get; private set; } = "UTF-8";
        public IReadOnlyList<string> UnsupportedFeatures { get; private set; } = Array.Empty<string>();
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

        // musicFileName: 앨범의 곡 음원 파일 이름(예: "music.ogg"). 주면 채널 01에서 그 음원이 놓인 위치를 찾아
        // 노트 전체를 그만큼 앞으로 당긴다. 게임은 곡 음원을 차트 0초에 재생하기 때문이다.
        public static BmsChart? TryParse(string filePath, string? musicFileName = null)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string[] rawLines = ReadBmsLines(filePath, out string encodingName);

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
                    // ID와 파일명 사이는 공백 또는 탭. 파일명 안의 공백("hold 시작.wav")은 그대로 둔다.
                    int separatorIndex = line.IndexOfAny(new[] { ' ', '\t' });
                    string idPart = separatorIndex > 4 ? line.Substring(4, separatorIndex - 4) : line.Substring(4);
                    string fileName = separatorIndex > 4 ? line.Substring(separatorIndex + 1).Trim() : string.Empty;
                    wavDefinitions.Add((idPart.Trim(), fileName));
                    continue;
                }

                // 기본 BPM은 "#BPM 값" 형태만 읽는다. "#BPM01 180" 같은 줄은 BPM 변경용 정의(채널 08이 참조)라
                // 여기서 읽으면 마지막 정의 값이 기본 BPM을 덮어써 곡 전체 싱크가 어긋난다.
                if (line.Length > 4
                    && line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase)
                    && char.IsWhiteSpace(line[4]))
                {
                    string valueStr = line.Substring(5).Trim();
                    if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedBpm) && parsedBpm > 0)
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
            var musicSoundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string rawId, string fileName) in wavDefinitions)
            {
                string soundId = NormalizeSoundId(rawId, extended);
                kindBySoundId[soundId] = ClassifyNoteKind(fileName);

                if (musicFileName is not null
                    && fileName.Length > 0
                    && string.Equals(Path.GetFileName(fileName), musicFileName, StringComparison.OrdinalIgnoreCase))
                {
                    musicSoundIds.Add(soundId);
                }
            }

            var chart = new BmsChart { Bpm = bpm, EncodingName = encodingName };
            chart.SoundKinds = kindBySoundId;
            var measuresByIndex = new Dictionary<int, BmsMeasure>();
            var unsupported = new SortedSet<string>(StringComparer.Ordinal);

            // 채널 01에서 곡 음원이 처음 나오는 위치(마디, 분자, 분모). 없으면 0.
            (int Measure, int Num, int Den)? musicStart = null;

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
                string data = line.Substring(7).Trim();

                if (!int.TryParse(measureText, out int measureIndex) || !int.TryParse(channelText, out int channel))
                {
                    continue;
                }

                if (IgnoredChannels.Contains(channelText)
                    && !string.Equals(channelText, "01", StringComparison.Ordinal))
                {
                    if (UsesUnsupportedChannel(channelText, data)
                        && UnsupportedChannelNames.TryGetValue(channelText, out string? featureName))
                    {
                        unsupported.Add(featureName);
                    }

                    continue;
                }

                if (data.Length == 0 || data.Length % chunkWidth != 0)
                {
                    continue;
                }

                int slotCount = data.Length / chunkWidth;

                if (string.Equals(channelText, "01", StringComparison.Ordinal))
                {
                    if (musicSoundIds.Count > 0)
                    {
                        for (int slot = 0; slot < slotCount; slot++)
                        {
                            string normalized = NormalizeSoundId(data.Substring(slot * chunkWidth, chunkWidth), extended);
                            if (!musicSoundIds.Contains(normalized))
                            {
                                continue;
                            }

                            (int num, int den) = ReduceFraction(slot * MeasureBeats, slotCount);
                            if (musicStart is null || ComparePosition(measureIndex, num, den, musicStart.Value.Measure, musicStart.Value.Num, musicStart.Value.Den) < 0)
                            {
                                musicStart = (measureIndex, num, den);
                            }
                        }
                    }

                    continue;
                }

                if (!measuresByIndex.TryGetValue(measureIndex, out BmsMeasure? targetMeasure))
                {
                    targetMeasure = new BmsMeasure(measureIndex);
                    measuresByIndex[measureIndex] = targetMeasure;
                }

                for (int slot = 0; slot < slotCount; slot++)
                {
                    string chunk = data.Substring(slot * chunkWidth, chunkWidth);
                    string normalized = NormalizeSoundId(chunk, extended);
                    if (normalized == "00" || normalized == "0")
                    {
                        continue;
                    }

                    (int numerator, int denominator) = ReduceFraction(slot * MeasureBeats, slotCount);

                    BmsNoteKind kind = kindBySoundId.TryGetValue(normalized, out BmsNoteKind found)
                        ? found
                        : BmsNoteKind.Normal;

                    targetMeasure.Notes.Add(new BmsNoteEvent(channel, numerator, denominator, normalized, kind));
                }
            }

            chart.UnsupportedFeatures = unsupported.ToArray();

            if (musicStart is not null && (musicStart.Value.Measure > 0 || musicStart.Value.Num > 0))
            {
                measuresByIndex = ShiftToMusicStart(measuresByIndex, musicStart.Value, out int dropped);
                chart.DroppedBeforeMusicCount = dropped;
                chart.MusicStartBeat = (musicStart.Value.Measure * MeasureBeats) + ((double)musicStart.Value.Num / musicStart.Value.Den);
            }

            int suppressedNoiseCount = 0;
            if (ExperimentChartSettings.EnableNoiseSuppression)
            {
                foreach (BmsMeasure measure in measuresByIndex.Values)
                {
                    if (measure.Notes.Count > 1)
                    {
                        var uniqueNotes = new List<BmsNoteEvent>();
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
                                if (ExperimentChartSettings.EnableNoiseSuppressionDebug)
                                {
                                    MelonLoader.MelonLogger.Msg($"[BmsNoiseFilter] 마디 {measure.Index} 채널 {note.Channel} 위치 {note.BeatNumerator}/{note.BeatDenominator} 중복/노이즈 노트 억제 (SoundID: {note.SoundId})");
                                }
                            }
                        }

                        measure.Notes.Clear();
                        measure.Notes.AddRange(uniqueNotes);
                    }
                }
            }

            chart.SuppressedNoiseCount = suppressedNoiseCount;

            // 한 채널이 여러 줄에 나뉘어 있어도 마디 안 노트가 시간순이 되도록 정렬한다.
            // 롱노트 짝 맞추기가 시간순을 전제로 한다.
            foreach (BmsMeasure measure in measuresByIndex.Values)
            {
                SortByPosition(measure.Notes);
            }

            // 중간에 비어있는 마디(노트가 없는 마디)가 누락되면 타임라인 영역(Area) 순서가 당겨져
            // 노트가 곡보다 일찍 끝나고 박자가 어긋나게 된다. 0부터 maxIndex까지 모든 마디를 순서대로 채운다.
            int maxMeasureIndex = measuresByIndex.Count > 0 ? measuresByIndex.Keys.Max() : 0;
            for (int i = 0; i <= maxMeasureIndex; i++)
            {
                if (measuresByIndex.TryGetValue(i, out BmsMeasure? measure))
                {
                    chart.Measures.Add(measure);
                }
                else
                {
                    chart.Measures.Add(new BmsMeasure(i));
                }
            }

            chart.DemotedHoldCount = NormalizeHoldPairs(chart.Measures);
            return chart;
        }

        // 채널 02는 마디 길이 배율(소수)이라 1이 아니면 변경, 나머지(03/08/09)는 오브젝트가 하나라도 있으면 사용.
        private static bool UsesUnsupportedChannel(string channelText, string data)
        {
            if (string.Equals(channelText, "02", StringComparison.Ordinal))
            {
                return double.TryParse(data, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ratio)
                    && Math.Abs(ratio - 1d) > 1e-9;
            }

            return data.Trim('0').Length > 0;
        }

        // 곡 음원이 채널 01의 (마디, 분자/분모) 위치에서 시작하면, 모든 노트를 그만큼 앞으로 당긴다.
        // 위치는 전부 "비트 단위 분수"라 정수 연산으로 정확하게 옮긴다(부동소수점 오차 없음).
        private static Dictionary<int, BmsMeasure> ShiftToMusicStart(
            Dictionary<int, BmsMeasure> measuresByIndex,
            (int Measure, int Num, int Den) offset,
            out int dropped)
        {
            dropped = 0;
            var shifted = new Dictionary<int, BmsMeasure>();
            long offsetNum = ((long)offset.Measure * MeasureBeats * offset.Den) + offset.Num;
            long offsetDen = offset.Den;

            foreach (BmsMeasure measure in measuresByIndex.Values)
            {
                foreach (BmsNoteEvent note in measure.Notes)
                {
                    long noteNum = ((long)measure.Index * MeasureBeats * note.BeatDenominator) + note.BeatNumerator;
                    long noteDen = note.BeatDenominator;

                    // note - offset = (noteNum*offsetDen - offsetNum*noteDen) / (noteDen*offsetDen)
                    long num = (noteNum * offsetDen) - (offsetNum * noteDen);
                    long den = noteDen * offsetDen;
                    if (num < 0)
                    {
                        dropped++;
                        continue;
                    }

                    long measureSpan = MeasureBeats * den;
                    int newIndex = (int)(num / measureSpan);
                    long remainder = num - (newIndex * measureSpan);
                    long gcd = Gcd(remainder, den);

                    if (!shifted.TryGetValue(newIndex, out BmsMeasure? target))
                    {
                        target = new BmsMeasure(newIndex);
                        shifted[newIndex] = target;
                    }

                    target.Notes.Add(new BmsNoteEvent(note.Channel, (int)(remainder / gcd), (int)(den / gcd), note.SoundId, note.Kind));
                }
            }

            return shifted;
        }

        // 레인(채널)별로 시간순으로 훑어 시작/끝 짝을 맞춘다. 짝이 없는 마커는 일반 노트로 바꾼다.
        // - 시작이 열린 채로 또 시작이 오면 앞의 시작을 일반 노트로
        // - 열린 시작 없이 끝이 오면 그 끝을 일반 노트로
        // - 곡 끝까지 닫히지 않은 시작은 일반 노트로
        private static int NormalizeHoldPairs(List<BmsMeasure> measures)
        {
            int demoted = 0;
            var openStart = new Dictionary<int, (BmsMeasure Measure, int Index)>();

            foreach (BmsMeasure measure in measures)
            {
                for (int i = 0; i < measure.Notes.Count; i++)
                {
                    BmsNoteEvent note = measure.Notes[i];
                    if (note.Kind == BmsNoteKind.HoldStart)
                    {
                        if (openStart.TryGetValue(note.Channel, out (BmsMeasure Measure, int Index) previous))
                        {
                            Demote(previous.Measure, previous.Index);
                            demoted++;
                        }

                        openStart[note.Channel] = (measure, i);
                    }
                    else if (note.Kind == BmsNoteKind.HoldEnd)
                    {
                        if (!openStart.Remove(note.Channel))
                        {
                            Demote(measure, i);
                            demoted++;
                        }
                    }
                }
            }

            foreach ((BmsMeasure Measure, int Index) unclosed in openStart.Values)
            {
                Demote(unclosed.Measure, unclosed.Index);
                demoted++;
            }

            return demoted;
        }

        private static void Demote(BmsMeasure measure, int index)
        {
            measure.Notes[index] = measure.Notes[index].WithKind(BmsNoteKind.Normal);
        }

        // 위치(분수) → 채널 → 원래 순서로 정렬(같은 위치는 원래 순서 유지).
        private static void SortByPosition(List<BmsNoteEvent> notes)
        {
            if (notes.Count < 2)
            {
                return;
            }

            var indexed = notes.Select((note, index) => (Note: note, Index: index)).ToList();
            indexed.Sort((a, b) =>
            {
                int byPosition = ComparePosition(0, a.Note.BeatNumerator, a.Note.BeatDenominator, 0, b.Note.BeatNumerator, b.Note.BeatDenominator);
                if (byPosition != 0)
                {
                    return byPosition;
                }

                int byChannel = a.Note.Channel.CompareTo(b.Note.Channel);
                return byChannel != 0 ? byChannel : a.Index.CompareTo(b.Index);
            });

            notes.Clear();
            notes.AddRange(indexed.Select(entry => entry.Note));
        }

        private static int ComparePosition(int measureA, int numA, int denA, int measureB, int numB, int denB)
        {
            if (measureA != measureB)
            {
                return measureA.CompareTo(measureB);
            }

            return ((long)numA * denB).CompareTo((long)numB * denA);
        }

        private static (int Num, int Den) ReduceFraction(int numerator, int denominator)
        {
            int gcd = (int)Gcd(numerator, denominator);
            return (numerator / gcd, denominator / gcd);
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

        // BMS는 UTF-8 말고도 CP949(한국어 에디터), Shift-JIS로 저장되는 일이 많다. 잘못 읽으면 #WAV 파일명의
        // "시작"/"끝"이 깨져 롱노트가 일반 노트로 인식된다. BOM → 엄격한 UTF-8 → CP949 순으로 시도한다.
        private static string[] ReadBmsLines(string filePath, out string encodingName)
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encodingName = "UTF-16LE";
                return SplitLines(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encodingName = "UTF-16BE";
                return SplitLines(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2));
            }

            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try
            {
                var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                encodingName = "UTF-8";
                return SplitLines(strictUtf8.GetString(bytes, start, bytes.Length - start));
            }
            catch (DecoderFallbackException)
            {
            }

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                encodingName = "CP949";
                return SplitLines(Encoding.GetEncoding(949).GetString(bytes));
            }
            catch (Exception)
            {
            }

            encodingName = "UTF-8(깨진 글자 대체)";
            return SplitLines(Encoding.UTF8.GetString(bytes, start, bytes.Length - start));
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
