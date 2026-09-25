/// <summary>
/// The authored Korean labels for the playable theme enemies. IDs and supplier asset
/// names remain stable; builders use this mapping when they regenerate definitions.
/// </summary>
public static class EnemyDisplayNames
{
    public const int AuthoredCount = 29;

    public static bool TryGet(string enemyId, out string displayName)
    {
        switch (enemyId)
        {
            case "SpiderBrood_RostrokarckLarvae": displayName = "갈퀴턱 유충"; return true;
            case "SpiderBrood_Horridomorph": displayName = "흉포 변이충"; return true;
            case "SpiderBrood_Scolokarck_Tint3": displayName = "분사갑충"; return true;
            case "SpiderBrood_Carcinoptera": displayName = "도약갑충"; return true;
            case "SpiderBrood_Rostrokarck": displayName = "갈퀴턱 우두머리"; return true;
            case "VenomBrood_Venodonte_Tint1": displayName = "독니충"; return true;
            case "VenomBrood_Venodonte_Tint3": displayName = "산성 독니충"; return true;
            case "VenomBrood_Arathrox": displayName = "산성 사수"; return true;
            case "VenomBrood_Kupolojuve_Tint_Orange": displayName = "전격 가시충"; return true;
            case "VenomBrood_Kupolobrach_Tint_Orange": displayName = "독낭 거수"; return true;
            case "PrimalHunt_Caniathrox": displayName = "송곳니 사냥개"; return true;
            case "PrimalHunt_CrustaspikanLarvae": displayName = "가시갑 유체"; return true;
            case "PrimalHunt_Dimaxillosaurus": displayName = "쌍발톱 포식룡"; return true;
            case "PrimalHunt_Venosaur_Tint_Brown": displayName = "독숨 포식룡"; return true;
            case "PrimalHunt_Occisodonte": displayName = "거대 송곳니"; return true;
            case "CavernMutants_Ceratoferox": displayName = "뿔발톱 야수"; return true;
            case "CavernMutants_Cephalonops": displayName = "암굴 추격수"; return true;
            case "CavernMutants_Gasterobrach": displayName = "암굴 강타수"; return true;
            case "CavernMutants_Limadon": displayName = "촉수 사수"; return true;
            case "CavernMutants_Gorhorrid": displayName = "긴혀 포식수"; return true;
            case "CavernMutants_Ursacetus": displayName = "암굴 거수"; return true;
            case "DeathHarvest_RakeSkulker": displayName = "창백한 숲갈퀴"; return true;
            case "DeathHarvest_RakeStalker": displayName = "뒤틀린 숲갈퀴"; return true;
            case "DeathHarvest_BoneAsh": displayName = "잿빛 해골"; return true;
            case "DeathHarvest_BoneMoss": displayName = "이끼 해골"; return true;
            case "DeathHarvest_RakeBrute": displayName = "거목 갈퀴"; return true;
            case "DeathHarvest_BoneWarden": displayName = "묘지 파수 해골"; return true;
            case "DeathHarvest_DeathKnight": displayName = "사령 기사"; return true;
            case "DeathHarvest_Reaper": displayName = "영혼 수확자"; return true;
            default: displayName = null; return false;
        }
    }

    public static string Resolve(string enemyId, string fallback)
    {
        return TryGet(enemyId, out string displayName) ? displayName : fallback;
    }
}
