# STARGAZER Custom Chart - 문서 1/2
# 안정화, 후킹, 판정/오토 결론 (2026-05-08)

## 목적

- 플레이 크래시를 줄이고, 안정적으로 관측 가능한 후킹 체인을 유지한다.
- 실사용 가능한 기능(제목 변경, 노트 구조 접근)을 우선 확정한다.
- 위험도가 높은 영역(IL2CPP 판정/오토 직접 제어)은 분리해 관리한다.

## 확정된 성과

- 곡 목록 제목 후처리 성공
  - `Starting Point` -> `Starting Point [SP]` 반영 확인.
- 결과 화면 자켓 UI 경로 확인
  - `Il2CppStargazer.Travel.Result.PlayInfoViewer.jacket` 확인.
- 플레이 씬 자켓 오브젝트 경로 확정
  - 목표 경로: `Play Layer/BG Layer/Jacket`
  - 탐색 성공 로그: `[JacketProbe][FOUND] ... source=Resources.FindObjectsOfTypeAll<T>() ... path=Play Layer/BG Layer/Jacket`
- 런타임 탐색 방식 확정
  - 1차 성공 경로: `Resources.FindObjectsOfTypeAll<T>()`
  - fallback 경로: `Object.FindObjectsOfType(...)`, `SceneManager` 루트 트리 순회
  - 환경별 오버로드 차이를 대비해 다중 수집 방식 유지
- 플레이 진입 안정화
  - `PlayerBase.SetupPlay` 계열 후킹으로 발생하던 NRE/진입 불안정 이슈를 회피.
- 로그 노이즈 억제
  - 고빈도 메서드 suppress + 공통 로그 스로틀링 적용.

## 2026-05-09 추가 확정 사항

- 플레이 자켓 참조 최종 확정
  - `PlayerBase.Play` 시점 스냅샷: `PlayerBase.jacket -> UnityEngine.UI.Image`
  - 실제 대상: `scene=Play`, `path=Play Layer/BG Layer/Jacket`
- 플레이 위젯 후킹 실측 확인
  - `Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen` PRE/POST 호출 확인
  - 멤버 확인: `CurrentTrackViewer.jacketImage=UnityEngine.UI.Image`
- 오토 관련 실험 결과 정리
  - `JudgementExtension.OnAutoPlay` 강제 호출/관측 실험은 정책상 롤백
  - `TravelArgsExtension.SetAutoplay/Autoplay` 적용 실험도 최종 제거
- 오토 활성 경로 확정
  - `PlayerBase.Play` 진입 PRE에서 `IsAutoPlay`를 `true`로 설정
  - 확인 로그: `[PlayerBaseAutoPlay] applied=... before=... after=True`
  - 상태 스냅샷: `[PlayerBaseJacket] ... IsAutoPlay=False/True`
- 판정 해제/조작 관련 기준 명시
  - 현재 실측 기준 오토플레이 활성은 안정 동작하나, 판정 해제/강제는 동일 수준으로 확정되지 않음.
  - 판정 축을 다시 풀 때는 아래 타입을 기준 앵커로 잡아야 함:
    - `Assembly-CSharp`
    - `Il2CppStargazer.Play.Widgets.JudgementViewerWidget`
  - 즉, 판정 로직 분석/재시도는 `JudgementViewerWidget` 중심으로 진행하고, 오토(`PlayerBase.IsAutoPlay`)와 분리 운영.
- 리소스 전수 열거 제거
  - `Resources.FindObjectsOfTypeAll<T>()` 기반 `JacketProbe` 스캔 루프는 제거
  - 현재는 `PlayerBase`/`CurrentTrackViewer` 후킹 기반 참조 확보로 단순화

## 2026-05-07 "오늘 작업" 이관 항목

- 오늘 해결한 문제
  - `PlayerBase.SetupPlay` 진입 크래시(NRE) 차단
  - 후킹 로그 과다 출력(노이즈) 억제
  - 결과 화면 자켓 UI 멤버 탐색 성공
  - 노트 데이터 경로(`Area.notes`) 쓰기 가능 여부 확인
  - 후킹 코드 역할 분리(`HookTargets.cs`, `HookNoteProbes.cs`)
- 크래시 분석 요약
  - 증상: `DMD<Il2CppStargazer.Play.PlayerBase::SetupPlay>` 구간 `NullReferenceException`
  - 조치: 런타임 패치 목록에서 `PlayerBase.SetupPlay` 제외
  - 결과: 해당 경유 크래시 재현되지 않음
- 로그 정책
  - suppress 대상: `SoundPlayer.GetBGMHandler`, `INNER_TravelPlayHandler.BGMPlayChecker`
  - 공통 PRE/POST 후킹 로그 스로틀링 적용

## 안정화 관점 변경 사항

- 유지한 주요 후킹 축
  - 플레이 진입/재생: `Play`, `PlayStart`, `StargazerPlayer.Load`, `INNER_TravelPlayHandler.Play`
  - 차트 로드: `TrackData.LoadPattern`, `PatternLoader.Load`, `PatternLoader._Load_b__5_0`
  - 오디오: `PlayBGM`, `StopBGM` 등
  - 플레이 위젯: `CurrentTrackViewer.Listen`
  - 결과 UI: `PlayInfoViewer.ShowPlayInfo`
- 제외/보류한 후킹
  - `PlayerBase.SetupPlay`, `TravelPlayer.SetupPlay` (크래시 회피)

