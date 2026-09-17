[System.Serializable]
public sealed class ComboGemRolledOption // 신규 콤보 보석 롤 결과
{
    public ComboGemRandomOptionType optionType;
    public float value;

    public ComboGemRolledOption(ComboGemRandomOptionType type, float rolledValue)
    {
        optionType = type;
        value = rolledValue;
    }
}
