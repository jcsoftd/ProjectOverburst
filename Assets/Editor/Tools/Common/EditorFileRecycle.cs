using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEngine;

public static class EditorFileRecycle
{
    public static bool MoveAsset(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("Assets/Editor/", StringComparison.Ordinal)
            && !normalized.StartsWith("Assets/ProjectOverburst/", StringComparison.Ordinal))
            throw new ArgumentException("프로젝트 소유 자산만 휴지통으로 이동할 수 있습니다: " + path);
        string full = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, normalized));
        if (!Within(full, Path.Combine(Application.dataPath, "Editor")) && !Within(full, Path.Combine(Application.dataPath, "ProjectOverburst")))
            throw new ArgumentException("정규화한 자산 경로가 소유 범위 밖입니다: " + path);
        if (!File.Exists(full) && !Directory.Exists(full)) return false;
        if (!AssetDatabase.MoveAssetToTrash(path)) throw new IOException("자산 휴지통 이동 실패. 원본을 보존합니다: " + path);
        if (File.Exists(full) || Directory.Exists(full) || File.Exists(full + ".meta"))
            throw new IOException("휴지통 이동 뒤 원래 자산/meta가 남아 있습니다: " + path);
        return true;
    }

    public static void MovePath(string path, bool recursive = false)
    {
        string project = Directory.GetParent(Application.dataPath).FullName;
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));
        if (!File.Exists(full) && !Directory.Exists(full)) return;
        string artifacts = Path.GetFullPath(Path.Combine(project, "../개인파일/코덱스산출"));
        string temporary = Path.Combine(project, "Temp/Codex");
        if (!Within(full, artifacts) && !Within(full, temporary)) throw new ArgumentException("검증 산출물 범위 밖입니다: " + full);
        if (Directory.Exists(full) && !recursive && Directory.EnumerateFileSystemEntries(full).Any())
            throw new IOException("비어 있지 않은 폴더의 제거 범위를 확인하세요: " + full);
        for (string current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("링크를 통과하는 휴지통 이동은 허용하지 않습니다: " + current);
        if (Application.platform != RuntimePlatform.WindowsEditor) throw new PlatformNotSupportedException("이 파일 경로의 휴지통 이동을 지원하지 않습니다.");
        Exception failure = null;
        // IFileOperation은 STA에서만 실행한다. Unity 객체는 이 작업 스레드에서 접근하지 않는다.
        var thread = new Thread(() =>
        {
            object operation = null; IShellItem item = null;
            try
            {
                operation = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                var recycle = (IFileOperation)operation;
                const uint recycleOnDelete = 0x00080000, allowUndo = 0x0040, noConfirmation = 0x0010,
                    silent = 0x0004, noErrorUi = 0x0400, earlyFailure = 0x00100000;
                recycle.SetOperationFlags(recycleOnDelete | allowUndo | noConfirmation | silent | noErrorUi | earlyFailure);
                Guid iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out item));
                recycle.DeleteItem(item, IntPtr.Zero);
                recycle.PerformOperations(); recycle.GetAnyOperationsAborted(out bool aborted);
                if (aborted) throw new IOException("휴지통 이동이 중단되었습니다: " + full);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { if (item != null) Marshal.FinalReleaseComObject(item); }
                catch (Exception error) { if (failure == null) failure = error; }
                try { if (operation != null) Marshal.FinalReleaseComObject(operation); }
                catch (Exception error) { if (failure == null) failure = error; }
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new IOException("휴지통 이동 실패. 직접 삭제로 전환하지 않습니다: " + full, failure);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("휴지통 이동 뒤 원래 경로가 남아 있습니다: " + full);
    }

    static bool Within(string child, string parent) => child.StartsWith(Path.GetFullPath(parent).TrimEnd('/', '\\')
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr window);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
