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
            "자켓.png", "커버.png", "썸네일.png",
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
        };

        private static readonly string[] PreviewNameCandidates =
        {
            "music_preview.ogg", "music_preview.mp3", "music_preview.wav",
            "preview.ogg", "preview.mp3", "preview.wav",
            "bgm_preview.ogg", "bgm_preview.mp3", "bgm_preview.wav",
        };

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        private CustomAlbum(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Name = new DirectoryInfo(directoryPath).Name;
        }

        public string DirectoryPath { get; }
        public string Name { get; }
        public CustomTrackInfo? Info { get; private set; }
        public string? BmsPath { get; private set; }
        public string? MusicPath { get; private set; }
        public string? PreviewPath { get; private set; }
        public string? JacketPath { get; private set; }

        // 표시명은 info.txt의 제목을 쓰고, 없으면 폴더 이름으로 대체한다.
        public string DisplayName => string.IsNullOrEmpty(Info?.Title) ? Name : Info!.Title!;
        public string Artist => string.IsNullOrEmpty(Info?.Artist) ? "화영왕" : Info!.Artist!;

        // 곡으로 인정하는 최소 조건: 차트나 음원 중 하나는 있어야 한다.
        public bool IsPlayable => BmsPath is not null || MusicPath is not null;

        public static CustomAlbum? TryLoad(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            var album = new CustomAlbum(directoryPath);
            string[] files;
            try
            {
                files = Directory.GetFiles(directoryPath);
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

            album.BmsPath = FirstWithExtension(byExtension, ".bms");

            // 음원은 MusicNameCandidates 후보를 우선하고, 없으면 .ogg/.mp3 순으로 찾는다.
            // 폴더 내 BMS 키음(*.wav)과의 충돌을 피하기 위해 명시적인 음악 파일명 후보군을 우선시한다.
            album.MusicPath = FindFirstCandidate(byName, MusicNameCandidates)
                ?? FirstWithExtension(byExtension, ".ogg")
                ?? FirstWithExtension(byExtension, ".mp3");

            album.PreviewPath = FindFirstCandidate(byName, PreviewNameCandidates) ?? album.MusicPath;
            album.JacketPath = FindJacket(files, byName);

            string? infoPath = FirstNamed(byName, "info.txt") ?? FirstWithExtension(byExtension, ".txt");
            album.Info = infoPath is null ? null : CustomTrackInfo.TryParse(infoPath);

            return album;
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

        private static string? FindJacket(string[] files, Dictionary<string, string> byName)
        {
            string? candidateHit = FindFirstCandidate(byName, JacketNameCandidates);
            if (candidateHit is not null)
            {
                return candidateHit;
            }

            // 이름 후보와 안 맞아도 이미지 확장자면 자켓으로 인정한다(사용자가 자유롭게 이름 지어도 되도록).
            return files.FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
    }
}
