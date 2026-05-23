# STARGAZER Custom Chart - 문서 1/2
# 안정화, 후킹, 오토/판정 운영 기준 (2026-05-09)

## 목적

- 운영 가능한 안정 라인과 실험 라인을 분리한다.
- 오토플레이는 확정 경로로 고정하고, 판정은 연구 축으로 제한한다.

## 오늘 확정 사항

- 오토플레이 경로 확정
  - `PlayerBase.Play` PRE에서 `PlayerBase.IsAutoPlay` 제어 경로가 실동작 확인됨.
  - 운영 기능은 오토 경로 중심으로 유지.
- 판정 강제 실험 정리
  - `JudgementViewerWidget.OnNoteJudged` 기반 판정 리라이트는 실험 후 운영 라인에서 제거.
  - 현재 운영 빌드에서는 `JudgeRewrite`/`JudgementDetail` 흐름 비활성.
- 판정 축 재접근 기준 명시
  - 판정 해제/강제 재시도 앵커:
    - `Assembly-CSharp`
    - `Il2CppStargazer.Play.Widgets.JudgementViewerWidget`
  - 오토 축(`PlayerBase.IsAutoPlay`)과 판정 축을 분리 운영.

## 빌드/배포 정리

- `build_mod.bat` 개선
  - `bin\Debug\net6.0`와 `bin\Any CPU\Debug\net6.0` 중 최신 DLL을 선택하도록 보정.
  - 구버전 DLL 오복사 가능성을 줄여 반영 신뢰도 향상.
- 오늘 배포 기준
  - 모드 DLL이 `Mods`에 정상 복사됨.
  - MSB3270 경고는 기존과 동일(운영상 허용 범위로 유지).

## 후킹 운영 기준(오늘판)

- 운영 라인(유지)
  - 플레이 진입/로딩/BGM/결과/트랙뷰어 등 안정 관측 축
  - getter/postfix 후처리(제목 변경) 계열
- 연구 라인(분리)
  - 판정 강제/해제 로직
  - 고빈도/부작용 큰 판정 이벤트 치환 실험

## 결론

- 기반 안정화는 약 80~90% 수준으로 판단.
- 운영 기능은 "안정 후처리 + 오토 확정 경로"로 유지하고,
- 판정은 `JudgementViewerWidget` 중심 연구 트랙에서 별도 진행한다.
