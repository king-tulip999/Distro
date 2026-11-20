using System.Runtime.InteropServices;
using System.Text;

namespace Core.Processes;

internal static class ProcessImage
{
    public static string? PathOf(int pid)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var text = new StringBuilder(1024);
            int size = text.Capacity;
            return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString(0, size) : null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref int size);
}
