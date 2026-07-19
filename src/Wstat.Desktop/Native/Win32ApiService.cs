namespace Wstat.Desktop.Native;

internal sealed class Win32ApiService : IWin32Api
{
    public IntPtr GetForegroundWindow() => Win32Api.GetForegroundWindow();

    public string? GetForegroundProcessPath() => Win32Api.GetForegroundProcessPath();

    public string? GetForegroundWindowTitle() => Win32Api.GetForegroundWindowTitle();

    public uint GetLastInputTick() => Win32Api.GetLastInputTick();

    public void ActivateExistingInstance() => Win32Api.ActivateExistingInstance();
}
