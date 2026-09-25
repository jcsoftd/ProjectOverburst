public static class ShopTabDisplayPolicy
{
    public static string GetHeaderTitle(ShopTab tab)
    {
        if (tab == ShopTab.Trade)
            return "상점 거래";
        if (tab == ShopTab.Quest)
            return "상점 퀘스트";

        return "상점 " + GetDisplayName(tab);
    }

    public static string GetInputBlockedMessage(ShopTab tab)
    {
        return GetDisplayName(tab) + " 탭에서는 거래 입력을 사용하지 않습니다.";
    }

    public static string GetDisplayName(ShopTab tab)
    {
        switch (tab)
        {
            case ShopTab.Trade:
                return "거래";
            case ShopTab.Quest:
                return "퀘스트";


            case ShopTab.WeaponCombine:
                return "무기조합";
            case ShopTab.WeaponEnhance:
                return "강화";
            default:
                return "준비중";
        }
    }

    public static string GetSpecialtyDescription(ShopTab tab)
    {
        switch (tab)
        {


            case ShopTab.WeaponCombine:
                return "무기 재료를 조합해 새 무기를 만드는 기능을 연결할 자리입니다.";
            case ShopTab.WeaponEnhance:
                return "무기 강화와 성장 기능을 연결할 자리입니다.";
            default:
                return "상인 전용 기능을 연결할 준비 영역입니다.";
        }
    }

    public static string GetSpecialtyFutureText(ShopTab tab)
    {
        switch (tab)
        {


            case ShopTab.WeaponCombine:
            case ShopTab.WeaponEnhance:
                return "무기상인 전용 기능으로 구현 예정";
            default:
                return "상인별 전용 기능으로 구현 예정";
        }
    }
}
