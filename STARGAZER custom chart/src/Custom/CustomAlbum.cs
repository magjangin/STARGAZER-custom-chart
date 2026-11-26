using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STARGAZER_custom_chart
{
    // hwa 아래 폴더 하나 = 커스텀 곡 하나("앨범"). 폴더 안의 파일을 역할별로 찾아 들고 있는다.
    // 게임/리플렉션 의존성 없는 순수 파일 조회라 단독으로 검증 가능하다.
    internal sealed class CustomAlbum
    {
        private static readonly string[] JacketNameCandidates =
        {
            "jacket.png", "jacket.jpg", "jacket.jpeg",
            "cover.png", "cover.jpg", "cover.jpeg",
            "thumbnail.png", "thumbnail.jpg", "thumbnail.jpeg",
            "illust.png", "illust.jpg", "illust.jpeg",
            "illustration.png", "illustration.jpg", "illustration.jpeg",
            "자켓.png", "커버.png", "썸네일.png", "일러스트.png",
            "封面.png", "封面.jpg", "封面.jpeg",
            "封套.png", "封套.jpg", "封套.jpeg",
            "曲绘.png", "曲绘.jpg", "曲绘.jpeg",
            "插图.png", "插图.jpg", "插图.jpeg",
        };

        private static readonly string[] MusicNameCandidates =
        {
            "music.ogg", "music.mp3", "music.wav",
            "bgm.ogg", "bgm.mp3", "bgm.wav",
            "audio.ogg", "audio.mp3", "audio.wav",
            "song.ogg", "song.mp3", "song.wav",
            "track.ogg", "track.mp3", "track.wav",
            "음원.ogg", "음원.mp3", "음원.wav",
            "음악.ogg", "음악.mp3", "음악.wav",
            "音乐.ogg", "音乐.mp3", "音乐.wav",
        };

        private static readonly string[] PreviewNameCandidates =
        {
            "music_preview.ogg", "music_preview.mp3", "music_preview.wav",
            "preview.ogg", "preview.mp3", "preview.wav",
            "bgm_preview.ogg", "bgm_preview.mp3", "bgm_preview.wav",
            "미리듣기.ogg", "미리듣기.mp3", "미리듣기.wav",
            "프리뷰.ogg", "프리뷰.mp3", "프리뷰.wav",
            "试听.ogg", "试听.mp3", "试听.wav",
        };

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        private static readonly HashSet<string> NonInfoTxtNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "readme.txt", "license.txt", "changelog.txt", "credits.txt",
        };

        private static readonly HashSet<string> NonJacketImageStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bg", "background", "bga", "stage", "banner", "wall", "wallpaper",
        };

        private static readonly string[] CanonicalLevels = { "cosmic", "stellar", "void" };

        private readonly Dictionary<string, string> _levelBmsPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _warnings = new List<string>();

        private CustomAlbum(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Name = new DirectoryInfo(directoryPath).Name;
        }

        public string DirectoryPath { get; }
        public string Name { get; }
        public CustomTrackInfo? Info { get; private set; }
        public string? BmsPath { get; private set; }
        public IReadOnlyDictionary<string, string> LevelBmsPaths => _levelBmsPaths;
        public string? MusicPath { get; private set; }
        public string? PreviewPath { get; private set; }
        public string? JacketPath { get; private set; }
        public IReadOnlyList<string> Warnings => _warnings;

        // info.txt 없이 BMS 파일만 넣어도 곡이 제대로 보이도록 BMS 헤더의 #TITLE/#ARTIST를 폴백으로 쓴다.
        public string? BmsTitle { get; private set; }
        public string? BmsArtist { get; private set; }

        // 표시명 우선순위: info.txt 제목 → BMS #TITLE → 폴더 이름.
        public string DisplayName =>
            !string.IsNullOrEmpty(Info?.Title) ? Info!.Title!
            : !string.IsNullOrEmpty(BmsTitle) ? BmsTitle!
            : Name;

        // 아티스트 우선순위: info.txt → BMS #ARTIST → 기본값.
        public string Artist =>
            !string.IsNullOrEmpty(Info?.Artist) ? Info!.Artist!
            : !string.IsNullOrEmpty(BmsArtist) ? BmsArtist!
            : DefaultArtist;

        internal const string DefaultArtist = "화영왕";

        // 곡으로 인정하는 최소 조건: 차트나 음원 중 하나는 있어야 한다.
        public bool IsPlayable => BmsPath is not null || _levelBmsPaths.Count > 0 || MusicPath is not null;

        public string? GetBmsPath(string? levelKey)
        {
            if (!string.IsNullOrWhiteSpace(levelKey)
                && _levelBmsPaths.TryGetValue(levelKey.Trim(), out string? levelPath))
            {
                return levelPath;
            }

            return BmsPath;
        }

        public static CustomAlbum? TryLoad(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            // 경로를 여기서 한 번 절대 경로로 정규화해 둔다. 자켓·BGM 서빙 훅(곡 선택 커서 이동마다 호출)은
            // 이 경로를 캐시 키로 그대로 쓰므로 호출마다 Path.GetFullPath를 다시 하지 않는다.
            try
            {
                directoryPath = Path.GetFullPath(directoryPath);
            }
            catch
            {
                return null;
            }

            var album = new CustomAlbum(directoryPath);
            string[] files;
            try
            {
                files = Directory.GetFiles(directoryPath);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }

            var byName = new Dictionary<string, string>(files.Length, StringComparer.OrdinalIgnoreCase);
            var byExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                byName.TryAdd(Path.GetFileName(file), file);
                string ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext))
                {
                    byExtension.TryAdd(ext, file);
                }
            }

            album.ResolveBmsPaths(files);

            // 음원은 MusicNameCandidates 후보를 우선하고, 없으면 .ogg/.mp3 순으로 찾는다.
            // 폴더 내 BMS 키음(*.wav)과의 충돌을 피하기 위해 명시적인 음악 파일명 후보군을 우선시한다.
            album.MusicPath = FindFirstCandidate(byName, MusicNameCandidates)
                ?? FirstWithExtension(byExtension, ".ogg")
                ?? FirstWithExtension(byExtension, ".mp3");

            album.PreviewPath = FindFirstCandidate(byName, PreviewNameCandidates) ?? album.MusicPath;
            album.JacketPath = album.FindJacket(files, byName);

            string? infoPath = album.ResolveInfoPath(files, byName);
            album.Info = infoPath is null ? null : CustomTrackInfo.TryParse(infoPath);

            album.WarnIfTooLarge(album.BmsPath, "BMS");
            foreach (string levelBms in album._levelBmsPaths.Values)
            {
                album.WarnIfTooLarge(levelBms, "BMS");
            }
            album.WarnIfTooLarge(infoPath, "info");

            album.LoadBmsHeaderFallback();

            return album;
        }

        private void WarnIfTooLarge(string? path, string label)
        {
            if (path is not null && SafeTextReader.IsTooLarge(path))
            {
                _warnings.Add($"{label} 파일 '{Path.GetFileName(path)}' 이(가) {SafeTextReader.MaxTextFileBytes / (1024 * 1024)}MB 상한을 넘어 읽지 않습니다.");
            }
        }

        // info.txt에 제목/아티스트가 모두 있으면 BMS를 열 필요가 없다. 하나라도 비어 있을 때만 헤더를 읽는다.
        private void LoadBmsHeaderFallback()
        {
            if (!string.IsNullOrEmpty(Info?.Title) && !string.IsNullOrEmpty(Info?.Artist))
            {
                return;
            }

            string? bms = BmsPath ?? _levelBmsPaths.Values.FirstOrDefault();
            if (bms is null)
            {
                return;
            }

            try
            {
                (BmsTitle, BmsArtist) = BmsChart.ReadHeaderMetadata(bms);
            }
            catch
            {
                // 보조 메타데이터라 실패해도 폴더 이름/기본값으로 계속 간다.
            }
        }

        private void ResolveBmsPaths(string[] sortedFiles)
        {
            var bmsFiles = sortedFiles
                .Where(f => string.Equals(Path.GetExtension(f), ".bms", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (bmsFiles.Count == 0)
            {
                return;
            }

            var generalBms = new List<string>();
            foreach (string file in bmsFiles)
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                string? matchedLevel = MatchLevelFromStem(stem);
                if (matchedLevel is not null)
                {
                    // 정확히 "cosmic.bms" 형태가 접미사("*_cosmic.bms")보다 우선한다.
                    if (!_levelBmsPaths.ContainsKey(matchedLevel)
                        || string.Equals(stem, matchedLevel, StringComparison.OrdinalIgnoreCase))
                    {
                        _levelBmsPaths[matchedLevel] = file;
                    }
                }
                else
                {
                    generalBms.Add(file);
                }
            }

            if (generalBms.Count > 1)
            {
                _warnings.Add($"기본 .bms 파일이 {generalBms.Count}개 발견되어 '{Path.GetFileName(generalBms[0])}' 를 사용합니다.");
            }

            BmsPath = generalBms.Count > 0
                ? generalBms[0]
                : (_levelBmsPaths.TryGetValue("stellar", out string? stellar)
                    ? stellar
                    : _levelBmsPaths.Values.FirstOrDefault());
        }

        private static string? MatchLevelFromStem(string stem)
        {
            foreach (string level in CanonicalLevels)
            {
                if (string.Equals(stem, level, StringComparison.OrdinalIgnoreCase)
                    || stem.EndsWith("_" + level, StringComparison.OrdinalIgnoreCase)
                    || stem.EndsWith("-" + level, StringComparison.OrdinalIgnoreCase))
                {
                    return level;
                }
            }

            return null;
        }

        private string? ResolveInfoPath(string[] sortedFiles, Dictionary<string, string> byName)
        {
            string? explicitInfo = FirstNamed(byName, "info.txt");
            if (explicitInfo is not null)
            {
                return explicitInfo;
            }

            var txtCandidates = sortedFiles
                .Where(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase)
                    && !NonInfoTxtNames.Contains(Path.GetFileName(f)))
                .ToList();

            if (txtCandidates.Count == 0)
            {
                return null;
            }

            if (txtCandidates.Count > 1)
            {
                _warnings.Add($"info.txt 없이 .txt 파일이 {txtCandidates.Count}개 있어 '{Path.GetFileName(txtCandidates[0])}' 를 사용합니다.");
            }

            return txtCandidates[0];
        }

        private static string? FindFirstCandidate(Dictionary<string, string> byName, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (byName.TryGetValue(candidate, out string? hit))
                {
                    return hit;
                }
            }
            return null;
        }

        private static string? FirstNamed(Dictionary<string, string> byName, string fileName)
        {
            return byName.TryGetValue(fileName, out string? hit) ? hit : null;
        }

        private static string? FirstWithExtension(Dictionary<string, string> byExtension, string extension)
        {
            return byExtension.TryGetValue(extension, out string? hit) ? hit : null;
        }

        private string? FindJacket(string[] sortedFiles, Dictionary<string, string> byName)
        {
            string? candidateHit = FindFirstCandidate(byName, JacketNameCandidates);
            if (candidateHit is not null)
            {
                return candidateHit;
            }

            var imageFiles = sortedFiles
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (imageFiles.Count == 0)
            {
                return null;
            }

            var preferred = imageFiles
                .Where(f => !NonJacketImageStems.Contains(Path.GetFileNameWithoutExtension(f)))
                .ToList();

            string selected = preferred.Count > 0 ? preferred[0] : imageFiles[0];
            if (imageFiles.Count > 1)
            {
                _warnings.Add($"자켓 후보 이미지가 {imageFiles.Count}개 있어 '{Path.GetFileName(selected)}' 를 자켓으로 사용합니다.");
            }

            return selected;
        }
    }
}
