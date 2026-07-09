# Cast and Wrapper Code Reference

이 문서는 저장소 안의 IL2CPP 캐스트 및 래퍼 관련 코드가 어떤 문제를 풀기 위해 존재하는지 설명합니다.

요약하면, 이 프로젝트의 캐스트 코드는 일반적인 C# 업캐스트/다운캐스트만을 의미하지 않습니다. Unity IL2CPP 런타임에서 같은 네이티브 객체를 가리키는 관리 래퍼(managed wrapper)를 다시 만들거나, IL2CPP delegate 래퍼의 `Invoke` 메서드를 리플렉션으로 호출하기 위한 호환 계층입니다.

## 배경

STARGAZER의 게임 타입은 대부분 `Il2Cpp...` 네임스페이스 아래의 런타임 타입입니다. 이런 객체는 C# 코드에서 관리 객체처럼 보이지만, 내부적으로는 IL2CPP 네이티브 객체를 가리키는 핸들(`Pointer`, `m_CachedPtr`, `IntPtr`)을 가지고 있습니다.

그래서 다음과 같은 상황이 자주 발생합니다.

- 런타임에서 받은 객체의 C# 래퍼 타입이 컬렉션이 요구하는 타입과 다릅니다.
- 실제로는 같은 IL2CPP 객체인데, 현재 래퍼에서는 특정 필드나 프로퍼티가 보이지 않습니다.
- 게임 코드가 넘긴 콜백이 일반 `Action<T>`처럼 보이지만, 실제 타입은 IL2CPP delegate 래퍼입니다.
- public API만으로는 트랙 메타데이터나 콜백 시그니처에 안정적으로 접근하기 어렵습니다.

이 때문에 코드에서는 직접 참조 대신 리플렉션, `TryCast<T>`, `IntPtr` 생성자, `Invoke` 메서드 탐색을 조합합니다.

## 주요 파일

| 파일 | 역할 |
| :--- | :--- |
| `src/Hooks/Support/TrackSelector/TrackSelectorReflectionSupport.cs` | IL2CPP 타입 검색, `Pointer` 기반 concrete wrapper 생성, 메타데이터 탐색 |
| `src/Hooks/Support/TrackSelector/TrackSelectorCollectionSupport.cs` | 컬렉션 `Insert` 대상 타입 확인 및 삽입 전 캐스트 처리 |
| `src/Hooks/Support/TrackSelector/TrackSelectorCloningSupport.cs` | 트랙/메타데이터 복제, IL2CPP identity 검증, 래퍼 생성 후 writable 멤버 복사 |
| `src/Hooks/Support/TrackSelector/TrackSelectorMetadataSupport.cs` | 트랙 ID 및 내부 메타데이터 접근, 덤프 로그 출력 |
| `src/Hooks/Support/Sound/CustomBgmSupport.cs` | `AudioClip` 포인터 설명, IL2CPP 콜백 래퍼의 `Invoke` 호출 |
| `src/Hooks/Support/TrackLoader/TrackLoaderHookSupport.cs` | onLoaded 콜백의 `Invoke` 메서드를 찾아 Harmony postfix 패치 |
| `src/Hooks/Support/Common/ReflectionSupport.cs` | 프로퍼티/필드/getter 순서로 멤버 값을 읽는 공통 helper |

## 캐스트 흐름

### `CastToConcreteTrackData`

위치: `TrackSelectorReflectionSupport.cs`

`CastToConcreteTrackData(object track, Type targetType)`는 트랙 객체에서 `Pointer` 또는 `m_CachedPtr`을 읽고, 대상 타입에 `IntPtr` 생성자가 있으면 그 포인터로 새 래퍼를 만듭니다.

중요한 점은 새 게임 객체를 생성하는 것이 아니라는 것입니다. 같은 IL2CPP 네이티브 객체를 가리키는 다른 C# 래퍼를 만드는 쪽에 가깝습니다.

처리 순서:

1. 원본 객체에서 `Pointer` 또는 `m_CachedPtr` 값을 찾습니다.
2. 값이 없거나 `IntPtr.Zero`이면 실패로 보고 `null`을 반환합니다.
3. 대상 타입에서 `Constructor(IntPtr)`를 찾습니다.
4. 생성자가 있으면 `ctor.Invoke(new object[] { ptr })`로 래퍼를 만듭니다.
5. 예외가 나면 verbose logging이 켜진 경우 경고 로그를 남기고 `null`을 반환합니다.

이 함수는 트랙 목록 덤프, 메타데이터 수정, 트랙 복제 전 concrete type 확보에 사용됩니다.

### `CastToType`

위치: `TrackSelectorCollectionSupport.cs`

`CastToType(object obj, Type targetType)`는 컬렉션에 값을 삽입하기 전에 컬렉션이 요구하는 원소 타입으로 객체를 맞추는 helper입니다.

처리 순서:

1. 이미 `targetType.IsAssignableFrom(obj.GetType())`이면 원본을 그대로 반환합니다.
2. 객체 타입에 generic `TryCast<T>()` 메서드가 있으면 `MakeGenericMethod(targetType)` 후 호출합니다.
3. `TryCast<T>()`가 실패하면 `Pointer` 또는 `m_CachedPtr`을 읽고, 대상 타입의 `IntPtr` 생성자로 래퍼를 만듭니다.
4. 모든 변환이 실패하면 현재 구현은 `null`이 아니라 원본 객체를 반환합니다.

마지막 fallback이 원본 반환인 이유는 일부 IL2CPP 컬렉션/래퍼가 런타임 binder에서 자체적으로 허용되는 경우가 있기 때문입니다. 다만 삽입 실패는 `TryInsertAtStart`의 예외 처리에서 경고 로그로 확인해야 합니다.

## 래퍼 생성과 복제

