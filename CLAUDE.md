# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 현황

1인 개발 중인 턴제 헥스 전략 + 횡스크롤 비행 슈팅 게임(에어리어88 모티브). Unity 6(`6000.3.20f1`, URP) 프로젝트다. 저장소는 아직 **코드 작성 전 단계**로, URP 템플릿(`Assets/Scenes/SampleScene`, `Assets/TutorialInfo`)과 패키지만 들어 있다. 설계의 기준은 `DevDocuments/`의 문서이며, 구현 전에 해당 절을 먼저 읽는다.

- `횡스크롤_전략_비행슈팅_기획서.md` — 게임 기획 (규칙, 경제, 유닛, 출격 구조)
- `횡스크롤_전략_비행슈팅_기술설계.md` — 기술 설계 (어셈블리, 데이터 파이프라인, 세이브, 렌더링, 빌드). 아래의 절 번호(§)는 이 문서 기준
- `횡스크롤_전략_비행슈팅_전투프로토타입_검증계획.md` — 첫 마일스톤: 전투 조작감 프로토타입

**프로토타입과 본 코드의 구분**: 전투 프로토타입은 명시적으로 *버리는 코드*다. 어셈블리·DI·데이터 파이프라인 구조를 따르지 않고, 기본 도형만 쓰며, 검증계획의 질문 Q1~Q9에 답하는 것이 목적이다. 프로토타입에 본 아키텍처를 강요하지 말고, 프로토타입 코드가 `Assets/_Game/Code/`로 흘러들지 않게 한다.

## 명령어

빌드 스크립트나 테스트는 아직 없다. Unity 에디터 경로는 `C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe`. 테스트(Unity Test Framework, `Assets/_Game/Code/Tests/`)가 생기면 저장소 루트에서 헤드리스로 실행한다 (에디터에서 프로젝트가 열려 있으면 안 됨).

```
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile Logs/test.log
```

단일 테스트는 `-testFilter <정규화된 테스트 이름|정규식>`을 추가한다.

`Game.Core` 규칙 테스트는 Unity 밖 .NET 프로젝트(`DotNet/`, 기술설계 §8.5)에 두고 `dotnet test DotNet/Game.Core.Tests`로 실행한다 (에디터를 연 채로 가능). 이 csproj는 `Assets/_Game/Code/Core`·`Contracts` 소스를 링크로 포함하므로 소스는 한 벌이다. Unity EditMode 테스트는 Data 임포터·검증기처럼 Unity가 필요한 것에만 쓴다.

## 본 아키텍처 (기술설계 §2)

어셈블리 = `Assets/_Game/Code/` 아래 폴더이며, 경계는 asmdef 참조로 강제된다.

| 어셈블리 | 의존 | 비고 |
|---|---|---|
| `Game.Core` | 없음 (`UnityEngine` 미참조) | 순수 C# 도메인: 월드, 헥스 맵, 경제, 턴 처리, 자동 정산, 적 AI. MessagePack(NuGet 코어, 세이브 포매터 소스 생성)만 허용 — **R3, UniTask, MessagePipe, VContainer 금지**. C# 9 기준 (.NET 프로젝트와 공용 컴파일) |
| `Game.Contracts` | 없음 (MessagePack만 허용 — 세이브에 들어가는 타입 때문) | 공유 타입: `SortieContext`/`SortieResult`, ID, 불변 정의 레코드, 도메인 사건, 메시지 구조체 |
| `Game.Data` | Contracts | SO 저작, xlsx 임포터(에디터), 정의 레코드 변환 |
| `Game.Strategy` | Core, Contracts | 전략 맵 뷰·UI |
| `Game.Combat` | **Contracts만** | 전투 씬. Core를 절대 참조하지 않음 |
| `Game.Infra` | Core, Contracts | 세이브/로드, Addressables, 입력·플랫폼, 씬 전환 |
| `Game.App` | 전부 | 부트스트랩, 조립(composition root), 턴-전투 오케스트레이션 |

