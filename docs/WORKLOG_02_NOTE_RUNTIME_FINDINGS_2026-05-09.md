# STARGAZER Custom Chart - 문서 2/2
# 노트 구조/런타임 운용 요약 (2026-05-09)

## 목적

- 노트 변형 운영 라인의 현재 상태를 오늘 기준으로 정리한다.
- 오토/판정 분리 정책을 노트 문서에도 동기화한다.

## 오늘 동기화 사항

- 오토플레이 운영 경로 확정
  - `PlayerBase.IsAutoPlay` 경로를 운영 기준으로 유지.
- 판정 재시도 앵커 명시
  - `Assembly-CSharp`의 `Il2CppStargazer.Play.Widgets.JudgementViewerWidget` 중심으로만 재접근.
  - 운영 라인에서는 판정 강제/치환 로직을 제외.

## 노트 변형 라인 상태

- 유효 경로 유지
  - `Pattern -> Layers -> Areas -> notes`
  - 실제 수정 타깃은 `Area.notes`(writable) 기준.
- 시간축 조작 규칙 유지
  - `BeatInfo` 필드 수정 후 `note.beatInfo` write-back 필요.
- 링크 안정성 규칙 유지
  - `linked=None` 우선 적용,
  - `StartPoint/EndPoint`는 쌍 단위 동시 처리.

## 운영 우선순위 (오늘판)

- 1단계: `linked=None` 대상 변형기 고도화
- 2단계: 링크 쌍 동시 이동 규칙 제품화
- 3단계: 패턴 단위 변환기로 확장

## 결론

- 노트 변형 라인은 운영 가능한 안정 축으로 유지한다.
- 판정 제어는 별도 연구 트랙으로 분리하고, 운영 빌드에는 넣지 않는다.
