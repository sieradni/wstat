namespace Wstat.Desktop.Native;

public interface IWin32Api
{
    IntPtr GetForegroundWindow();
    string? GetForegroundProcessPath();
    string? GetForegroundWindowTitle();
    uint GetLastInputTick();
    void ActivateExistingInstance();
}
