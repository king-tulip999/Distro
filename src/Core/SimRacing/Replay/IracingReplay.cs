using System.Runtime.InteropServices;
using Core.Processes;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Replay;

internal static class IracingReplay
{
    public const int CamSwitchNum = 1, SetPlaySpeed = 3, Search = 5, SearchSessionTime = 12;
    public const int SearchToEnd = 1;

    public static (nint WParam, nint LParam) Pack(int message, int first, int second) =>
        ((nint)(uint)((message & 0xFFFF) | ((first & 0xFFFF) << 16)), second);

    public static (nint WParam, nint LParam) Pack(int message, int first, int second, int third) =>
        ((nint)(uint)((message & 0xFFFF) | ((first & 0xFFFF) << 16)), (nint)(uint)((second & 0xFFFF) | ((third & 0xFFFF) << 16)));

    public static int NumberCode(string number)
    {
        string digits = number.Trim();
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit) || digits.Length > 3) return -1;
        int value = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        int zeros = digits.Length - digits.TrimStart('0').Length;
        if (value == 0) zeros = digits.Length - 1;
        if (zeros == 0) return value;
        int places = value > 99 ? 3 : value > 9 ? 2 : 1;
        return value + 1000 * (places + zeros);
    }

    public static int TimeMs(double seconds) => (int)Math.Round(Math.Clamp(seconds, 0, int.MaxValue / 1000.0) * 1000);

    public static bool JumpTo(int session, double seconds) =>
        Send(Pack(SearchSessionTime, session, TimeMs(seconds)), Pack(SetPlaySpeed, 1, 0));

    public static bool Watch(int numberCode, int group, int camera) =>
        numberCode >= 0 && Send(Pack(CamSwitchNum, numberCode, group, camera));

    public static bool ToLive() => Send(Pack(Search, SearchToEnd, 0), Pack(SetPlaySpeed, 1, 0));

    private static bool Send(params (nint WParam, nint LParam)[] commands)
    {
        if (!AppWatcher.IsRunningNow(TelemetryService.SimProcess)) return false;
        uint message = RegisterWindowMessage("IRSDK_BROADCASTMSG");
        if (message == 0) return false;
        bool sent = true;
        foreach (var (w, l) in commands) sent &= SendNotifyMessage(new IntPtr(0xFFFF), message, w, l);
        return sent;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
