using System;
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

            album.BmsPath = FirstWithExtension(files, ".bms");

            // 음원은 music.ogg를 우선한다. 폴더에는 BMS 키음(*.wav)도 같이 들어있으므로
            // 확장자 폴백은 .ogg로만 한정해서 키음을 곡 음원으로 오인하지 않게 한다.
            album.MusicPath = FirstNamed(files, "music.ogg") ?? FirstWithExtension(files, ".ogg");
            album.PreviewPath = FirstNamed(files, "music_preview.ogg") ?? album.MusicPath;
            album.JacketPath = FindJacket(files);

            string? infoPath = FirstNamed(files, "info.txt") ?? FirstWithExtension(files, ".txt");
            album.Info = infoPath is null ? null : CustomTrackInfo.TryParse(infoPath);

            return album;
        }

        private static string? FirstNamed(string[] files, string fileName)
        {
            return files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FirstWithExtension(string[] files, string extension)
        {
            return files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FindJacket(string[] files)
        {
            foreach (string candidate in JacketNameCandidates)
            {
                string? hit = FirstNamed(files, candidate);
                if (hit is not null)
                {
                    return hit;
                }
            }

            // 이름 후보와 안 맞아도 이미지 확장자면 자켓으로 인정한다(사용자가 자유롭게 이름 지어도 되도록).
            return files.FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
    }
}
