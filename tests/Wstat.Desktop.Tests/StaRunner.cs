namespace Wstat.Desktop.Tests;

internal static class StaRunner
{
    public static void Run(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { exception = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception != null)
            throw new Exception("STA thread threw", exception);
    }
}
