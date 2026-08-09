# 문서 인덱스

이 폴더의 문서는 성격에 따라 세 종류로 나뉩니다.

| 접두어 | 성격 | 갱신 방식 |
| --- | --- | --- |
| `GUIDE_` | 사용자용 사용법. 모드를 쓰는 사람이 읽는다. | 기능이 바뀌면 갱신 |
| `REFERENCE_`, `*_REFERENCE` | 지속적으로 참조하는 사실 정리(구조/규칙/API). | 사실이 바뀌면 갱신 |
| `WORKLOG_` | 특정 날짜의 실측·검증 기록. 결론에 이른 근거를 남긴다. | 추가만 하고 고치지 않음 |

`WORKLOG`는 **그날의 판단 근거를 보존하는 것이 목적**이므로 나중에 결론이 바뀌어도 수정하지 않습니다.
대신 최신 상태는 `GUIDE`/`REFERENCE`에 반영하고, 뒤집힌 결론이 있으면 그쪽에 명시합니다.

---

## 사용자 가이드

- [GUIDE_CUSTOM_ALBUM.md](GUIDE_CUSTOM_ALBUM.md) — 커스텀 곡(앨범) 만들기: `hwa` 폴더 구성, `info.txt`, `savecustomkey/config.txt` 설정

## 참조 문서

- [REFERENCE_BMS_CONVERSION.md](REFERENCE_BMS_CONVERSION.md) — BMS 파일을 게임 차트로 바꾸는 규칙(채널·레인 매핑, 분수 매핑, 롱노트, 확장 사운드 ID)
- [REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md) — 커스텀 트랙이 목록에 뜨고 재생되기까지 어떤 훅이 무엇을 하는지
- [REFERENCE_DECOMPILED.md](REFERENCE_DECOMPILED.md) — `decompiled/` 폴더가 무엇인지, 무엇이 정확하고 무엇이 없는지, 재생성 방법
- [TRACK_METADATA_REFERENCE.md](TRACK_METADATA_REFERENCE.md) — 트랙 메타데이터 구조
- [CAST_AND_WRAPPER_CODE_REFERENCE.md](CAST_AND_WRAPPER_CODE_REFERENCE.md) — IL2CPP 캐스트/래퍼 코드가 왜 필요한지
- [NATIVE_HOOKS_CLARIFICATION.md](NATIVE_HOOKS_CLARIFICATION.md) — 이 저장소는 네이티브 훅을 쓰지 않는다는 설명

## 작업 일지

| 날짜 | 문서 | 주제 |
| --- | --- | --- |
| 2026-05-08 | [WORKLOG_01](WORKLOG_01_STABILITY_AND_HOOKS_2026-05-08.md), [WORKLOG_02](WORKLOG_02_NOTE_RUNTIME_FINDINGS_2026-05-08.md) | 안정화·후킹 / 노트 구조 |
| 2026-05-09 | [WORKLOG_01](WORKLOG_01_STABILITY_AND_HOOKS_2026-05-09.md), [WORKLOG_02](WORKLOG_02_NOTE_RUNTIME_FINDINGS_2026-05-09.md) | 운영 기준 정리 |
| 2026-07-09 | [WORKLOG_03](WORKLOG_03_AREA_STRUCTURE_AND_BMS_INJECTION_2026-07-09.md) | Area 구조 확정, BMS 주입 가능성 검증 |
| 2026-08-08 | [WORKLOG_04](WORKLOG_04_CUSTOM_TRACK_PIPELINE_2026-08-08.md) | BMS 파서 구현, 앨범 구조, 자켓/난이도 표시, 함정 4건 |

## 참고: 디컴파일 자료

게임 어셈블리의 타입/멤버 시그니처는 저장소 루트의 `decompiled/`에 있습니다(폴더 전체가 git에 올라가지 않으며,
[scripts/decompile.ps1](../scripts/decompile.ps1)로 재생성합니다). 자세한 내용은 [REFERENCE_DECOMPILED.md](REFERENCE_DECOMPILED.md)를 보세요.
메서드 본문은 없으므로 "이 메서드가 내부에서 뭘 하는가"는 런타임 프로브나 로그로 확인해야 합니다.
