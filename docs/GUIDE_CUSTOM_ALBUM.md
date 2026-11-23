# 커스텀 곡(앨범) 만들기

게임 폴더의 `hwa` 아래에 폴더를 하나 만들면 그것이 커스텀 곡 하나가 됩니다.
폴더를 여러 개 만들면 곡도 그만큼 목록에 추가됩니다.

```
Sixtar Gate STARGAZER/
├── hwa/
│   ├── どりーむもーど/          <- 곡 1
│   │   ├── hwa2.bms             차트
│   │   ├── music.ogg            음원
│   │   ├── Thumbnail.png        자켓
│   │   ├── info.txt             곡 정보
│   │   ├── normal.wav           BMS 키음(게임에서는 쓰이지 않음)
│   │   ├── hold 시작.wav
│   │   └── hold 끝.wav
│   └── 다른곡/                   <- 곡 2
│       └── ...
└── savecustomkey/
    └── config.txt               모드 설정
```

두 폴더 모두 게임을 처음 실행할 때 자동으로 만들어집니다.

## 폴더 안 파일

파일 이름은 아래 순서로 찾습니다. 앞의 것이 없으면 뒤의 것을 씁니다.

| 역할 | 찾는 순서 |
| --- | --- |
| 차트 | 난이도별 파일(`cosmic.bms`, `stellar.bms`, `void.bms` 또는 `*_cosmic.bms` 등) → 없으면 기본 `*.bms` (정렬 기준 첫 번째) |
| 음원 | `music` / `bgm` / `audio` / `song` / `track` (`.ogg` `.mp3` `.wav`) → 첫 번째 `*.ogg` → 첫 번째 `*.mp3` |
| 프리뷰 | `music_preview` / `preview` / `bgm_preview` (`.ogg` `.mp3` `.wav`) → 없으면 음원과 동일 |
| 자켓 | `jacket` / `cover` / `thumbnail` (`.png` `.jpg` `.jpeg`), `자켓.png` / `커버.png` / `썸네일.png`, `封面` / `封套` / `曲绘` / `插图` → 없으면 폴더 안 첫 번째 이미지(`.png` `.jpg` `.jpeg`, 배경 이미지 `bg.*` 등 제외) |
| 정보 | `info.txt` → 첫 번째 `*.txt` (`readme.txt` 제외) |

- **핫리로드**: 게임 실행 중에 `.bms`, 음원, 자켓 파일을 수정하면 파일 수정 시각(`LastWriteTimeUtc`) 변경을 감지해 다음 로드 시 자동으로 새로 읽습니다.

확장자 기준 음원 폴백이 `.ogg`/`.mp3`로만 한정된 이유가 있습니다. 폴더에는 BMS 키음(`*.wav`)이 함께 들어있는데,
임의의 `.wav`를 곡 음원으로 잘못 집으면 짧은 키음이 재생되기 때문입니다. 곡 음원을 `.wav`로 넣을 때는 반드시 `music.wav`처럼 지정된 이름을 쓰거나, 가급적 `.ogg`/`.mp3`로 넣으세요.

차트나 음원 중 하나도 없는 폴더는 곡으로 인정하지 않고 건너뜁니다(로그에 이유가 남습니다).

둘 중 하나만 있어도 곡으로는 인정되지만, **없는 쪽은 복제 원본인 Starting Point의 것이 그대로 쓰입니다.**

| 없는 파일 | 결과 |
| --- | --- |
| `*.bms` | Starting Point 원본 차트로 플레이 |
| `*.ogg` | Starting Point 원본 음원·프리뷰 재생 |
| 자켓 이미지 | Starting Point 원본 자켓 표시 |

## info.txt

```
곡 제목 : どりーむもーど
아티스트 : 화영왕
난이도 : cosmic, stellar, void
cosmic = 8
stellar = 8
void = 8
```

