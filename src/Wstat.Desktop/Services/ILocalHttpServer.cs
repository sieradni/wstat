namespace Wstat.Desktop.Services;

public interface ILocalHttpServer
{
    bool IsRunning { get; }
    void Start();
    void Stop();
}