핵심 불변 규칙:
- **출격 계약 (§5)**: Core 턴 처리기가 `SortieContext`를 만들고, Combat(직접 플레이) 또는 Core 자동 정산 리졸버가 같은 형태의 `SortieResult`를 돌려준다. Combat은 사실만 보고한다. 손상·포로·보상·잔해 회수·전략 효과 계산은 모두 Core에서 하므로 직접/자동 간 규칙 차이가 생길 수 없다.
- **턴 처리기는 동기 상태 머신**이다 (Core에 async 없음). 씬 로드·대기는 App 레이어가 UniTask로 조율한다. 상태 머신의 현재 위치가 데이터이므로 어느 시점에서든 저장 가능해야 한다.
- **결정론 (§2.6)**: 모든 무작위성은 Core의 시드 난수를 거친다. 같은 세이브 + 같은 커맨드 = 같은 결과. 게임 로직에서 `UnityEngine.Random`/`System.Random`을 임의로 쓰지 않는다.
- **상태와 규칙 분리 (§3.3)**: 월드 상태는 로직 없는 데이터 클래스로 직접 직렬화한다 (세이브 DTO 없음). 상태 클래스에 `ReactiveProperty`·이벤트를 두지 않는다. 규칙은 무상태 서비스.
- **세이브 (§3.4)**: MessagePack 하나, `[MessagePackObject]` + 문자열 `[Key("...")]`. JSON은 개발용 덤프(`ConvertToJson`)일 뿐 로드 경로가 아니다. 플레이어 설정(§3.5)은 세이브와 분리된 JSON 파일.
- **UI (§2.8)**: UI Toolkit 하나 (uGUI 미사용). 개발 전용 튜닝 오버레이만 IMGUI 허용.
- **씬**: `Boot`(상주, 루트 `LifetimeScope`) + `Strategy`/`Combat`을 추가 로드해 VContainer 자식 스코프로 구성. Combat 스코프는 `SortieContext`를 파라미터로 주입받는다. Core 코드는 생성자 주입만 쓰고 VContainer를 참조하지 않는다.

### R3 vs MessagePipe (§2.4)
- **값**(체력, 선택 타일, 자금) → R3 `ReactiveProperty`
- **받는 쪽이 출처를 아는 사건** → 해당 대상의 R3 Observable에 직접 구독
- **출처를 모르는 중앙 수신자용 사건**(이펙트·사운드 매니저, 전투 결과 기록기) → MessagePipe `IPublisher<T>`/`ISubscriber<T>`, 브로커는 루트 스코프에 등록
- 이 구분은 Core 바깥의 규칙이다. Core는 R3도 MessagePipe도 쓰지 않고, 커맨드가 **결과 + 도메인 사건 목록**을 돌려준다. Strategy 프레젠터가 사건을 받아 자기 `ReactiveProperty`를 갱신하고, 출처를 모르는 수신자용 사건은 App이 MessagePipe로 중계한다.
- 메시지는 Contracts에 구조체로 정의한다. 새 메시지 타입을 만들기 전에 받는 쪽이 정말 출처를 모르는지 확인한다.

### 전투 (§6)
Entities를 쓰지 않는 가벼운 DOTS 하이브리드. 플레이어·보스는 GameObject, 탄은 NativeArray 구조체 + Burst Job으로 이동·충돌하고 인스턴싱으로 그린다. 잡몹은 풀링된 GameObject이고 `TransformAccessArray` Job으로 이동한다. 피격 판정에 물리 엔진을 쓰지 않는다. 매 프레임 히트박스를 NativeArray에 복사해 판정하고, Job 완료 후 메인 스레드에서 결과를 발행한다. 적은 청크 프리팹에 굽지 않는다 (웨이브는 xlsx 데이터 + 스플라인 ID).

전투 프로토타입에서 나온 규칙 (§6.6~6.8):
- 적·지상 목표·적 탄·폭탄·지형은 **지면 좌표**, 플레이어와 기관포 탄만 화면 좌표. 폭탄은 기체 대지 속도를 계승하되 0 이상.
- 스크롤 속도는 부호가 있다 (선회 = 방향 반전). "앞 = +x"를 가정하지 않는다.
- 지형은 청크별 바닥·천장 **높이 곡선**이 원본이고, 판정·메시·지형 예고가 같은 곡선을 읽는다. 고정형 보스 본체도 이 곡선에 합친다. 피할 수 없는 경사는 검증기가 막는다.

## 데이터·콘텐츠 규칙

