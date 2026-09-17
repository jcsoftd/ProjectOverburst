public readonly struct ItemUseResult
{
    private ItemUseResult(bool success, string message, bool showPlayerStatusText)
    {
        Success = success;
        Message = message;
        ShowPlayerStatusText = showPlayerStatusText;
    }

    public bool Success { get; }
    public string Message { get; }
    public bool ShowPlayerStatusText { get; }

    public static ItemUseResult Ok(string message)
    {
        return new ItemUseResult(true, message, false);
    }

    public static ItemUseResult Fail(string message, bool showPlayerStatusText = false)
    {
        return new ItemUseResult(false, message, showPlayerStatusText);
    }
}
