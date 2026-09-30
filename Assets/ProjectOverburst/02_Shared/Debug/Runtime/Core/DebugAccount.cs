#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 저장 계정 구분. 격리 계정은 저장 폴더로 나뉜다(<c>OVERBURST_SAVE_DIRECTORY</c>가 있으면 그 폴더, 없으면 실제 계정).
    /// 90C 12절 1번 확인 결과.
    /// </summary>
    public static class DebugAccount
    {
        public const string SaveDirectoryVariable = "OVERBURST_SAVE_DIRECTORY";
        public const string RealAccountWarning = "실제 계정에 저장돼요. 계속할까요?";

        public static bool IsIsolated
            => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SaveDirectoryVariable));

        public static string Label => IsIsolated ? "격리 계정" : "실제 계정";

        /// <summary>저장 폴더 이름만(경로 전체는 보여 주지 않는다).</summary>
        public static string FolderName
        {
            get
            {
                string directory = Overburst.Persistence.AccountBootstrap.SaveDirectory;
                if (string.IsNullOrEmpty(directory))
                    return "계정 준비 전";
                string trimmed = directory.TrimEnd('/', '\\');
                int slash = Mathf.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
                return slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            }
        }
    }
}
#endif
