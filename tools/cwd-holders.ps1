# 读出所有进程的当前工作目录，列出工作目录落在某个前缀下的进程。
#
# 为什么需要它：Windows 不允许改名一个被任何进程当作工作目录的目录（含子目录），
# 而 DSH 的安装目录是 E:\dsh —— 只要有任何常驻进程的 cwd 落在 E:\dsh 下，
# DSH 自更新就必然失败。改名探测只能间接推断，这里直接读 PEB 里的 CurrentDirectory。
#
# 用法：pwsh -File tools/cwd-holders.ps1 [-Prefix 'E:\dsh']
param([string]$Prefix = 'E:\dsh')

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ProcessCwd
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr handle, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private const int QueryInformation = 0x0400;
    private const int VmRead = 0x0010;

    // PEB->ProcessParameters 在 x64 上是 +0x20；RTL_USER_PROCESS_PARAMETERS.CurrentDirectory
    // （UNICODE_STRING：Length, MaximumLength, Buffer）在 +0x38。
    public static string Of(int processId)
    {
        var handle = OpenProcess(QueryInformation | VmRead, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
            {
                return null;
            }

            var pointer = new byte[8];
            if (!ReadProcessMemory(handle, (IntPtr)((long)info.PebBaseAddress + 0x20), pointer, 8, out _))
            {
                return null;
            }

            var parameters = (IntPtr)BitConverter.ToInt64(pointer, 0);
            var unicode = new byte[16];
            if (!ReadProcessMemory(handle, (IntPtr)((long)parameters + 0x38), unicode, 16, out _))
            {
                return null;
            }

            var length = BitConverter.ToUInt16(unicode, 0);
            var buffer = (IntPtr)BitConverter.ToInt64(unicode, 8);
            if (length == 0 || buffer == IntPtr.Zero)
            {
                return string.Empty;
            }

            var text = new byte[length];
            if (!ReadProcessMemory(handle, buffer, text, length, out _))
            {
                return null;
            }

            return System.Text.Encoding.Unicode.GetString(text);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
'@

$rows = foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    $cwd = $null
    try { $cwd = [ProcessCwd]::Of($process.Id) } catch { $cwd = $null }
    if ($cwd) {
        [pscustomobject]@{ Id = $process.Id; Name = $process.ProcessName; Cwd = $cwd; Exe = $process.Path }
    }
}

Write-Host ("读到了 {0} 个进程的工作目录" -f @($rows).Count)
$bad = $rows | Where-Object { $_.Cwd -like "$Prefix*" }
Write-Host ''
if ($bad) {
    Write-Host ("⚠ 工作目录落在 {0} 下的进程（会挡住 DSH 自更新）:" -f $Prefix)
    $bad | Select-Object Id, Name, Cwd | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    exit 1
}

Write-Host ("✓ 没有任何进程的工作目录落在 {0} 下" -f $Prefix)
Write-Host ''
Write-Host '—— 顺便看看这几个进程的 cwd ——'
$rows | Where-Object { $_.Name -in @('CodexQuotaChip', 'DeepSeek Harness', 'dotnet', 'VBCSCompiler') } |
    Select-Object Id, Name, Cwd | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
exit 0