- 구분자는 `:` 와 `=` 둘 다 됩니다. UTF-8(BOM 유무 무관), UTF-16, 한국어(CP949), 중국어(GB18030/GBK) 인코딩을 자동으로 인식합니다.
- `곡 제목`은 `제목`, `title`, `name`, `曲名`, `歌名`, `标题`로도 씁니다.
- `아티스트`는 `artist`, `composer`, `작곡`, `艺术家`, `作曲`, `曲师`로도 씁니다.
- 난이도 키는 `cosmic`(`宇宙`/`星空`), `stellar`(`恒星`), `void`(`虚空`)를 모두 인식합니다.
- 제목이 없으면 **BMS 파일 헤더의 `#TITLE`**, 그것도 없으면 **폴더 이름**이 곡 이름으로 표시됩니다.
- 아티스트가 없으면 **BMS 헤더의 `#ARTIST`**, 그것도 없으면 기본값(`화영왕`)이 표시됩니다. 제목과 아티스트는 각각 따로 폴백하므로, `info.txt`에 제목만 적어도 아티스트는 BMS에서 가져옵니다. (`info.txt`가 없어도 BMS만으로 곡이 제대로 표시됩니다. `#SUBTITLE` 등은 쓰지 않습니다.)
- 텍스트 파일(`info.txt`, `config.txt`, `.bms`)은 **16MB**까지만 읽습니다. 상한을 넘으면 읽지 않고 `[Album]` 경고가 남습니다(폴더에 엉뚱한 대용량 파일이 섞여도 시작이 느려지지 않도록 한 안전장치입니다).
- `난이도 :` 줄은 사람이 읽기 위한 것이고, 실제로 반영되는 건 `cosmic` / `stellar` / `void` 각 줄입니다.

