namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private void BuildStatFields()
        {
            Heading("공격 속도와 공격 간격");
            Field("variant", "attackSpeedMultiplier", "공격속도 (배율)");
            Note("1은 기준 속도입니다. 0.8은 느리게, 1.2는 빠르게 휘두릅니다. 준비·타격·회수와 패링 가능 시간이 모션 진행에 함께 맞춰집니다.");
            Field("variant", "attackInterval", "공격간격 (초)");
            Note("공격 모션과 회수가 끝난 뒤 다음 공격까지 기다리는 시간입니다. 공격속도를 바꿔도 이 대기는 변하지 않습니다. 보스의 패턴 간격은 보스메이커에서 조절합니다.");
            Heading("기본 능력치 배율");
            Field("variant", "healthMultiplier", "체력 배율");
            Field("variant", "damageMultiplier", "피해 배율");
            Field("variant", "moveSpeedMultiplier", "이동속도 배율");
            Note("이 몬스터의 값만 저장합니다. 공유 프로필은 저장할 때 이 몬스터용으로 분리합니다.");
        }
    }
}
