#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

namespace Overburst.DebugTools
{
    public sealed class VisualPlayCase
    {
        public readonly string Id, Title, Observe;
        public VisualPlayCase(string id, string title, string observe) { Id = id; Title = title; Observe = observe; }
    }

    public sealed class VisualPlayCategory
    {
        public readonly string Id, Title;
        public readonly VisualPlayCase[] Cases;
        public VisualPlayCategory(string id, string title, VisualPlayCase[] cases) { Id = id; Title = title; Cases = cases; }
    }

    /// <summary>개발자 시각 확인 목록. 입력·저장 회귀와 도구 자체 검증은 포함하지 않는다.</summary>
    public static class VisualPlayCatalog
    {
        public static readonly VisualPlayCategory[] Categories =
        {
            new VisualPlayCategory("C01", "시작 화면·HUD·성장·메뉴", new[]
            {
                new VisualPlayCase("VT01-01", "하이드아웃 시작 화면", "시작 위치, 플레이어와 기본 HUD를 보고 원소·상태 아이콘의 초기 모습을 확인한다."),
                new VisualPlayCase("VT01-02", "피해 전후 체력 표시", "체력 게이지와 뒤따르는 피해 잔량 표시의 속도·색·가독성을 본다."),
                new VisualPlayCase("VT01-03", "회복 표시", "회복 숫자, 게이지 움직임과 회복 효과가 함께 읽히는지 본다."),
                new VisualPlayCase("VT01-04", "경험치 획득 표시", "경험치 게이지 변화와 획득 표시가 알아보기 쉬운지 본다."),
                new VisualPlayCase("VT01-05", "레벨업 연출", "레벨 표시, 효과와 소리의 크기·시점·겹침을 본다."),
                new VisualPlayCase("VT01-06", "미니맵 기본 표시", "플레이어·대상·방향 표시가 화면에서 잘 구분되는지 본다."),
                new VisualPlayCase("VT01-07", "환경에 가린 플레이어 표시", "가려진 플레이어를 찾을 수 있는지와 표시의 강도를 본다."),
                new VisualPlayCase("VT02-07", "게임 메뉴·정지·재개 화면", "게임 메뉴의 배치·정지한 화면과 닫은 뒤 재개 모습을 본다."),
            }),
            new VisualPlayCategory("C03", "이동·카메라", new[]
            {
                new VisualPlayCase("VT03-01", "걷기와 달리기", "걷기·달리기의 자세, 발 움직임과 전환을 본다."),
                new VisualPlayCase("VT03-02", "출발·정지·방향 전환", "출발, 멈춤과 급회전에서 몸이 자연스럽게 이어지는지 본다."),
                new VisualPlayCase("VT03-03", "대각선 이동", "대각선 이동의 몸 방향과 발 움직임을 본다."),
                new VisualPlayCase("VT03-04", "점프와 착지", "도약·공중·착지 모션과 착지 효과·소리의 시점을 본다."),
                new VisualPlayCase("VT03-05", "경사 이동", "경사에서 발과 지면의 접촉, 몸 자세와 카메라 움직임을 본다."),
                new VisualPlayCase("VT03-06", "계단과 작은 턱", "발이 뜨거나 몸이 튀는지, 계단과 턱을 넘는 모습이 자연스러운지 본다."),
                new VisualPlayCase("VT03-07", "벽과 막힌 길", "벽에 닿을 때 관통·떨림·미끄러짐이 눈에 거슬리는지 본다."),
                new VisualPlayCase("VT03-08", "발판 가장자리와 낙하", "가장자리 이탈·낙하·현재 복귀 모습을 본다. 미구현 복귀는 미지원으로 표시한다."),
                new VisualPlayCase("VT03-09", "지면별 발소리", "연결된 지면의 발소리와 발 디딤 시점, 정지 뒤 소리 종료를 듣는다."),
                new VisualPlayCase("VT03-10", "카메라 줌과 기본 위치", "가까움·멀어짐·기본 줌 복귀의 속도와 화면 구도를 본다."),
                new VisualPlayCase("VT03-11", "이동·대시 중 카메라 추적", "이동과 대시·급회전에서 카메라 지연과 추적이 편안한지 본다."),
                new VisualPlayCase("VT03-12", "카메라 환경 가림", "벽과 소품 뒤에서도 플레이어와 전투가 읽히는지 본다."),
            }),
            new VisualPlayCategory("C04", "월드 상호작용", new[]
            {
                new VisualPlayCase("VT04-01", "상인 접근 안내와 상점 열기", "접근 안내·선택 강조·상점 열림의 위치와 연결을 본다."),
                new VisualPlayCase("VT04-02", "창고 접근 안내와 창고 열기", "접근 안내와 창고창의 열림·닫힘 모습을 본다."),
                new VisualPlayCase("VT04-03", "포탈 접근 안내와 지도창", "포탈 안내와 지도 선택창이 자연스럽게 이어지는지 본다."),
                new VisualPlayCase("VT04-04", "훈련 대상 안내", "훈련 대상의 선택 표시와 현재 제공되는 시험 안내를 본다."),
                new VisualPlayCase("VT04-05", "상호작용 안내 등장과 사라짐", "다가갔다 멀어질 때 안내가 갑자기 튀거나 잔상처럼 남는지 본다."),
                new VisualPlayCase("VT04-06", "여러 대상의 선택 표시", "가까운 대상 사이에서 강조와 안내가 누구를 가리키는지 분명한지 본다."),
            }),
            new VisualPlayCategory("C05", "기본 공격과 동작 연결", new[]
            {
                new VisualPlayCase("VT05-01", "탐험·전투 자세 전환", "무기와 몸의 자세가 모드 전환에서 자연스럽게 이어지는지 본다."),
                new VisualPlayCase("VT05-02", "약공 한 번", "준비·휘두름·타격·회수 모션과 궤적·소리의 시점을 본다."),
                new VisualPlayCase("VT05-03", "약공 콤보", "모든 콤보 단계의 자세·궤적·소리와 단계 사이 연결을 본다."),
                new VisualPlayCase("VT05-04", "약공 뒤 이동 연결", "회수 동작에서 이동으로 이어질 때 몸과 발이 끊기는지 본다."),
                new VisualPlayCase("VT05-05", "무원소 강공", "강공 준비·내려찍기·착지 모션과 효과·소리의 무게감을 본다."),
                new VisualPlayCase("VT05-06", "무기 궤적과 적 피격", "무기와 효과의 보이는 범위가 적의 피격 반응과 어울리는지 본다."),
            }),
            new VisualPlayCategory("C06", "회피·닷지 공격", new[]
            {
                new VisualPlayCase("VT06-01", "탐험 대시", "대시 자세·이동감·종료 연결과 카메라 추적을 본다."),
                new VisualPlayCase("VT06-02", "전투 구르기", "구르는 방향·몸 회전·지면 접촉과 종료 자세를 본다."),
                new VisualPlayCase("VT06-03", "전투 대시", "접근 자세·이동감·카메라 지연과 종료 연결을 본다."),
                new VisualPlayCase("VT06-04", "닷지 뒤 약공", "대시 후반에서 베기 준비·타격·회수로 이어지는 연결을 본다."),
                new VisualPlayCase("VT06-05", "닷지 약공 뒤 콤보", "대시 베기에서 각 일반 콤보로 이어지는 보간과 간격을 본다."),
                new VisualPlayCase("VT06-06", "닷지 뒤 강공", "회피 뒤 강공 준비·착지·원소 방출의 연결을 본다."),
                new VisualPlayCase("VT06-09", "회피 잔상·먼지·소리", "잔상 자세·색·수명, 지면 먼지와 소리가 동작을 방해하지 않는지 본다."),
            }),
            new VisualPlayCategory("C07", "패링·강화 강공", new[]
            {
                new VisualPlayCase("VT07-01", "적 강공 예고", "예고 범위·준비 자세·소리가 공격을 알아차리게 하는지 본다."),
                new VisualPlayCase("VT07-02", "패링 실패 피드백", "실패 시 피격 모션·체력 표시·소리가 상황을 분명하게 전달하는지 본다."),
                new VisualPlayCase("VT07-03", "패링 성공과 기본 자세 복귀", "성공 모션·피드백을 보고 후속 공격 없이 기본 자세로 돌아가는 모습까지 본다."),
                new VisualPlayCase("VT07-04", "패링 기절과 행동 복귀", "적 기절 모션·아이콘·기절 해제 뒤 행동 연결을 본다."),
                new VisualPlayCase("VT07-05", "패링 뒤 강화 강공", "반속 패링에서 두 회전과 내려찍기로 이어지는 모션·효과·소리를 본다."),
                new VisualPlayCase("VT07-06", "일반·강화 강공 비교", "같은 구도에서 두 강공의 모션·궤적·착지 효과·소리를 차례로 비교한다."),
                new VisualPlayCase("VT07-07", "다수 적 패링", "여러 적의 반응·효과가 겹쳐도 패링 성공이 읽히는지 본다."),
            }),
            new VisualPlayCategory("C08", "피격·넉다운·기상", new[]
            {
                new VisualPlayCase("VT08-01", "일반 피격", "타격 순간의 몸 반응·피해 표시·소리와 자세 복귀를 본다."),
                new VisualPlayCase("VT08-02", "정예 강공 넉다운 과정", "실제 강공을 맞아 밀려나고 넘어지는 동안 몸 이동과 모션이 맞는지 본다."),
                new VisualPlayCase("VT08-04", "일반 기상과 방향별 비교", "입력 없음·앞뒤·좌우·대각선에서 일반 기상 자세와 이동 방향을 비교한다."),
                new VisualPlayCase("VT08-06", "Shift 회피 기상", "모드별 구르기 또는 빠른 기상의 모션·이동·종료 연결을 본다."),
                new VisualPlayCase("VT08-08", "벽 가까운 넉다운과 기상", "벽 근처에서 몸 관통·떨림과 기상 후 자세를 본다."),
                new VisualPlayCase("VT08-09", "경사·발판 위 기상", "넘어짐·기상 동안 지면 접촉과 이후 이동 연결을 본다."),
                new VisualPlayCase("VT08-10", "피격 중 사망", "피격에서 사망으로 이어지는 모습과 남는 효과·몸 자세를 본다."),
            }),
            new VisualPlayCategory("C09", "원소 보석·에너지", new[]
            {
                new VisualPlayCase("VT09-01", "보석 장착과 해제 표시", "보석칸·원소 아이콘·무기 효과가 장착과 해제에서 어떻게 바뀌는지 본다."),
                new VisualPlayCase("VT09-02", "원소 교체 비교", "5원소의 아이콘·색·효과와 이전 효과가 사라지는 모습을 본다."),
                new VisualPlayCase("VT09-04", "무기 교체 뒤 원소 외형", "무기를 바꿔 같은 원소의 무기 효과·궤적이 잘 보이는지 본다."),
                new VisualPlayCase("VT09-06", "적중으로 에너지 충전", "실제 약공 적중으로 충전되는 HUD·무기 효과·충전 소리를 본다."),
                new VisualPlayCase("VT09-07", "에너지 단계별 효과와 소리", "0·낮음·중간·최대 에너지에서 무기 효과·휘두름 소리·최대 알림을 비교한다."),
                new VisualPlayCase("VT09-09", "방출 뒤 재충전", "강공 방출 뒤 HUD와 무기 효과 변화, 다음 적중의 충전 표현을 본다."),
                new VisualPlayCase("VT09-10", "보석 툴팁과 품질 표시", "성향·별·옵션·등급색의 배치와 긴 내용의 가독성을 본다."),
                new VisualPlayCase("VT09-11", "정식 무기와 5원소 외형 전수", "정식 무기와 지원되는 5원소 조합의 효과·트레일·몸과의 겹침을 본다."),
            }),
            new VisualPlayCategory("C10", "불", new[]
            {
                new VisualPlayCase("VT10-01", "연소의 시작·지속·종료", "약공으로 연소를 만들고 화염·아이콘·지속 피해 숫자·소리와 종료까지 본다."),
                new VisualPlayCase("VT10-04", "불 강공과 연쇄폭발", "강공 착지에서 상태 변화·연쇄폭발로 이어지는 시점과 크기를 본다."),
                new VisualPlayCase("VT10-05", "다수 대상 불 강공", "폭발이 여러 대상에 퍼질 때 대상과 타격 순간이 구분되는지 본다."),
            }),
            new VisualPlayCategory("C11", "얼음", new[]
            {
                new VisualPlayCase("VT11-01", "냉기 축적과 감속 표현", "냉기 효과·상태 표시·적 이동 변화가 서로 맞게 읽히는지 본다."),
                new VisualPlayCase("VT11-02", "빙결의 발동·유지·해제", "얼어붙는 효과·정지 자세와 해제 뒤 효과·행동 복귀를 본다."),
                new VisualPlayCase("VT11-04", "얼음 강공과 쇄빙", "강공 착지·쇄빙 효과·적 반응·소리의 시점을 본다."),
                new VisualPlayCase("VT11-05", "다수 대상 쇄빙", "여러 대상의 쇄빙과 파급 효과 크기·겹침을 본다."),
                new VisualPlayCase("VT11-06", "빙결 면역 대상의 표현", "면역 대상의 냉기·감속·쇄빙 표현이 일반 대상과 어떻게 다른지 본다."),
            }),
            new VisualPlayCategory("C12", "번개", new[]
            {
                new VisualPlayCase("VT12-01", "감전의 시작과 지속 표현", "약공의 전격·상태 아이콘·피해 숫자·피격 반응과 지속 소리를 본다."),
                new VisualPlayCase("VT12-03", "번개 강공·연쇄·종료", "착지에서 연쇄번개로 이어지는 순서와 대상 사망 뒤 전격 종료를 본다."),
                new VisualPlayCase("VT12-04", "다수 대상 연쇄번개", "대상 사이의 연결선·효과·소리가 겹쳐도 흐름을 따라갈 수 있는지 본다."),
            }),
            new VisualPlayCategory("C13", "어둠", new[]
            {
                new VisualPlayCase("VT13-01", "잠식 축적 표현", "어둠 약공에서 잠식 효과·아이콘·중첩 표현이 읽히는지 본다."),
                new VisualPlayCase("VT13-02", "강공의 잠식 회수 표현", "맞은 대상에서 잠식 회수·상태 변화·후속 효과로 이어지는 모습을 본다."),
                new VisualPlayCase("VT13-03", "탄막 없는 어둠 강공", "에너지나 잠식이 없는 조건의 기본 강공을 탄막 발동 조건과 비교한다."),
                new VisualPlayCase("VT13-04", "탄막 발사와 비행", "발사 위치·묶음·곡선·속도와 카메라에서의 궤적을 본다."),
                new VisualPlayCase("VT13-05", "탄막 적중 효과와 소리", "탄막 명중 효과·적 피격·소리의 크기와 겹침을 본다."),
                new VisualPlayCase("VT13-06", "대상 사망 뒤 탄막 재조준", "대상이 죽을 때 탄의 방향 전환과 종료가 자연스러운지 본다."),
                new VisualPlayCase("VT13-07", "화면 안팎 탄막 표현", "카메라 이동 전후의 화면 가장자리 발사·비행·적중 표현을 본다."),
                new VisualPlayCase("VT13-08", "다수 대상 탄막", "밀집한 대상에서 탄 궤적·명중·소리가 얼마나 구분되는지 본다."),
            }),
            new VisualPlayCategory("C14", "빛", new[]
            {
                new VisualPlayCase("VT14-01", "광휘 축적 표현", "빛 약공의 광휘 효과·상태 표시·색과 밝기를 본다."),
                new VisualPlayCase("VT14-02", "과충전 표현", "과충전 단계의 효과 크기·밝기·HUD와 소리를 본다."),
                new VisualPlayCase("VT14-03", "빛 강공 연타", "강공 연타의 간격·궤적·적 반응·타격음을 본다."),
                new VisualPlayCase("VT14-04", "빛 방출 뒤 상태 변화", "방출 전후 HUD·아이콘·무기 효과의 변화를 본다."),
                new VisualPlayCase("VT14-05", "다수 대상 빛 강공", "밝은 효과가 여러 대상에 겹칠 때 적과 타격이 보이는지 본다."),
                new VisualPlayCase("VT14-06", "교체·사망 뒤 빛 효과 종료", "원소 교체·사망 뒤 빛 효과와 상태 표시가 사라지는 모습을 본다."),
            }),
            new VisualPlayCategory("C15", "전투 표시와 소리", new[]
            {
                new VisualPlayCase("VT15-04", "일반·치명 피해 숫자", "숫자의 크기·색·위치·움직임과 겹침을 비교한다."),
                new VisualPlayCase("VT15-05", "지속·원소 피해 숫자", "여러 종류의 피해가 동시에 표시될 때 구분되는지 본다."),
                new VisualPlayCase("VT15-06", "대상 체력바와 대상 전환", "체력바의 위치·가림·변화와 대상 전환을 본다."),
                new VisualPlayCase("VT15-07", "원소·기절 상태 아이콘", "아이콘의 구분·배치·등장·사라짐과 다중 상태 겹침을 본다."),
                new VisualPlayCase("VT15-08", "혈흔 표현", "혈흔의 위치·양·지면과의 겹침·종료 모습을 본다."),
                new VisualPlayCase("VT15-09", "타격·착지 소리 비교", "타격과 착지 소리의 종류·시점·크기와 반복 피로도를 듣는다."),
                new VisualPlayCase("VT15-10", "히트스톱과 화면 흔들림", "정상 1배속에서 타격 정지·카메라 흔들림의 강도와 회복을 본다."),
                new VisualPlayCase("VT15-11", "다수 효과와 음원 중첩", "효과·피해 숫자·소리가 겹칠 때 전투가 읽히고 소리가 과하지 않은지 본다."),
            }),
            new VisualPlayCategory("C16", "몬스터·분대·군집", new[]
            {
                new VisualPlayCase("VT16-01", "정식 몬스터 외형 전수", "현재 게임에 연결된 개체의 크기·재질·색·무기·몸 겹침을 순환해 본다."),
                new VisualPlayCase("VT16-02", "몬스터 Idle·이동·회전", "개체별 기본 자세·이동·회전과 발 접촉을 본다."),
                new VisualPlayCase("VT16-03", "몬스터 약공 전수", "개체별 준비·타격·회수 모션과 효과·소리를 본다."),
                new VisualPlayCase("VT16-04", "몬스터 강공 전수", "개체별 예고·강공·복귀 모션과 효과·소리를 본다."),
                new VisualPlayCase("VT16-05", "원거리·돌진·광역 행동", "해당 행동을 가진 정식 개체의 발사·이동·범위와 피드백을 본다."),
                new VisualPlayCase("VT16-06", "체급별 피격 표현", "소형·중형·정예·보스의 피격·기절 반응 차이를 본다."),
                new VisualPlayCase("VT16-07", "몬스터 사망 전수", "개체별 사망 모션·효과·소리와 몸·상태 효과가 사라지는 모습을 본다."),
                new VisualPlayCase("VT16-08", "장애물 주변 추격", "벽·장애물·거리 이탈에서 적 이동과 공격 연결이 어색한지 본다."),
                new VisualPlayCase("VT16-09", "분대 추격과 합류", "분대의 추격·합류·밀집 흐름과 개체 겹침을 본다."),
                new VisualPlayCase("VT16-10", "연속 공세 흐름", "현재 시험 서비스의 공세 교대·스폰·이동·종료가 화면에서 읽히는지 본다."),
                new VisualPlayCase("VT16-12", "규모별 혼합 전투", "소수·중간·대규모의 적 밀집, 공격 가독성과 화면 끊김을 비교한다."),
                new VisualPlayCase("VT16-13", "대량 처치와 드롭 화면", "많은 적의 사망·드롭·명찰과 이어지는 전투가 함께 읽히는지 본다."),
            }),
            new VisualPlayCategory("C17", "월드 아이템·외형·획득", new[]
            {
                new VisualPlayCase("VT17-01", "정식 월드 아이템 외형 전수", "정식 카탈로그 모델의 크기·재질·회전·지면 접촉을 순환해 본다."),
                new VisualPlayCase("VT17-02", "아이템 드롭과 착지", "낙하·회전·착지·정지와 지면 접촉을 본다."),
                new VisualPlayCase("VT17-03", "등급 효과와 착지음", "등급별 색·착지 효과·소리의 차이와 크기를 비교한다."),
                new VisualPlayCase("VT17-04", "월드 명찰과 툴팁", "명찰·등급색·툴팁의 위치와 내용 가독성을 본다."),
                new VisualPlayCase("VT17-05", "모델 클릭·키 획득 피드백", "모델 강조·선택과 획득 순간의 효과·명찰 종료·알림을 본다."),
                new VisualPlayCase("VT17-06", "밀집한 명찰 화면", "아이템이 많을 때 명찰의 겹침·정렬과 선택 표시를 본다."),
                new VisualPlayCase("VT17-07", "명찰 없는 모델의 hover", "명찰 표시 범위 밖 모델의 강조·툴팁이 구분되는지 본다."),
                new VisualPlayCase("VT17-08", "카메라 이동 뒤 명찰 갱신", "카메라 이동에 따른 명찰 배치·등장·사라짐이 튀는지 본다."),
                new VisualPlayCase("VT17-09", "획득 불가 안내와 재화 획득 표시", "아이템 획득 불가 안내와 재화 획득 알림의 구분·위치·가독성을 본다."),
                new VisualPlayCase("VT17-10", "무기 장착 외형 전수", "정식 무기의 손 위치·몸 겹침·크기와 현재 자세를 순환해 본다."),
            }),
            new VisualPlayCategory("C18", "장비·인벤토리·가방", new[]
            {
                new VisualPlayCase("VT18-01", "인벤토리·장비창 열기와 닫기", "창의 배치·열림·닫힘과 복귀 화면을 본다."),
                new VisualPlayCase("VT18-02", "장비 장착·교체·해제 표시", "슬롯 아이콘·선택 표시·캐릭터 외형 변화가 자연스럽게 이어지는지 본다."),
                new VisualPlayCase("VT18-03", "장비 비교와 통계 표시", "비교 수치의 색·정렬·읽기 쉬움과 통계창 갱신 모습을 본다."),
                new VisualPlayCase("VT18-04", "캐릭터 미리보기와 다시 열기", "외형·무기·Idle·드래그 회전, 닫고 다시 열기와 씬 복귀 뒤 표시를 본다."),
                new VisualPlayCase("VT18-06", "드래그 이동·교환·취소 표시", "드래그 아이콘·대상칸 강조·교환과 취소 뒤 표시를 본다."),
                new VisualPlayCase("VT18-07", "정렬과 문맥 메뉴", "정렬 후 배치·선택 강조·문맥 메뉴 위치와 화면 가장자리를 본다."),
                new VisualPlayCase("VT18-08", "스택 수량 표시 변화", "이동·합치기·사용에서 수량 글자와 슬롯 표시가 읽히는지 본다."),
                new VisualPlayCase("VT18-09", "퀵슬롯 지정·교체·사용 표시", "아이콘·번호 1~9와 0·사용·빈칸·해제 표시를 차례로 본다."),
                new VisualPlayCase("VT18-11", "열린 칸과 잠긴 칸", "가방 용량별 열린 칸과 잠긴 칸의 색·프레임·구분을 본다."),
                new VisualPlayCase("VT18-12", "가방 교체 미리보기", "드래그 중 열릴 칸·잠길 칸의 강조가 이해하기 쉬운지 본다."),
                new VisualPlayCase("VT18-13", "가방 축소·초과·용량 회복", "작은 가방의 초과 보관·획득 불가 표시와 큰 가방 복귀 뒤 해제를 본다."),
                new VisualPlayCase("VT18-15", "긴 툴팁과 화면 가장자리", "긴 이름·옵션·별·등급 표시, 가장자리 배치와 스크롤 마스크를 본다."),
                new VisualPlayCase("VT18-16", "보석칸과 귀걸이 한 칸", "현재 장착칸의 크기·배치·아이콘과 장착·해제 표시를 본다."),
            }),
            new VisualPlayCategory("C19", "물약·소모품·버프", new[]
            {
                new VisualPlayCase("VT19-01", "장착형 물약 3칸 표시", "세 물약칸의 아이콘·종류·장착·해제 표시를 본다."),
                new VisualPlayCase("VT19-02", "장착형 회복 물약", "사용 모션·회복 효과·소리·체력 표시를 본다."),
                new VisualPlayCase("VT19-03", "장착형 효과 물약", "물약 종류별 사용 효과·버프 아이콘·소리를 본다."),
                new VisualPlayCase("VT19-04", "물약 쿨다운·사용 불가 표시", "쿨다운 숫자·가림·사용 불가 피드백과 사용 가능 복귀 표시를 본다."),
                new VisualPlayCase("VT19-05", "일반 회복 소모품", "회복 효과·소리·체력과 수량 표시 변화를 본다."),
                new VisualPlayCase("VT19-06", "이동 효과 소모품", "이동 변화와 버프 효과·아이콘이 함께 읽히는지 본다."),
                new VisualPlayCase("VT19-07", "버프 중첩·재사용 표시", "중첩 숫자·시간 갱신·효과가 겹칠 때 표시를 본다."),
                new VisualPlayCase("VT19-08", "다수 버프 아이콘", "여러 버프의 배치·구분·툴팁과 화면 가독성을 본다."),
                new VisualPlayCase("VT19-09", "버프 만료 표현", "남은 시간·아이콘·효과가 끝나는 모습을 본다."),
                new VisualPlayCase("VT19-10", "상태 변경 뒤 버프 표시", "사망·장비·씬 변경 뒤 아이콘과 효과의 변화·종료를 본다."),
            }),
            new VisualPlayCategory("C20", "상인·창고·재화", new[]
            {
                new VisualPlayCase("VT20-01", "상인 재고·탭·툴팁", "상인별 재고 슬롯·탭·가격·툴팁의 배치와 가독성을 본다."),
                new VisualPlayCase("VT20-02", "구매 화면 흐름", "구매 제안·확정과 슬롯·골드 표시가 바뀌는 순서를 본다."),
                new VisualPlayCase("VT20-03", "판매 화면 흐름", "판매 제안·확정과 슬롯·골드 표시가 바뀌는 순서를 본다."),
                new VisualPlayCase("VT20-04", "거래 제안 변경과 취소 표시", "제안 수정·취소에서 강조·슬롯·버튼 표시의 변화를 본다."),
                new VisualPlayCase("VT20-05", "거래 실패 안내 비교", "플레이어 골드 부족·상인 골드 부족·공간 부족의 서로 다른 안내를 차례로 본다."),
                new VisualPlayCase("VT20-08", "상인 평판과 할인 표시", "평판·할인·가격 글자의 배치·색·변화가 이해되는지 본다."),
                new VisualPlayCase("VT20-09", "상인 재고 갱신 표시", "갱신·다시 열기 후 재고 목록의 배치와 표시를 본다."),
                new VisualPlayCase("VT20-10", "창고 넣기·꺼내기 표시", "두 창의 슬롯·수량·드래그·대상칸 표시가 이어지는 모습을 본다."),
                new VisualPlayCase("VT20-11", "골드·지도조각 표시", "계정·창고 재화 표시와 획득 알림의 위치·가독성을 본다."),
            }),
            new VisualPlayCategory("C21", "던전 입장·필드", new[]
            {
                new VisualPlayCase("VT21-01", "지도 선택·취소·입장 화면", "레벨·등급·옵션·테마·입장 정보, 취소와 무료·지도 입장의 화면 흐름을 본다."),
                new VisualPlayCase("VT21-05", "입장 실패 안내와 복귀 화면", "제공되는 실패 안내와 지도창·게임 화면 복귀를 본다."),
                new VisualPlayCase("VT21-06", "로딩 화면", "등록된 로딩 그림·문구·진행 표시와 화면 전환을 본다."),
                new VisualPlayCase("VT21-07", "던전 입구·카메라·미니맵", "입구 구도·플레이어·카메라·미니맵과 귀환 후 다음 런의 시작 화면을 본다."),
                new VisualPlayCase("VT21-08", "던전 필드 전투와 드롭", "현재 필드의 무리·전투·사망·런 드롭이 배경에서 구분되는지 본다."),
                new VisualPlayCase("VT21-09", "던전 지형·경계·낙하", "실제 가장자리·낙하와 현재 제공되는 복귀 화면을 본다."),
                new VisualPlayCase("VT21-10", "지도 조건별 표시와 조우", "선택한 레벨·등급·테마의 표시와 실제 조우 화면을 비교한다."),
            }),
            new VisualPlayCategory("C22", "이벤트·카드·전송", new[]
            {
                new VisualPlayCase("VT22-01", "토벌 이벤트 시작·진행·완료", "시작 안내·진행 표시·완료 피드백과 보상 선택으로 이어지는 모습을 본다."),
                new VisualPlayCase("VT22-02", "수호 이벤트 시작·진행·완료", "수호 대상·남은 시간·방어 상황과 성공 뒤 보상 화면을 본다."),
                new VisualPlayCase("VT22-04", "이벤트 실패 표현", "실패 안내·효과와 필드로 돌아오는 모습을 본다."),
                new VisualPlayCase("VT22-05", "카드 공개와 선택", "앞뒷면·등급·hover·선택 강조·확정 연출을 본다."),
                new VisualPlayCase("VT22-06", "버프 카드 표시", "종류별 카드 그림·문구·선택 후 효과·아이콘을 본다."),
                new VisualPlayCase("VT22-07", "상자 카드와 드롭", "상자 생성·열기·아이템 드롭·명찰의 연결을 본다."),
                new VisualPlayCase("VT22-08", "경험치 카드의 획득 표현", "카드 선택에서 경험치·레벨업 표시로 이어지는 연출을 본다."),
                new VisualPlayCase("VT22-09", "전송 카드와 자연 전송 오브젝트", "두 경로의 생성 위치·외형·상호작용 안내를 비교한다."),
                new VisualPlayCase("VT22-10", "전송 선택·취소·확정 화면", "대상 선택·보호 표시·취소·확정과 사용 뒤 오브젝트 표시를 본다."),
            }),
            new VisualPlayCategory("C23", "보스·귀환·실패 화면", new[]
            {
                new VisualPlayCase("VT23-01", "현재 보스 행동과 HUD", "게임에 연결된 보스 또는 대역의 모션·예고·효과와 보스 HUD를 본다."),
                new VisualPlayCase("VT23-02", "보스 처치와 보상 연출", "사망·지도 보상·포탈 등장과 소리의 연결을 본다."),
                new VisualPlayCase("VT23-03", "포탈 성공 귀환 화면", "귀환 안내·전환·보상 표시와 하이드아웃의 HUD·창·프롬프트를 본다."),
                new VisualPlayCase("VT23-04", "시간 만료 안내와 귀환", "마지막 카운트다운·시간 만료 안내·귀환 화면을 본다."),
                new VisualPlayCase("VT23-05", "사망 안내와 귀환 화면", "사망·실패 안내·전환과 복귀 뒤 HUD·창·프롬프트를 본다."),
                new VisualPlayCase("VT23-06", "중도 이탈 안내와 복귀", "이탈 확인·실패 안내·정산 표시와 하이드아웃 복귀를 본다."),
            }),
        };
        public static readonly VisualPlayCase[] All = Categories.SelectMany(category => category.Cases).ToArray();
        public static VisualPlayCase Find(string id) => Array.Find(All, item => item.Id == id);
        public static VisualPlayCategory CategoryOf(string id) => Array.Find(Categories, category => Array.Exists(category.Cases, item => item.Id == id));
    }
}
#endif
