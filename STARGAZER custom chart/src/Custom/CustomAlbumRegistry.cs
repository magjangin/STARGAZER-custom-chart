using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;

namespace STARGAZER_custom_chart
{
    // hwa 폴더를 훑어 앨범(=커스텀 곡) 목록을 만든다. 결과는 캐시한다 — 트랙 주입, 자켓/BGM 서빙,
    // 난이도 표시 등 여러 훅이 매번 호출하므로 파일 IO를 반복하지 않는다.
    internal static class CustomAlbumRegistry
    {
        private static List<CustomAlbum>? _albums;

        public static string RootPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwa");

        public static IReadOnlyList<CustomAlbum> GetAlbums()
        {
            if (_albums is not null)
            {
                return _albums;
            }

            _albums = Scan();
            return _albums;
        }

        public static void Reset()
        {
            _albums = null;
        }

        private static List<CustomAlbum> Scan()
        {
            var result = new List<CustomAlbum>();
            string root = RootPath;

            if (!Directory.Exists(root))
            {
                MelonLogger.Warning($"[Album] hwa 폴더가 없습니다: {root}");
                return result;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Album] hwa 하위 폴더 스캔 실패: {ex.GetType().Name}: {ex.Message}");
                directories = Array.Empty<string>();
            }

            // 예외는 폴더마다 따로 잡는다. 한 폴더의 info.txt를 못 읽었다고 그 뒤 앨범까지 전부 빠지면 안 된다.
            foreach (string dir in directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                CustomAlbum? album = TryLoadAlbum(dir);
                if (album is null)
                {
                    continue;
                }

                if (!album.IsPlayable)
                {
                    MelonLogger.Warning($"[Album] 건너뜀(차트/음원 없음): {album.Name}");
                    continue;
                }

                result.Add(album);
            }

            // 곡으로 인정된 하위 폴더가 하나도 없으면 예전 방식(hwa 바로 아래에 파일을 두는 구성)으로 간주한다.
            if (result.Count == 0)
            {
                CustomAlbum? legacy = TryLoadAlbum(root);
                if (legacy is not null && legacy.IsPlayable)
                {
                    MelonLogger.Msg("[Album] 하위 폴더가 없어 hwa 루트를 앨범 하나로 사용합니다.");
                    result.Add(legacy);
                }
            }

            foreach (CustomAlbum album in result)
            {
                MelonLogger.Msg($"[Album] {album.Name}: title={album.DisplayName} artist={album.Artist} "
                    + $"bms={Describe(album.BmsPath)} music={Describe(album.MusicPath)} jacket={Describe(album.JacketPath)} "
                    + $"levels={(album.Info is null || album.Info.Levels.Count == 0 ? "<none>" : string.Join("/", album.Info.Levels.Select(kvp => $"{kvp.Key}={kvp.Value}")))}");
            }

            MelonLogger.Msg($"[Album] 총 {result.Count}개 앨범을 찾았습니다.");
            return result;
        }

        private static CustomAlbum? TryLoadAlbum(string directoryPath)
        {
            try
            {
                return CustomAlbum.TryLoad(directoryPath);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Album] 건너뜀(폴더 읽기 실패): {directoryPath}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static string Describe(string? path) => path is null ? "<none>" : Path.GetFileName(path);
    }
}
