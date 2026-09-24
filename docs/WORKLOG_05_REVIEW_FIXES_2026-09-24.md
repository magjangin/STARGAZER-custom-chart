# STARGAZER Custom Chart - 문서 5
# 전체 코드 리뷰 후 로직·병목·구조 수정 (2026-09-24)

## 목적

- 저장소 전체(코드 47개 파일, 문서, 스크립트)를 읽고 로직 오류·병목·구조 문제를 찾아 고친다.
- 게임 없이도 컴파일과 BMS 파서 동작을 검증한다.

## 검증 방법

게임 DLL이 없는 환경이라 다음으로 대신했다(저장소에는 포함하지 않음).

- **컴파일**: NuGet의 MelonLoader 0.6.6 / HarmonyX 2.10.2 / Il2CppInterop 1.4.5와, 코드가 쓰는 Unity 타입만 흉내 낸
  스텁 어셈블리로 `src/` 전체를 빌드 — 수정 전·후 모두 경고 0, 오류 0.
- **BMS 파서**: `BmsChart.cs`만 떼어 콘솔 테스트 15개(기본 BPM, 롱노트 짝, 음원 오프셋, CP949, 탭/CRLF, 정렬) — 전부 통과.
- **게임 실측은 아직 안 함.** 아래 "게임에서 확인할 것"을 참고.

## 로직 수정

| # | 문제 | 수정 |
| --- | --- | --- |
| 1 | 훅 대상을 못 찾으면 `TargetMethods()`가 빈 목록 → Harmony `"Undefined target method"` → PatchAll 중단, 모드 전체 미기동. 74c550b의 수정(빈 목록 반환)은 이 문제를 그대로 남겼다 | 모든 패치 클래스가 `Prepare()`에서 대상을 해석(`PrepareTargets`)하고, 없으면 그 클래스만 건너뜀 |
| 2 | BMS 주입이 `EnableRuntimeProbeLogging`(진단 로그 플래그)에 묶여 있음 — 끄면 조용히 원본 차트 | 커스텀 곡이면 로그 설정과 무관하게 주입. 진단(LinkProbe/AreaProbe)만 플래그를 따름. 조용히 삼키던 예외도 로그로 |
| 3 | `#BPM01 180` 같은 BPM 변경 정의가 기본 BPM을 덮어씀 → 곡 전체 싱크 어긋남 | `#BPM` 뒤에 공백/탭이 오는 줄만 기본 BPM으로 읽음 |
| 4 | 복제 원본을 `StartsWith("startingpoint")`로 찾음 → 재즈 어레인지가 먼저 나오면 그 곡이 원본 | `startingpoint`와 정확히 일치하는 트랙만 원본 |
| 5 | 주입 때마다 식별 매핑을 비움 + 첫 항목만 보고 중복 판단 → 목록에 남은 이전 복제본이 "공식곡"으로 취급(원본 차트가 나오고 기록 저장 차단도 풀림) | 복제본을 세션에 한 번 만들어 재사용, 래퍼를 붙잡아 주소 재사용 차단, 목록 전체에서 중복 확인 |
| 6 | 오토플레이가 공식곡에도 적용 — 저장 차단은 커스텀 곡만 하므로 오토 기록이 공식 기록으로 저장될 수 있음 | 커스텀 곡에만 적용. 코드 기본값도 설정 파일과 같은 `false`로 |
| 7 | 롱노트 짝을 개수만 비교하고 그대로 주입(짝 깨진 linked 노트는 무결성 오류) | 레인별 시간순으로 짝을 맞추고 짝 없는 마커는 일반 노트로 |
| 8 | 채널 01의 곡 음원 위치를 무시 — `#001`에 음원을 두면 채보 전체가 밀림 | 음원 위치만큼 노트를 분수 단위로 당김(앞의 노트는 버림) |
| 9 | 음원만 있는 앨범은 Starting Point 채보, 차트만 있으면 Starting Point 음악으로 플레이 | 차트와 음원이 둘 다 있어야 곡으로 인정 |
| 10 | 기존 Areas를 먼저 비우고 새 Area를 만듦 — 중간 실패 시 빈 차트/마디 당겨짐 | 전부 만든 뒤에만 교체, 실패하면 원본 유지 |
| 11 | Area 길이를 템플릿에서 복사(4비트라는 보장 없음) | 4비트로 명시(분모는 원본과 같게) |
| 12 | 이름 부분일치 리플렉션이 인자 없는 메서드를 실제로 호출(`Clear()`, `Start()`, `Judge()` 등 위험) | 게터/세터 모양(`get_`/`Get`, `set_`/`Set`)만 호출 |
| 13 | CP949 BMS에서 "시작/끝" 판별 실패, `#WAV` 탭 구분 미지원, 한 채널이 여러 줄이면 노트 순서 뒤섞임 | 인코딩 자동 판별, 탭 허용, 마디 안 시간순 정렬 |
| 14 | 음원 경로의 `%`/`#`가 URI 특수 문자로 해석돼 로드 실패 | 두 글자만 이스케이프(기존 경로는 그대로) |

