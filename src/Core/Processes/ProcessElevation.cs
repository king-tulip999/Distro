using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Core.Processes;

internal static class ProcessElevation
{
    private const uint QueryLimitedInformation = 0x1000, TokenQuery = 0x0008;
    private const int TokenElevation = 20, AccessDenied = 5;

    public static bool AnyElevated(string processName)
    {
        Process[] found;
        try { found = Process.GetProcessesByName(processName); }
        catch (InvalidOperationException) { return false; }
        bool elevated = false;
        foreach (var process in found)
            using (process)
                if (!elevated && Elevated(process.Id)) elevated = true;
        return elevated;
    }

    private static bool Elevated(int id)
    {
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, id);
        if (handle == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(handle, TokenQuery, out IntPtr token)) return Marshal.GetLastWin32Error() == AccessDenied;
            try { return GetTokenInformation(token, TokenElevation, out int value, sizeof(int), out _) && value != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