### `CloneTrackData`

위치: `TrackSelectorCloningSupport.cs`

`CloneTrackData`는 기존 트랙을 바탕으로 독립된 메타데이터를 가진 새 트랙 래퍼를 만들기 위한 흐름입니다.

처리 흐름:

1. 원본 트랙을 `CastToConcreteTrackData`로 concrete wrapper에 맞춥니다.
2. `_metaData`, `metaData`, `m_metaData`, `MetaData` 후보에서 원본 메타데이터를 찾습니다.
3. `CloneMetaData`로 메타데이터 객체를 복제합니다.
4. `CreateTrackDataFromMetaData`로 새 메타데이터를 받는 `INNER_TrackData` 생성자를 호출합니다.
5. 메타데이터 관련 멤버를 제외하고 원본 트랙의 writable 필드/프로퍼티를 새 트랙에 복사합니다.
6. 새 트랙이 원본 메타데이터가 아니라 새 메타데이터를 가리키는지 검증합니다.

### `CloneIl2CppObject`

`CloneIl2CppObject`는 `InstantiateIl2CppObject(type)`로 새 IL2CPP 래퍼 객체를 만들고, `CopyFieldsAndProperties`로 쓰기 가능한 멤버를 복사합니다.

복사에서 제외되는 대표 멤버:

- `Pointer`
- `m_CachedPtr`
- `pooledPtr`
- `isWrapped`
- `ObjectClass`

이 값들은 IL2CPP 객체의 내부 identity와 수명 관리에 관여합니다. 원본에서 새 객체로 그대로 복사하면 두 래퍼가 같은 네이티브 객체처럼 보이거나, 잘못된 수명 상태를 갖게 될 수 있습니다.

### `HaveSameIl2CppIdentity`

`HaveSameIl2CppIdentity(left, right)`는 두 객체가 같은 IL2CPP identity인지 확인합니다.

우선 `Pointer` 또는 `m_CachedPtr`을 비교하고, 포인터를 읽을 수 없으면 `ReferenceEquals`로 fallback합니다. 이 비교는 복제 검증에서 중요합니다. 새 메타데이터가 원본과 같은 포인터를 공유하면 독립 복제가 아니기 때문입니다.

## delegate 래퍼와 `Invoke`

### `InvokeActionOfAudioClip`

위치: `CustomBgmSupport.cs`

커스텀 BGM 로더는 게임의 `LoadBGMClip` 또는 `LoadPreviewClip`에 전달된 콜백을 보관했다가, 로컬 `AudioClip` 로딩이 끝나면 호출합니다.

콜백 객체가 컴파일 타임의 `Action<AudioClip>` 타입으로 직접 보장되지 않기 때문에 다음처럼 처리합니다.

1. 콜백 객체의 런타임 타입에서 public instance `Invoke` 메서드를 찾습니다.
2. 찾으면 `invoke.Invoke(callback, new object?[] { clip })`로 호출합니다.
3. 없거나 실패하면 경고 로그를 남깁니다.

이 방식은 IL2CPP delegate 래퍼를 직접 generic delegate로 캐스팅하지 않아도 되게 해 줍니다.

### `TryPatchTrackLoaderOnLoadedCallback`

위치: `TrackLoaderHookSupport.cs`

트랙 로더의 onLoaded 콜백도 같은 원리로 처리합니다. 콜백 객체에서 `Invoke` 메서드를 찾고, 그 메서드 자체에 Harmony postfix를 붙입니다.

중복 패치를 막기 위해 `BuildMethodPatchKey(invokeMethod)` 결과를 `TrackLoaderCallbackPatchedMethods`에 저장합니다.

## 멤버 접근 helper

### `TryGetMemberValue`

위치: `ReflectionSupport.cs`

IL2CPP 래퍼 타입은 프로퍼티, 필드, getter 메서드 노출 방식이 타입마다 다를 수 있습니다. `TryGetMemberValue`는 이 차이를 흡수하기 위해 다음 순서로 값을 찾습니다.

1. 같은 이름의 readable property
2. 같은 이름의 field
3. `get_{memberName}` 형태의 getter method

각 단계는 실패해도 예외를 밖으로 던지지 않고 다음 후보를 시도합니다. 런타임 분석과 patch 코드에서 게임 버전 차이를 견디기 위한 방어적 접근입니다.

## 수정 시 주의사항

- `Pointer`, `m_CachedPtr`, `pooledPtr`, `isWrapped`, `ObjectClass`는 일반 데이터 필드처럼 복사하지 마세요.
- `IntPtr` 생성자 호출은 네이티브 훅이 아니라 기존 IL2CPP 객체를 가리키는 managed wrapper 재구성입니다.
- `CastToType`의 최종 fallback은 현재 원본 객체 반환입니다. 이 동작을 `null` 반환으로 바꾸면 `TryInsertAtStart` 흐름과 로그 의미가 달라집니다.
- `TryCast<T>()` 탐색은 메서드 이름과 generic 정의 여부에 의존합니다. 오버로드가 늘어나는 경우 파라미터 수와 generic 조건을 같이 확인해야 합니다.
- delegate 래퍼를 호출할 때는 `Invoke` 시그니처가 바뀔 수 있으므로, 호출 전 인자 개수와 타입을 로그로 확인하는 것이 안전합니다.
- 복제 후에는 `HaveSameIl2CppIdentity`로 원본과 새 객체가 의도대로 분리되었는지 확인하세요.

## 관련 문서

- `docs/NATIVE_HOOKS_CLARIFICATION.md` - `Pointer`/`IntPtr` 사용이 네이티브 훅을 의미하지 않는다는 설명
- `docs/TRACK_METADATA_REFERENCE.md` - 트랙 메타데이터 덤프와 주요 멤버 설명