- 모든 연결은 문자열 ID(`AC_F16`, `FAC_HANGAR`)로 한다. xlsx 행과 SO가 같은 ID로 짝을 이룬다. Core와 시트는 ID만 알고 에셋은 모른다. 로컬라이제이션 키는 ID에서 파생(`AC_F16.name`).
- xlsx 원본은 저장소의 `SourceAssets/Tables/`(LFS)에 둔다. `.blend`·레이어 텍스처 같은 제작 원본은 **저장소 밖** 클라우드 동기화 폴더에 두고 Git에 넣지 않는다. `Assets`에는 내보낸 결과(FBX·PNG·오디오)만 둔다.
- 출시 기준 (기술설계 1장): Steam Deck 처음부터 목표(60fps, 1280×800에서 UI가 읽혀야 함, 패드만으로 진행 가능), 언어는 영어+한국어(원문 한국어), 기체 화면 이름은 가상 명칭(내부 ID는 참고 실기체 기준, 예 `AC_F16`), 외부 구매 에셋은 핵심 모델(기체·보스·적) 외에만.
- `Assets/_Game/Data/Generated/`는 임포터 산출물이므로 직접 수정하지 않는다.
- `Assets/_Game/` 최상위는 7개 폴더로 고정(Code, Content, Data, Rendering, Audio, Scenes, Settings). 여러 파일로 이뤄진 콘텐츠는 ID 폴더에 모은다 (예: `Content/Aircraft/AC_F16/AC_F16.{fbx,prefab}`). 하드포인트 소켓은 `HP_` 접두사 Empty.
- 커스텀 렌더 패스는 Unity 6 URP 렌더 그래프 API 기준.

## 네이밍 (기술설계 §2.10)

- **콘텐츠 ID**: `접두사_본문`, `^[A-Z]{2,5}_[A-Z0-9_]+$`. 접두사 `AC_ WPN_ PRT_ FAC_ BASE_ PLT_ CHK_ SPL_ EVT_ EN_ BOSS_ RIV_ FCT_ GND_ RSH_ ENV_ WAVE_ UI_`, 지형 코드 `PLN MTN SEA CST DST`. 정한 ID는 바꾸지 않는다. 로컬라이제이션 키 `<ID>.<소문자 필드>`, Addressables 주소 = ID.
- **에셋**: ID 콘텐츠는 파일명 = ID (변형은 `_역할`), 공용 에셋은 PascalCase. `M_`/`T_` 같은 타입 접두사 금지. FBX 내부: 루트 = ID, `HP_` 하드포인트, `MZ_` 총구, `FX_` 이펙트 지점, `PT_` 보스 부위.
- **xlsx**: 시트 = 정의 레코드 이름에서 `Def` 뺀 것, 열 = 필드 이름(PascalCase), 첫 열 `Id`, `#` 머리글 열은 메모.
- **C#**: 파일당 public 타입 하나, 네임스페이스 = 어셈블리 + 첫 하위 폴더. `_camelCase` private 필드, `private` 생략, 기본 `sealed`, `XxxAsync`. 역할 이름: `XxxDef`/`XxxDefId`(문자열) 정의, `XxxId`(정수) 인스턴스, Core 상태는 접미사 없는 명사, `XxxRules` 무상태 서비스, `XxxCommand`, 도메인 사건은 과거형 무접미사, `XxxMessage` MessagePipe 구조체, `XxxAsset` SO, `XxxPresenter`/`XxxPanel`/`XxxView`, `XxxLifetimeScope`, `XxxJob`. 테스트는 `<대상>Tests` + `Subject_Behavior`.
- **세이브 호환**: 상태에 들어가는 열거형은 값을 명시하고 뒤에만 추가. 문자열 `[Key]`는 필드 이름이 바뀌어도 유지.
- **UI·입력**: USS 클래스 kebab-case + BEM, 토큰은 USS 변수(`--color-accent`), UXML `name`은 PascalCase. 입력 액션 맵·액션은 PascalCase.
- **Git**: `feature/<영문-kebab>` 브랜치, 커밋 메시지는 한국어 `대상: 내용`.

## 패키지

git URL 패키지는 `Packages/manifest.json`에, R3·MessagePack·ExcelDataReader는 NuGetForUnity로 설치한 NuGet 패키지다 (`Assets/Packages/`, `Assets/packages.config` — 커밋 대상). R3.Unity·MessagePack.Unity는 NuGet 코어가 먼저 설치되어 있어야 컴파일된다. MessagePipe.VContainer는 MessagePipe 코어를 자동으로 끌어오지 않으므로 둘 다 같은 태그로 넣어야 한다. ExcelDataReader는 Editor 전용으로 제한한다. 버전은 기술설계 §8.4와 맞춘다 (참고: 현재 manifest는 아직 v0.5 미반영 — MessagePipe `#1.8.2` 태그 누락, Burst·Collections·Mathematics·Newtonsoft 명시 필요, Visual Scripting·Multiplayer Center·Collab Proxy·AI Navigation 제거 예정).
