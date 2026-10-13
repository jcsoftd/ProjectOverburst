using System;
using System.IO;

[Serializable]
public sealed class TutorialProgress
{
    public int version = 1;
    public bool offered;
    public string firstCharacterId;
    public int completed;
    public int skipped;
    public int lastLesson;

    public static TutorialProgress Load(string directory)
    {
        var settings = Settings(directory);
        if (!File.Exists(settings.FullPath)) return new TutorialProgress();
        var value = ES3.Load<TutorialProgress>("tutorial", settings);
        int mask = (1 << TutorialLessons.Count) - 1;
        if (value == null || value.version != 1 || (value.completed & ~mask) != 0 || (value.skipped & ~mask) != 0
            || value.lastLesson < 0 || value.lastLesson >= TutorialLessons.Count)
            throw new InvalidDataException("튜토리얼 기록을 읽지 못했습니다. 기존 파일은 유지됩니다.");
        return value;
    }
    public static void RecordFirstCharacter(string directory, string characterId)
    {
        var progress = Load(directory);
        if (progress.offered || !string.IsNullOrEmpty(progress.firstCharacterId)) return;
        progress.firstCharacterId = characterId;
        progress.Save(directory);
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        ES3.Save("tutorial", this, Settings(directory));
    }
    private static ES3Settings Settings(string directory) => new ES3Settings(Path.Combine(directory, "tutorial.es3"))
    { location = ES3.Location.File, encryptionType = ES3.EncryptionType.None, compressionType = ES3.CompressionType.None };
}
