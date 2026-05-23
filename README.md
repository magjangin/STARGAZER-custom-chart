# 🌟 STARGAZER Custom Chart (식스타게이저 커스텀 차트 모드)

> **식스타게이트 스타트레일 (Sixtar Gate: STARTRAIL)** 을 위한 MelonLoader 기반 런타임 후킹 및 커스텀 차트 지원 모드입니다.

⚠️ **중요 안내**: 본 프로젝트는 현재 **활발하게 개발 및 실험 중(Work In Progress)**인 프로젝트입니다. 실구현 상태나 게임 패치에 따라 로직이 지속적으로 업데이트 및 변경될 수 있습니다.

본 프로젝트는 게임 플레이 엔진의 동작을 정밀 분석하고, 커스텀 트랙의 메타데이터와 UI 요소를 오버라이드하며, 안정적인 오토플레이 및 판정 분석 도구를 제공하기 위한 모딩 솔루션입니다.

---

## ✨ 주요 기능 (Key Features)

### 🏷️ 트랙 정보 동적 오버라이드 (Track Info Overrides)
* 게임의 트랙 메타데이터 및 디스플레이 명칭 로직을 실시간으로 Harmony 후킹합니다.
* `TrackDisplayName`, `TrackDisplayNameEN`, `displayName` 등의 Property를 동적으로 감지하고 커스텀 차트에 걸맞은 이름으로 오버라이드합니다.

### 🤖 오토플레이 강제 제어 (Force AutoPlay)
* 플레이 시작 단계(`PlayerBase.Play`)의 `PRE` 훅에서 `IsAutoPlay` 값을 강제로 설정하여 안정적인 오토플레이 구동을 지원합니다.
* 오토플레이 제어와 판정 축을 독립적으로 운영하여 로직 충돌을 방지합니다.

### 🔍 자켓 및 UI 탐색기 (Jacket & UI Probing)
* `CurrentTrackViewer`, `Result.PlayInfoViewer` 등의 인게임 UI 컴포넌트를 관측합니다.
* 트랜스폼 경로(Transform Path), 게임 오브젝트 이름 및 연결된 자켓 이미지를 스냅샷 형태로 분석하고 로깅합니다.

### ⏱️ 지능형 로깅 스로틀링 (Log Throttling)
* 고주파(High-frequency)로 호출되는 후킹 지점의 로그 과다 발생을 방지하기 위한 스로틀링(Throttling) 메커니즘이 내장되어 있습니다.
* 1초에 최대 6회 호출로 제한하여 성능 부하와 로그 크기를 최적화합니다.

---

## 📂 프로젝트 구조 (Directory Structure)

```text
STARGAZER custom chart/
├── STARGAZER custom chart.slnx          # Visual Studio 2022 최신 솔루션 파일
├── .gitignore                           # 빌드 산출물 및 VS 설정 무시
├── README.md                            # 본 문서
├── scripts/
│   └── build_mod.bat                    # 모드 빌드 및 Mods 폴더 자동 배포 스크립트
├── docs/
│   ├── WORKLOG_...                      # 개발/안정화 및 분석 일지
│   └── TODAY_WORK_...                   # 일일 작업 일지 및 로드맵
└── STARGAZER custom chart/
    ├── STARGAZER custom chart.csproj    # C# 프로젝트 파일
    ├── Properties/                      # 어셈블리 정보
    └── src/                             # 소스 코드
        ├── Core/                        # 모드 진입점 (MelonMod 상속)
        ├── Hooks/                       # 주요 Harmony 패치 및 오토 플레이 제어
        ├── Overrides/                   # 트랙 표시 명칭 등 데이터 오버라이드
        └── Probes/                      # 노트 런타임 탐색 및 자켓 스냅샷
```

---

## 🛠️ 개발 및 빌드 환경 (Development Environment)

* **언어 및 프레임워크**: C# / .NET 6.0
* **모딩 툴**: MelonLoader, HarmonyLib
* **타겟 플랫폼**: PC Steam (Sixtar Gate: STARTRAIL, IL2CPP/Mono)

### 💻 빌드 및 배포 방법
1. Visual Studio를 통해 `STARGAZER custom chart.slnx` 솔루션을 엽니다.
2. 빌드를 실행하거나, `scripts/build_mod.bat` 스크립트를 사용하여 최신 빌드된 DLL 파일을 게임 디렉토리 내 `Mods/` 폴더에 자동으로 복사해 배포할 수 있습니다.

---

## 📝 개발 이력 및 로드맵 (Docs)
상세한 개발 일지와 리서치 기록은 [docs/](file:///h:/source/repos/STARGAZER%20custom%20chart/docs/) 디렉토리 내 문서를 참조하세요:
* [안정화 및 후킹 운영 기준 일지](file:///h:/source/repos/STARGAZER%20custom%20chart/docs/WORKLOG_01_STABILITY_AND_HOOKS_2026-05-09.md)
* [노트/런타임 운용 분석 일지](file:///h:/source/repos/STARGAZER%20custom%20chart/docs/WORKLOG_02_NOTE_RUNTIME_FINDINGS_2026-05-09.md)

---
*Created by [magjangin](https://github.com/magjangin)*