## 패치 등록/누락 관리 기준 (중요)

- 실제 패치 적용 기준은 `HookTargets.cs`의 `PatchSpec[]` 목록 하나로 통일한다.
- `HookPostfix` 내부 분기만 있고 `PatchSpec[]`에 없는 경우는 **미활성(실행 안 됨)**으로 간주한다.
- 런타임에서 메서드가 없거나 시그니처가 안 맞으면 `[HookPatch] target not found: ...` 로그를 남기고 `missingCount`에 집계한다.
- 이번 정리 반영:
  - `Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen`를 `PatchSpec[]`에 등록해 실제 후킹 활성화.
  - 이전 오토 관련 실험 메서드(`JudgementExtension.OnAutoPlay`, `TravelArgsExtension.SetAutoplay/Autoplay`)는 롤백하여 패치 목록에서 제거.

## 2026-05-07 기준 패치 메서드 목록 (이관)

- 플레이 진입
  - `Il2CppStargazer.Play.PlayerBase.Play(TravelArgs)`
  - `Il2CppStargazer.Play.PlayerBase.PlayStart(IPlayHandler)`
  - `Il2CppStargazer.Play.StargazerPlayer.Load(TravelArgs, Action)`
- 차트 로드
  - `Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPattern(ELevels, Action)`
  - `Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader.Load(TravelArgs)`
  - `Il2CppStargazer.Play.StargazerPlayer+INNER_PatternLoader._Load_b__5_0(Pattern)`
- 오디오/BGM
  - `Il2CppStargazer.TrackLoader+INNER_TrackData.LoadBGMClip(Action`1 callback)`
  - `Il2CppStargazer.TrackLoader+INNER_TrackData.LoadPreviewClip(Action`1 callback)`
  - `Il2CppStargazer.Starlike.Sound.SoundPlayer.PlayBGM(AudioClip, ESoundType)`
  - `Il2CppStargazer.Starlike.Sound.SoundPlayer.GetBGMHandler()`
  - `Il2CppStargazer.Starlike.Sound.SoundPlayer.StopBGM()`
  - `Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler.BGMPlayChecker(Single)`
  - `Il2CppStargazer.Play.StargazerPlayer+INNER_TravelPlayHandler.Play(Single)`
- 플레이 위젯
  - `Il2CppStargazer.Play.Widgets.CurrentTrackViewer.Listen(...)`
- 결과 화면
  - `Il2CppStargazer.Travel.Result.PlayInfoViewer.ShowPlayInfo(ITravelResultData)`
- 곡 제목 후처리(Getter Postfix)
  - `Il2CppStargazer.TrackLoader+INNER_TrackData.get_TrackDisplayName()`
  - `Il2CppStargazer.TrackLoader+INNER_TrackData.get_TrackDisplayNameEN()`
  - `Il2CppStargazer.TrackLoader+INNER_TrackMetaData.get_displayName()`
  - `Il2CppStargazer.TrackLoader+INNER_TrackMetaData.get_displayNameEN()`
- 크래시 회피로 제외
  - `Il2CppStargazer.Play.PlayerBase.SetupPlay()`
  - `Il2CppStargazer.Play.TravelPlayer.SetupPlay()`

## 판정/오토플레이 실험 결론

- 결론
  - IL2CPP 환경에서 reflection + Harmony만으로 판정(`Judgement`)과 오토(`OnAutoPlay`)를 안정 제어하는 것은 현재 기준 매우 어려움.
- 이유
  - 중간 값 변경이 최종 표시/결과까지 이어지지 않거나, 탐색 경로에서 크래시 리스크가 확인됨.
- 현재 정책
  - `OnAutoPlay` 강제 호출/`TravelArgsExtension` 경로 실험은 롤백 완료.
  - 현재 오토 활성은 `PlayerBase.IsAutoPlay` 세팅 경로를 사용.
  - 판정은 `JudgementViewerWidget` 축에서만 재접근한다는 기준을 명시하고, 그 외 판정 강제 호출/치환은 운영 라인에서 제외.
  - 메인 라인은 안정 기능(제목 후처리 + 노트 데이터 변형)에 집중.

## 현재 빌드/운영 상태

- `dotnet build` 기준 정상 빌드 유지.
- 모드 DLL 배포 스크립트로 게임 `Mods` 반영 정상.
- 실험성 기능은 플래그 기반으로 제한 실행.
- `JacketProbe` 스캔 로직은 제거되었고, 자켓 경로 확인은 `PlayerBaseJacket`/`CurrentTrackViewer` 로그 기준으로 운영.

## 코드 분할 이관

- `HookTargets.cs`
  - 런타임 패치 등록, 공통 PRE/POST 로깅, UI/Image 프로브 중심.
- `HookNoteProbes.cs`
  - 노트 전용 프로브 로직 분리(`ProbeNoteArrayMembers`)
  - `Area.notes`/`Area.Notes` 해석
  - IL2CPP 컬렉션 순회(`IEnumerable` + `Count/get_Item` fallback)

## 다음 권장 방향 (안정 라인)

- 노트 변형 기능을 단계적으로 제품화
  - 단일 노트 -> 단일 Area -> 전체 패턴 순서로 확장.
- 판정/오토는 별도 연구 트랙으로 분리
  - 네이티브 레벨 분석 또는 더 안전한 고정 호출 지점 재탐색.
