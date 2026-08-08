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
| 차트 | 첫 번째 `*.bms` |
| 음원 | `music.ogg` → 첫 번째 `*.ogg` |
| 프리뷰 | `music_preview.ogg` → 없으면 음원과 동일 |
| 자켓 | `jacket` / `cover` / `thumbnail` / `자켓` / `커버` / `썸네일` (`.png` `.jpg` `.jpeg`) → 없으면 폴더 안 첫 번째 이미지 |
| 정보 | `info.txt` → 첫 번째 `*.txt` |

음원 폴백이 `.ogg`로만 한정된 이유가 있습니다. 폴더에는 BMS 키음(`*.wav`)이 함께 들어있는데,
그걸 곡 음원으로 잘못 집으면 엉뚱한 소리가 재생되기 때문입니다. **곡 음원은 반드시 `.ogg`로 넣으세요.**

차트나 음원 중 하나도 없는 폴더는 곡으로 인정하지 않고 건너뜁니다(로그에 이유가 남습니다).

## info.txt

```
곡 제목 : どりーむもーど
아티스트 : 화영왕
난이도 : cosmic, stellar, void
cosmic = 8
stellar = 8
void = 8
```

- 구분자는 `:` 와 `=` 둘 다 됩니다.
- `곡 제목`은 `제목`, `title`로도 씁니다. `아티스트`는 `artist`로도 씁니다.
- 제목이 없으면 **폴더 이름**이 곡 이름으로 표시됩니다.
- `난이도 :` 줄은 사람이 읽기 위한 것이고, 실제로 반영되는 건 `cosmic` / `stellar` / `void` 각 줄입니다.

난이도 숫자는 **화면 표시만** 바뀝니다. 게임 내부 데이터는 건드리지 않는데,
그렇게 하면 곡 진입 후 로딩이 멈추기 때문입니다(자세한 내용은
[REFERENCE_CUSTOM_TRACK_PIPELINE.md](REFERENCE_CUSTOM_TRACK_PIPELINE.md#난이도-표시)).

## savecustomkey/config.txt

```
# STARGAZER Custom Chart 설정
# 값을 바꾼 뒤 게임을 다시 시작하면 적용됩니다.

# 오토플레이 강제 사용 여부 (true = 자동 연주, false = 직접 플레이)
autoplay = false
```

- 파일이 없으면 기본값으로 새로 만들어 줍니다.
- `#`으로 시작하는 줄은 주석입니다.
- 참/거짓 값은 `true/false`, `on/off`, `yes/no`, `1/0`, `켜기/끄기`를 모두 받습니다.
- **설정은 게임 시작 시 한 번만 읽습니다.** 바꾼 뒤에는 게임을 다시 켜야 합니다.

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
| `[BmsInject] 완료: ... notesCreated=N` | 차트가 몇 노트로 변환됐는지 |
| `[CustomJacket] Serving custom jacket for ...` | 자켓이 교체됨 |
| `[CustomBgm] Serving cached ...` | 음원이 교체됨 |