## 병목 수정

| 위치 | 문제 | 수정 |
| --- | --- | --- |
| 플레이 중 매 프레임 | `BGMPlayChecker`, `TravelPlayHandler.Play`에 공용 훅이 걸려 매 호출 리플렉션+문자열 생성(로그는 억제돼 아무것도 안 찍음) | 로그도 기능도 없는 대상 제거. `LogBgmDebugInvocation`은 대상 메서드일 때만 계산 |
| 노트 타격마다 | `PlaySFX` 4개 오버로드 진단 훅 | 오버로드 확인이 끝난 진단이라 제거 |
| 노트 연출 켰을 때 | 노트×프레임마다 IL2CPP 래퍼 3개 할당 | RectTransform 캐시. 풀링으로 재사용된 노트 감지 추가 |
| 게임 시작 | 모든 앨범 음원을 PCM으로 풀어 상주(곡당 수십 MB) | `compressed=true`로 압축 상태 유지(세터가 없으면 기존 방식, `config.txt`의 `CompressBgmInMemory=0`으로 끌 수 있음) |
| 채보 로딩 | 노트마다 생성자·`Add` 탐색, `Type.GetType`, 템플릿 속성 재조회 | 타입별 캐시, 템플릿은 한 번만 |
| 곡 목록 | 커서 이동마다 로그 7줄 | 1줄 |

## 구조 정리

- 패치 클래스 7개 파일(Play/Sound/TrackLoader/TrackSelector/Travel/AssetLoader×2) → `Hooks/Patches/InvocationPatches.cs` 1개
- BMS 주입기를 `Probes/Notes/`에서 `Bms/BmsChartInjector.cs`로 이동(진단 코드가 아니라 핵심 기능)
- 안 쓰는 코드 삭제: `TypeScanner.cs`, `SoundHookSupport.cs`, `TryApplyHarmonyAttributePatches`, `GetRuntimeInvocationPatchSpecs`,
  `ResolveOptionalTargetMethods`, `SetTrackId`, `DumpTrackListViewerTracks`, `EnumerateLevelSelectorLevels`, `DumpObjectValues`, 중복 `Gcd`
- 빌드 스크립트: 루트 `build.bat`은 `scripts/build_mod.bat` 호출만. `Platform="AnyCPU"`로 고쳐 `bin\Any CPU` 폴더와
  날짜 문자열 비교(지역 형식에 따라 틀림)를 없앰. csproj 참조 경로가 `$(GamePath)`를 따름
- 문서: 포인터뿐이던 `TODAY_WORK_*.md` 삭제, BMS 레인 표 오기 수정, README·가이드·파이프라인 문서 갱신

## 게임에서 확인할 것

1. 모드가 뜨고 `[HookPatch][Invocation] target: ...` 로그가 정상적으로 찍히는지
2. `[BmsInject] 채널→레인 매핑` 로그의 방향이 실제 화면과 맞는지(11/12/13은 오름차순 가정)
3. 음원이 `compressed`로 로드되는지(`[CustomBgm] DownloadHandlerAudioClip.compressed를 찾지 못해...` 경고가 없으면 적용됨)와 싱크.
   이상하면 `savecustomkey/config.txt`에서 `CompressBgmInMemory=0`
4. 곡 선택 화면을 여러 번 드나들어도 커스텀 곡이 중복되지 않고, 플레이 결과가 저장되지 않는지(`[SaveGuard]`)
5. 롱노트가 들어간 차트, 마디를 넘는 홀드

## 남은 것

- 커스텀 곡의 난이도 선택 화면 최고 기록이 Starting Point 기록으로 표시됨(TrackID 공유)
- 클래스 이름 `GameTypeEnumeratorMod`(초기 프로토타입 이름)과, 모든 파일이 한 partial 클래스인 구조는 그대로 둠
- BPM 변경·마디 길이 변경 미지원(이제는 경고 로그로 알려 줌)
