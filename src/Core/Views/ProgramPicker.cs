namespace Core.Views;

internal static class ProgramPicker
{
    public static string? BrowseExe(string filter = "Programs (*.exe)|*.exe|Shortcuts and scripts (*.lnk;*.bat;*.cmd;*.ps1)|*.lnk;*.bat;*.cmd;*.ps1|All files (*.*)|*.*")
    {
        if (!Core.Services.External.Allowed("open dialog")) return null;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
