# OVERBURST

Unity 기반 쿼터뷰 액션 프로젝트다. 직접 조작 전투, 몬스터 군집전, 절차 생성 던전과 보스전을 개발한다.

## 개발 환경

- Unity `6000.3.15f1`
- 시작 씬: `Assets/ProjectOverburst/00_Scenes/PersistentScene.unity`
- 메인 거점: Hideout
- 던전: Hideout 포탈에서 DunGen 기반 DungeonRun으로 진입하고 다시 Hideout으로 복귀

현재 프로젝트에는 Input System 기반 입력·상태 조정, CharacterController 이동 모터, Cinemachine 쿼터뷰 카메라, 통합 상호작용, 표면별 발소리와 전투 피드백 기반이 연결되어 있다.

## 로컬 전용 의존성

저장소에는 재생성 캐시, 내부 기획 문서, 개인 작업 파일, `Assets/ThirdParty`와 재배포할 수 없는 Asset Store 패키지를 포함하지 않는다. 프로젝트를 여는 개발자는 자신이 보유한 라이선스 사본을 원래 경로에 복원해야 한다.

Hera Agent Unity와 lilToon처럼 저장소에 포함된 패키지는 각 패키지의 라이선스를 따른다.

## 기본 조작

- `WASD`: 이동
- `Tab`: 인벤토리
- `X`: 전투 모드 전환
- 마우스 왼쪽: 기본 공격
- 근접 전투 모드에서 `Shift`: 회피

무기 슬롯은 한 개이며 시작 시 비어 있다. Hideout에서 획득한 무기를 장착해 시험할 수 있다.