난이도 숫자는 **화면 표시만** 바뀝니다. 게임 내부 데이터는 건드리지 않는데,
그렇게 하면 곡 진입 후 로딩이 멈추기 때문입니다(자세한 내용은
[REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md#난이도-표시)).

## savecustomkey/config.txt

키 이름을 잘못 쓰면(예: `AllPerfet`) 그 줄은 무시되고, `MelonLoader/Latest.log`에 `[Config] 알 수 없는 설정 … 의 오타인지 확인하세요` 경고가 남습니다(v1.2.1). 값을 못 읽을 때도 `[Config]` 경고가 남습니다.

- 파일이 없으면 기본값으로 새로 만들어 줍니다.
- 모드가 업데이트되어 설정이 늘어나면, **빠진 항목만 파일 끝에 자동으로 덧붙습니다.** 이미 바꿔 둔 값은 그대로 남습니다.
- `#`으로 시작하는 줄은 주석입니다. 구분자는 `=` 와 `:` 둘 다 됩니다.
- 참/거짓 값은 `true/false`, `on/off`, `yes/no`, `1/0`, `켜기/끄기`, `사용/사용안함`을 모두 받습니다.
- 소수점은 항상 `.`입니다(`0.8`). 지역 설정과 무관합니다.
- **설정은 게임 시작 시 한 번만 읽습니다.** 바꾼 뒤에는 게임을 다시 켜야 합니다.
- 아래 표에 없는 키는 읽지 않습니다(경고도 없음).
- 파일을 읽지 못하거나 값을 해석하지 못하면 그 항목은 아래 기본값으로 동작합니다(로그에 경고가 남습니다).

### 플레이 및 해금

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| `autoplay` | `false` | 오토플레이 강제 사용 여부 (`true` = 자동 연주, 이때 공식 곡 기록은 저장하지 않음) |
| `AllPerfect` | `false` | 올 퍼펙트 판정 모드 (`true` = 키 입력 시 타이밍 오차 없이 무조건 BLUE 최고 판정 및 결과 100% 반영, `false` = 기본 판정 및 판정 훅 미적용) |
| `AllPerfectCustomOnly` | `true` | `AllPerfect=true`일 때 커스텀 곡에서만 적용할지 여부 (`true` = 커스텀 곡 전용, `false` = 공식 곡에도 적용 및 공식 기록 저장 차단) |
| `UnlockAll` | `true` | 전곡 해금 및 볼트 제한 해제 (`true` = 볼트/DLC/어드벤처 잠금 없이 모든 공식 수록곡 즉시 플레이 가능 및 999,999 볼트 표시, `false` = 기본 잠금 유지) |
| `BgmCacheMaxEntries` | `24` | 메모리에 유지할 커스텀 BGM `AudioClip` 최대 개수 (`0` = 무제한, 초과 시 가장 오래 안 쓴 클립부터 LRU 해제) |

### 디버그 로그

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| `VerboseInvocationLogging` | `false` | Harmony 훅 호출 로그 출력 여부 |
| `FocusedHarmonyInvocationLogging` | `true` | `VerboseInvocationLogging` 활성 시 주요 훅만 출력 |
| `RuntimeProbeLogging` | `false` | 런타임 프로브 상세 로그 출력 여부 |
| `TrackSelectorVerboseLogging` | `false` | `TrackSelector.Set` 주입 상세 로그 출력 여부 |

### 노트 연출

판정에는 **전혀 영향이 없습니다.** 그려지는 위치만 바꾸며, 판정·점수·기록은 그대로입니다
(이유는 [REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md#노트-연출-흔들림--속도-카오스) 참고).
두 항목이 모두 꺼져 있으면 노트 훅 자체를 걸지 않으므로 성능 부담도 0입니다.

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| `NoteSway` | `0` | 노트가 눈송이처럼 좌우로 흔들리며 내려오는 연출 |
| `NoteSwayAmplitude` | `20` | 흔들림 폭(픽셀). 너무 크면 레인 밖으로 나가 잘릴 수 있습니다 |
| `NoteSwaySpeed` | `0.8` | 흔들림 속도(초당 왕복 횟수) |
| `NoteSwayDamping` | `1` | 판정선에 가까워지면 흔들림을 잦아들게 함 |
| `NoteSwayDampingTime` | `0.4` | 판정선 도달 몇 초 전부터 잦아들지 |
| `NoteSpeedChaos` | `0` | **[챌린지]** 노트마다 낙하 속도를 제각각으로. 노트끼리 서로 추월하므로 읽기가 매우 어려워집니다 |
| `NoteSpeedChaosMin` / `Max` | `0.6` / `1.8` | 속도 배율 범위(`1` = 원래 속도) |
| `NoteSpeedChaosPerLane` | `1` | `1` = 레인마다 속도가 다름(레인 안에서는 순서 유지). `0` = 노트마다 다름(완전 카오스) |

- 배율이 뒤집혀 있거나(`Min` > `Max`) `0.05` 미만이면 자동으로 바로잡고 로그에 남깁니다.
- 롱노트에 `NoteSpeedChaos`를 걸면 머리 위치만 배율을 타고 꼬리 길이는 원래대로라, 꼬리가 실제 길이를 정확히 나타내지 않습니다.

## 차트 만들기

BMS 파일 작성 규칙과 레인 매핑은 [REFERENCE_BMS_CONVERSION.md](REFERENCE_BMS_CONVERSION.md)를 보세요.

## 잘 안 될 때

`MelonLoader/Latest.log`를 보면 대부분 원인이 나옵니다.

| 로그 | 의미 |
| --- | --- |
| `[Album] 총 N개 앨범을 찾았습니다.` | 폴더를 몇 개 인식했는지 |
| `[Album] <이름>: title=... bms=... music=... jacket=...` | 폴더 안에서 어떤 파일을 골랐는지 |
| `[Album] 건너뜀(차트/음원 없음)` | 그 폴더는 곡으로 인정되지 않음 |
| `[TrackSelector.Set] 커스텀 트랙을 주입했습니다! 적용=N/M` | 목록에 실제로 들어간 개수 |
| `[BmsInject] 완료: ... notesCreated=N ... elapsed=Nms` | 차트가 몇 노트로 변환됐는지, 변환에 걸린 시간(곡 로딩이 느리면 이 값부터 확인) |
| `[CustomJacket] Serving custom jacket for ...` | 자켓이 교체됨 (경로별 처음 1회만 기록) |
| `[CustomBgm] Serving cached ...` | 음원이 교체됨 (경로별 처음 1회만 기록) |
| `[Health] 훅 상태 요약` | 각 기능의 훅이 몇 개 걸렸는지(`정상`/`일부`/`실패`/`꺼짐`) 한눈에 표시 |
| `[Health] GameAssembly.dll / global-metadata.dat 이(가) 지난 실행과 다릅니다` | 게임이 업데이트되었을 때 뜨는 경고 (`savecustomkey/.game-fingerprint` 기준) |
| `[Health] Steam BuildID … 가 고정 빌드 … 와 다릅니다` | Steam 빌드가 모드가 검증된 빌드(`21205351`)와 다름 |
| `[Health] Steam 자동 업데이트가 막혀 있지 않습니다` | `appmanifest_3446490.acf`의 `AutoUpdateBehavior`가 `1`이 아니고 읽기 전용도 아님 — 업데이트가 몰래 들어올 수 있음 |
| `[Health][CRITICAL] 기록 보호(SaveGuard) …` | 저장 차단 훅이 안 걸림 — 커스텀 곡 결과가 공식 Starting Point 기록을 덮어쓸 수 있으니 고치기 전까지 커스텀 곡 플레이 금지 |
