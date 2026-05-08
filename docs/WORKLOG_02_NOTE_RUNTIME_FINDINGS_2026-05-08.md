# STARGAZER Custom Chart - 문서 2/2
# 노트 구조/시간축 조작 확정 결과 (2026-05-08)

## 목적

- 런타임에서 노트 데이터가 실제 플레이에 반영되는지 검증한다.
- 시간축 조작의 유효 경로와 링크 노트 안전 규칙을 확정한다.

## UI 자켓 탐색 확정

- 플레이 씬 목표 경로 확정
  - `Play Layer/BG Layer/Jacket`
- 플레이 참조 확정 로그
  - `[PlayerBaseJacket] type=UnityEngine.UI.Image scene=Play path=Play Layer/BG Layer/Jacket`
  - `[ImageProbe] viewer.CurrentTrackViewer.jacketImage=UnityEngine.UI.Image`
- 현재 운영 방식
  - 리소스 전수 열거(`Resources.FindObjectsOfTypeAll<T>()` 기반 JacketProbe)는 제거
  - `PlayerBase.Play` + `CurrentTrackViewer.Listen` 후킹으로 자켓 참조를 직접 확보

## 노트 구조 확정

- 런타임 경로
  - `Pattern -> Layers -> Areas -> notes`
- 핵심 멤버
  - `Area.notes`: writable (실제 수정 타깃)
  - `Area.Notes`: readonly view 성격
- 관측 샘플
  - `totalAreas=80`, `totalNotes=63`, `notesMemberWritable=True`

## 2026-05-07 "오늘 작업" 이관 항목

- 노트 구조 탐색 결론
  - 최종 수정 타깃은 `Area.notes`(writable), `Area.Notes`는 readonly 뷰.
  - 당시 요약 로그: `totalAreas=80`, `resolvedNotesMembers=80`, `totalNotes=63`, `notesMemberWritable=True`
- 결과 화면 탐색 연계 정보
  - `PlayInfoViewer.ShowPlayInfo(ITravelResultData)` 후킹에서 자켓 멤버 `jacket` 확인.
  - 노트 실험과 별개로 UI 탐색 경로가 분리되어 있음(안정성 점검에 유리).

## 노트 타입/링크 구조 확정

- 노트 타입
  - 샘플 기준 `Il2CppMikazuki.Note` 단일 타입.
- 링크 구분
  - 별도 클래스가 아니라 `Note.property.linked` enum 값으로 구분.
  - 분포 예시: `None:55`, `StartPoint:4`, `EndPoint:4`
- 안전 규칙
  - `linked=None`은 단독 수정 가능.
  - `StartPoint/EndPoint`는 쌍 단위 유지 필요.
  - 한쪽만 삭제/변경 시 `uncompleted linked note` 같은 무결성 오류 가능.

## 멤버 카탈로그 확정 (이관+추가)

- `Il2CppMikazuki.Note`
  - 주요 프로퍼티: `TargetLaneUID(rw)`, `Owner(rw)`, `beatInfo(rw)`, `property(rw)`, `UniqueHash(ro)`, `BeatValue(ro)`
- `Il2CppMikazuki.BeatInfo`
  - 프로퍼티: `BeatValue(ro)`, `IsNull(ro)`, `IsZero(ro)`
  - 필드: `BeatSplit(Int32)`, `BeatIndex(Int32)`
- `Il2CppMikazuki.NoteProperty`
  - 프로퍼티: `linked(rw)`, `expressionHolder(rw)`, `Expressions(ro)`

## 노트 제거/선별 실험 경과

- 전체 삭제: 링크 무결성 오류로 크래시 발생.
- 첫 노트만 삭제: 크래시는 피했으나 목표와 불일치.
- 전역 최조기 노트만 유지: 성공.
  - 즉, `Area.notes` 수정이 실제 게임 플레이에 반영됨을 재확인.

## 판정/오토 보류와 노트 집중 전략 (이관)

- IL2CPP에서 판정/오토 직접 제어는 위험/불안정 구간으로 확인.
- 오토플레이는 `PlayerBase.IsAutoPlay` 경로에서만 운영 확정.
- 판정 해제/강제 재시도 기준 앵커는 `Assembly-CSharp`의 `Il2CppStargazer.Play.Widgets.JudgementViewerWidget`으로 고정.
- 따라서 커스텀 차트 핵심은 노트 데이터 변형 라인으로 집중:
  - 시간축(`BeatIndex/BeatSplit`) 조작
  - 링크 쌍 무결성 유지
  - 런타임 컬렉션(`Area.notes`) 직접 반영

## 시간축(타이밍) 조작 확정

- 구조
  - `Note.BeatValue`: readonly (직접 set 불가)
  - `Note.beatInfo`: writable 접근 가능
  - `BeatInfo` 필드: `BeatIndex`, `BeatSplit`
- 핵심 발견
  - `BeatInfo` 필드만 수정하면 반영이 안 되는 경우가 있음(복사본 수정 문제).
  - **필드 수정 후 `note.beatInfo` 재할당(write-back)** 해야 안정 반영됨.

## 최신 검증 로그 해석 (확정)

- 관측 로그
  - `[BeatShiftTest] ... BeatIndex 0->1, BeatSplit 192->24, BeatValue 0->0.042, UniqueHash L3_[0,192]->L3_[1,24]`
  - `[NoteWipe] ... keptTime=0 keptTimeNow=0.042`
- 결론
  - `BeatValue`가 실시간으로 바뀜 -> 시간축 조작 성공.
  - `keptTimeNow`가 변경값 반영 -> 수정 후 재측정 일치.
  - `UniqueHash`도 동기화되어 갱신됨 -> 식별자 일관성 유지 확인.

## 현재 기술 결론

- 커스텀 차트 구현 가능성은 충분히 높음.
- 우선순위는 아래 순서가 안전함:
  - `linked=None` 노트 대상 시간/레인 변형기 고도화
  - 링크 노트 쌍(`StartPoint/EndPoint`) 동시 이동 규칙 구현
  - 전체 패턴 단위 변환기로 확장
