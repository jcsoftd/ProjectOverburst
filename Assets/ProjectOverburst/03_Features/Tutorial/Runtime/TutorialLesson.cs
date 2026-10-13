public enum TutorialLesson
{
    Move, Charge, Discharge, Dash, Roll, DashLight, DashHeavy,
    IncompleteParry, NormalParry, PerfectParry, Combat, Equipment, Potion, World
}

public static class TutorialLessons
{
    public const int Count = 14;
    public static readonly string[] Names = {
        "이동과 전투 모드", "약공으로 힘 모으기", "강공으로 방출하기", "앞으로 대시", "뒤로 구르기",
        "대시 공격", "대시 강공격", "불완전 패링", "일반 패링", "완벽 패링", "짧은 실전", "장비와 원소보석", "물약과 퀵슬롯", "사냥과 지도" };
    public static string Description(TutorialLesson lesson)
    {
        switch (lesson)
        {
            case TutorialLesson.Move: return "표식까지 이동한 뒤 전투 모드를 켜보세요.\n마우스로 공격할 방향을 바라봅니다.";
            case TutorialLesson.Charge: return "약공을 맞히면 원소 게이지가 찹니다.\n표적을 공격해 30% 이상 모아보세요.";
            case TutorialLesson.Discharge: return "모은 에너지를 강공으로 방출하세요.\n많이 모을수록 강해지고, 비어 있으면 원소 방출이 없습니다.";
            case TutorialLesson.Dash: return "표적을 바라보며 앞쪽으로 대시해보세요.\n전투 중 앞·옆은 대시, 뒤는 구르기입니다.";
            case TutorialLesson.Roll: return "표적을 바라보며 뒤쪽으로 굴러보세요.\n구르기는 대시보다 무적 구간이 길어 이탈에 좋습니다.";
            case TutorialLesson.DashLight: return "대시로 이동하는 중에 약공을 누르세요.\n끝난 뒤 누르면 일반 공격입니다.";
            case TutorialLesson.DashHeavy: return "대시로 이동하는 중에 강공을 누르세요.\n접근하면서 모은 에너지를 방출합니다.";
            case TutorialLesson.IncompleteParry: return "적의 강공 예고에 맞춰 받아치세요.\n게이지 30% 미만: 피해 20%가 남고 약공으로 반격합니다.";
            case TutorialLesson.NormalParry: return "같은 공격을 다시 받아쳐보세요.\n30% 이상~80% 미만: 피해 없이 강화 반격합니다.";
            case TutorialLesson.PerfectParry: return "같은 공격을 다시 받아쳐보세요.\n80% 이상: 강화 반격에 더해 일반 적을 기절시킵니다.";
            case TutorialLesson.Combat: return "약공으로 모으고 강공으로 방출하세요.\n위험할 때는 구르거나 패링해보세요.";
            case TutorialLesson.Equipment: return "바닥의 훈련 방어구를 주워 장착하세요.\n장비는 능력치를, 원소보석은 원소 전투를 바꿉니다.";
            case TutorialLesson.Potion: return "회복약을 퀵슬롯에 등록하고 한 번 사용하세요.\n싸우면서 가방을 열지 않고 회복할 수 있습니다.";
            default: return "의뢰 → 야외 사냥 → 지도 던전 → 귀환 · 입장 시 지도 소비\n사망 시 지도 런의 미반출품은 잃고, 야외 전리품은 유지됩니다.";
        }
    }
    public static bool IsParry(TutorialLesson lesson) => lesson >= TutorialLesson.IncompleteParry && lesson <= TutorialLesson.PerfectParry;
    public static ParryGrade Grade(TutorialLesson lesson) => (ParryGrade)((int)lesson - (int)TutorialLesson.IncompleteParry);
    public static float PreparedEnergy(TutorialLesson lesson) => lesson == TutorialLesson.IncompleteParry ? .20f
        : lesson == TutorialLesson.NormalParry ? .50f : lesson == TutorialLesson.PerfectParry || lesson == TutorialLesson.DashHeavy ? .85f : 0f;
}
