using UnityEditor;

internal static class ProjectVtpEditorMenuGuard
{
    public static bool ConfirmDangerousAction(string title, string detail)
    {
        return EditorUtility.DisplayDialog(
            "[위험] ProjectVTP Editor Utility",
            detail + "\n\n현재 열려 있는 씬의 저장 상태와 백업 여부를 먼저 확인하세요.",
            "실행",
            "취소");
    }
}
