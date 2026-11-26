using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace STARGAZER_custom_chart
{
    /// <summary>
    /// UTF-8(BOM 포함/미포함), UTF-16, 중국어(GB18030/GBK/GB2312/Big5), 한국어(CP949/EUC-KR), 일본어(Shift-JIS) 등
    /// 다국어 인코딩 환경에서 텍스트 파일을 안전하고 무결하게 읽어오는 리더 유틸리티입니다.
    /// </summary>
    internal static class SafeTextReader
    {
        private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);
        private static Encoding? _chineseEncoding;

        static SafeTextReader()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _chineseEncoding = Encoding.GetEncoding("GB18030");
            }
            catch
            {
                try { _chineseEncoding = Encoding.GetEncoding("gb2312"); }
                catch { _chineseEncoding = null; }
            }
        }

        public static void EnsureInitialized()
        {
            // 정적 생성자 호출 유도
        }

        /// <summary>
        /// info.txt / config.txt / .bms 같은 텍스트 설정·차트 파일의 상한. 폴더에 엉뚱한 대용량 .txt가 섞여 있어도
        /// 통째로 메모리에 올려 시작이 느려지거나 메모리를 쓰지 않도록 이 크기를 넘으면 읽지 않는다.
        /// (실제 BMS 차트는 수백 KB 수준이라 16MB면 충분히 넉넉하다.)
        /// </summary>
        public const long MaxTextFileBytes = 16L * 1024 * 1024;

        /// <summary>파일이 <see cref="MaxTextFileBytes"/>를 넘으면 true. 파일이 없거나 길이를 못 읽으면 false.</summary>
        public static bool IsTooLarge(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                return info.Exists && info.Length > MaxTextFileBytes;
            }
            catch
            {
                return false;
            }
        }

        private static string DecodeBytes(byte[] bytes)
        {
            string content;

            // 1. UTF-8 BOM 감지
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            // 2. UTF-16 LE BOM 감지
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                content = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            // 3. UTF-16 BE BOM 감지
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                content = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else
            {
                // 4. 엄격한 UTF-8 디코딩 시도 (잘못된 바이트 시 예외 발생)
                try
                {
                    content = Utf8Strict.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    // 5. UTF-8이 아닌 ANSI(중국어 GB18030/GBK 또는 시스템 기본 인코딩) 폴백
                    if (_chineseEncoding != null)
                    {
                        content = _chineseEncoding.GetString(bytes);
                    }
                    else
                    {
                        content = Encoding.Default.GetString(bytes);
                    }
                }
            }

            return content.TrimStart('\uFEFF');
        }

        /// <summary>
        /// 파일의 바이트와 BOM을 분석하여 최적의 인코딩으로 전체 라인을 읽어옵니다.
        /// 첫 줄의 UTF-8 BOM(\uFEFF)은 자동으로 제거되어 #WAV, #BPM, 제목 등의 헤더 파싱이 누락되지 않습니다.
        /// </summary>
        public static string[] ReadAllLines(string filePath)
        {
            if (!File.Exists(filePath) || IsTooLarge(filePath))
            {
                return Array.Empty<string>();
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0)
                {
                    return Array.Empty<string>();
                }

                string content = DecodeBytes(bytes);

                var lines = new List<string>();
                using (var reader = new StringReader(content))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }

                return lines.ToArray();
            }
            catch
            {
                try
                {
                    return File.ReadAllLines(filePath);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
        }

        /// <summary>
        /// 텍스트 파일을 한 줄씩 지연 열거합니다. 헤더만 읽고 중간에 break할 때 전체 라인 배열/리스트를
        /// 메모리에 할당하지 않으므로, 대용량 BMS 차트의 메타데이터 조회 등에 유용합니다.
        /// </summary>
        public static IEnumerable<string> EnumerateLines(string filePath)
        {
            if (!File.Exists(filePath) || IsTooLarge(filePath))
            {
                yield break;
            }

            string? content = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0)
                {
                    yield break;
                }

                content = DecodeBytes(bytes);
            }
            catch
            {
                content = null;
            }

            if (content is not null)
            {
                using var reader = new StringReader(content);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
                yield break;
            }

            IEnumerable<string>? fallbackLines = null;
            try
            {
                fallbackLines = File.ReadLines(filePath);
            }
            catch
            {
                yield break;
            }

            if (fallbackLines is not null)
            {
                foreach (string fallbackLine in fallbackLines)
                {
                    yield return fallbackLine;
                }
            }
        }
    }
}
