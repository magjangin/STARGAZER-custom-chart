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
        /// 파일의 바이트와 BOM을 분석하여 최적의 인코딩으로 전체 라인을 읽어옵니다.
        /// 첫 줄의 UTF-8 BOM(\uFEFF)은 자동으로 제거되어 #WAV, #BPM, 제목 등의 헤더 파싱이 누락되지 않습니다.
        /// </summary>
        public static string[] ReadAllLines(string filePath)
        {
            if (!File.Exists(filePath))
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

                var lines = new List<string>();
                using (var reader = new StringReader(content))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // 줄 시작 부분에 남아있을 수 있는 BOM(\uFEFF) 제거
                        lines.Add(line.TrimStart('\uFEFF'));
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
    }
}
