using System;
using System.Globalization;
using System.IO;
using Overburst.Appearance;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

// Existing area verifiers predate the title gate. Boot only their own isolated
// account through the real title, including subsequent Play cycles.
[InitializeOnLoad]
public static class UIPrefabPlayBoot
{
    const string Key="Overburst.UIPrefabPlayBoot.";
    static UIPrefabPlayBoot(){if(!string.IsNullOrEmpty(SessionState.GetString(Key+"account","")))EditorApplication.update+=Tick;}
    public static void Arm(string accountDirectory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))throw new InvalidOperationException("Returned account required.");
        if(!string.IsNullOrEmpty(SessionState.GetString(Key+"account","")))throw new InvalidOperationException("Previous UI boot must be stopped.");
        string account=IsolatedSavePlayGuard.ValidateDirectory(accountDirectory);
        SessionState.SetString(Key+"account",account);SessionState.SetString(Key+"deadline",DateTime.UtcNow.AddMinutes(15).ToString("O",CultureInfo.InvariantCulture));
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    public static void Seed()
    {
        string account=SessionState.GetString(Key+"account","");if(string.IsNullOrEmpty(account))throw new InvalidOperationException("Owned boot required.");
        if(File.Exists(Path.Combine(account,"account-A.es3"))||File.Exists(Path.Combine(account,"account-B.es3")))throw new InvalidOperationException("Fresh account only.");
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var catalog=Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);
        var state=AccountCharacterCommands.First(Guid.NewGuid().ToString("N"),"UI검증기사","knight",catalog.defaultAppearance,registry);
        new EasySaveAccountStore(account).Save(state,state.lastTransactionId);
    }
    public static void Stop()
    {
        SessionState.EraseString(Key+"account");SessionState.EraseString(Key+"deadline");EditorApplication.update-=Tick;
    }
    static void Tick()
    {
        string account=SessionState.GetString(Key+"account","");if(string.IsNullOrEmpty(account)){Stop();return;}
        if(DateTime.UtcNow>DateTime.Parse(SessionState.GetString(Key+"deadline",""),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)){Stop();return;}
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||!EditorApplication.isPlaying||!string.Equals(IsolatedSavePlayGuard.ActiveDirectory,account,StringComparison.OrdinalIgnoreCase))return;
        var title=TitleCharacterPanel.Instance;if(!title||title.NameDialogOpen||title.quitDialog.activeSelf||OverburstGameMenu.IsOpen)return;
        try
        {
            if(title.Screen=="title")title.start.onClick.Invoke();
            if(title.Screen=="characters"&&title.enter.interactable)title.enter.onClick.Invoke();
        }
        catch(Exception error){File.WriteAllText(Path.Combine(account,"ui-boot-failure.txt"),error.ToString());Stop();}
    }
}
