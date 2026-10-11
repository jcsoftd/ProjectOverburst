# OVERBURST

Unity 6와 C#으로 개발 중인 쿼터뷰 액션 RPG다. 대검으로 원소 에너지를 쌓고 강공으로 방출하며, 몬스터 군집전과 보스 공방을 중심으로 제작한다.

최신 개발 소스는 [`JC` 브랜치](https://github.com/jcsoftd/ProjectOverburst/tree/JC)에 있다. 아래 기능과 실행 안내는 이 브랜치를 기준으로 한다.

## 플레이 흐름

마을에서 장비와 원소보석을 준비하고 상인의 의뢰를 받는다. 지도 아이템을 사용해 사막이나 동굴에 진입하고, 전투에서 얻은 아이템을 정상 귀환으로 반출한다. 지도에는 레벨·등급·지형·크기·몬스터 테마·옵션을 저장해 입장할 때 같은 생성 정보를 사용한다.

## 주요 기능

### 전투와 원소

- 대검 약공 4타, 강공, 회피 후 공격과 대시 강공을 지원한다.
- 무기에 장착한 원소보석이 불·얼음·번개·빛·어둠 중 하나를 공급한다. 약공 적중으로 에너지를 충전하고 강공으로 방출한다.
- 불완전·일반·완벽의 3단계 패링을 사용한다. 불완전 패링은 약공 반격, 일반·완벽 패링은 강화 반격으로 이어지며 성공부터 반격 종료까지 무적을 유지한다.
- 몬스터는 체급과 공격 패턴에 따라 무리로 교전한다. 보스 Crustaspikan은 2페이즈 전투와 등장 연출, 패링·그로기 반응을 갖춘다.

### 월드와 지도 던전

- 메인 마을 `MainScene`에 무기·방어구·잡화·물약 상인, 치장사와 보관함을 연결했다. `HideoutScene`은 전투 테스트 거점으로 유지한다.
- 동굴은 4~20개 플랫폼을 실시간으로 조립한다. 지도별 고정 몬스터 명단의 95%를 처치하면 완료되며, 플레이어가 있는 플랫폼에 귀환 포탈을 만든다.
- 사막은 선별된 지형 후보를 사용한다. 보스방 진입 시 Crustaspikan이 등장하고, 보스를 처치하면 완료된다.
- 완료 보상으로 지도 1장을 지급한다. 완료 후에는 남은 적을 더 사냥하거나 원하는 때 귀환할 수 있으며, 귀환 전 사망·포기 시 미반출 아이템을 잃는다.
- 동굴 원형 미니맵은 탐색한 바닥과 다리를 공개하고 방문 기록을 유지한다.

### 성장과 마을 서비스

- 장비·원소보석·물약·가방의 옵션을 전투 능력치와 아이템 설명에 반영한다. 공용 퀵슬롯은 10칸이다.
- 상인의 의뢰 수락·진행·보고, 보상 지급, 우호도에 따른 구매 할인을 지원한다. 개인 퀘스트 목록과 HUD에서 목표를 확인한다.
- 캐릭터 외모 편집과 저장, 공통 스킬트리의 능력치 배분과 적용을 지원한다. 스킬트리의 추가 공격 효과와 액티브 스킬은 후속 개발 대상이다.
- 계정 저장은 A/B 파일을 사용하며 캐릭터별 장비·외모·퀘스트와 공유 보관함·상인 데이터를 관리한다.

## 개발 환경

| 항목 | 구성 |
|---|---|
| 엔진 | Unity `6000.3.15f1` |
| 언어 | C# |
| 렌더링 | Universal Render Pipeline `17.3.0` |
| 입력 | Input System `1.19.0` |
| 카메라 | Cinemachine `3.1.5` 쿼터뷰 |
| 이동·길 찾기 | CharacterController, AI Navigation `2.0.12` |
| UI | uGUI·TextMeshPro, Editor 제작 도구의 UI Toolkit |
| 시작 씬 | `Assets/ProjectOverburst/00_Scenes/PersistentScene.unity` |

## 기본 조작

| 입력 | 동작 |
|---|---|
| `WASD` / 방향키 | 이동 |
| 마우스 왼쪽 | 약공 |
| 마우스 오른쪽 | 강공·패링 |
| `Shift` | 전투 중 회피 |
| `X` | 전투 모드 전환 |
| `F` | NPC·포탈·아이템 상호작용 |
| `Tab` | 인벤토리 |
| `C` | 장비 창 |
| `J` | 개인 퀘스트 목록 |
| `1`~`9`, `0` | 퀵슬롯 1~10 |
| `Esc` | 창 닫기·취소 |

## 코드 구성

프로젝트 소유 코드는 `Assets/ProjectOverburst` 아래에 기능별로 나눈다.

| 경로 | 역할 |
|---|---|
| `00_Scenes` | 상주 씬, 마을·던전과 UI 관리 씬 |
| `01_Core` | 입력, 씬 전환, 계정 저장과 거래 |
| `02_Shared` | 공용 전투 판정, 원소·피드백, UI |
| `03_Features` | 플레이어, 무기, 몬스터·보스, 아이템, 월드·지도 던전 |
| `05_Art` / `Resources` | 프로젝트 아트와 런타임 로드 자산 |
| `Assets/Editor` | 제작 도구, 자산 빌더와 기능별 검증기 |

전투는 `PlayerInputFacade`에서 입력을 받아 `MeleeRuntime`과 공격 실행기로 전달한다. 계정 변경은 거래 처리 후 장비·진행 데이터와 UI에 반영한다. 지도 런은 입장 준비, 고정 몬스터 명단과 증원, 완료 보상과 귀환을 나눠 처리한다.

## 제작 도구

Unity 메뉴의 `JC Tool > Tool Hub`에서 제작 도구를 연다.

- **ComboMaker**: 무기의 공격 모션, 타격 구간과 VFX를 편집한다.
- **MonsterTuner**: 몬스터별 능력치, 공격·모션과 상태 효과를 편집한다.
- **BossMaker**: 보스 공격 재료, 복합 패턴과 모션을 편집한다.
- **월드·오디오·밸런스 도구**: 사막 생성, 효과음 카탈로그와 공통 수치를 조정한다.

자산 생성·연결 코드는 `Assets/Editor/Builders`, 검증기는 `Assets/Editor/Testers`에서 관리한다.

## 프로젝트 열기와 외부 의존성

1. 최신 개발 브랜치를 내려받는다.

   ```bash
   git clone --branch JC https://github.com/jcsoftd/ProjectOverburst.git
   ```

2. Unity Hub에서 저장소 폴더를 Unity `6000.3.15f1`로 연다. Package Manager 의존성은 `Packages/manifest.json`에 정의되어 있다.
3. 라이선스를 보유한 외부 패키지와 해당 패키지에서 파생한 씬·프리팹·아트를 원래 경로에 복원한다.
4. `PersistentScene.unity`를 열고 실행한다.

저장소에는 `Assets/ThirdParty`와 재배포할 수 없는 Asset Store 원본·파생 자산을 포함하지 않는다. 메인 마을 씬과 일부 UI·환경·전투 효과도 복원이 필요하므로, 저장소를 내려받는 것만으로 전체 게임을 실행할 수는 없다. 주요 외부 의존성에는 DunGen, Easy Save, Feel, RPG and MMO UI 11과 환경·모델·VFX 패키지가 있다.

상단 대상·보스 HUD의 네임플레이트 텍스처는 RPG and MMO UI 11 원본에서 만든 로컬 자산이다. 해당 패키지를 복원한 뒤 `Tools/Project VTP/UI/상단 대상·보스 HUD 다시 만들기 (RPG UI 11)`로 텍스처와 프리팹 연결을 다시 만들 수 있다.

저장소에 포함된 Hera Agent Unity와 lilToon은 각 패키지의 라이선스를 따른다. Noto Serif KR 및 파생 글꼴의 라이선스는 [OFL_NotoSerifKR.txt](https://github.com/jcsoftd/ProjectOverburst/blob/JC/Assets/ProjectOverburst/05_Art/Fonts/OFL_NotoSerifKR.txt), Spoqa Han Sans Neo는 [글꼴 고지](https://github.com/jcsoftd/ProjectOverburst/blob/JC/Assets/ProjectOverburst/Resources/UI/Fonts/ProjectMT/NOTICE_ThirdPartyFonts.md)에서 확인할 수 있다.

내부 기획 문서, 개인 작업 기록, 검증 로그와 재생성 캐시는 공개 Git에서 제외한다.

## 후속 개발

- 사막 탐색 지도, 반투명 펼쳐보기와 목표·포탈 표시
- 초반 의뢰와 첫 장비·원소보석·지도 획득을 연결하는 온보딩
- 스킬트리의 추가 효과·액티브 스킬과 성장 밸런스
- 마법 연사 무기
