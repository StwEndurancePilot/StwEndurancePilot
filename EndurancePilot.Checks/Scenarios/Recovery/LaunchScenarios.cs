using EndurancePilot.Core;

internal static class LaunchScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var supervisor = new LaunchSupervisor();
        GameProcessSnapshot Snapshot(int time, bool game = false, bool boot = false, bool window = false) => new(epoch.AddSeconds(time), game, boot, window);
        supervisor.Start();
        check("A single absent-process reading cannot launch", supervisor.Observe(Snapshot(0)) is null);
        check("A short process gap cannot launch", supervisor.Observe(Snapshot(2)) is null);
        var request = supervisor.Observe(Snapshot(5));
        check("Confirmed absence proposes an Epic launch", request is not null);
        supervisor.Dispatched(request!, epoch.AddSeconds(5));
        check("Slow startup does not trigger a duplicate launch", supervisor.Observe(Snapshot(20, boot: true)) is null && supervisor.Status == LaunchStatus.Starting);
        supervisor.Observe(Snapshot(30, game: true, window: true));
        check("A game window confirms startup", supervisor.Status == LaunchStatus.Monitoring);
        supervisor.Observe(Snapshot(40));
        request = supervisor.Observe(Snapshot(45));
        check("A game exit can trigger a fresh launch", request is not null);
        supervisor.Dispatched(request!, epoch.AddSeconds(45));
        supervisor.Observe(Snapshot(50, game: true, window: true));
        supervisor.Observe(Snapshot(60));
        request = supervisor.Observe(Snapshot(65));
        supervisor.Dispatched(request!, epoch.AddSeconds(65));
        supervisor.Observe(Snapshot(70, game: true, window: true));
        supervisor.Observe(Snapshot(80));
        check("Repeated game exits stop the launch loop", supervisor.Observe(Snapshot(85)) is null && supervisor.Status == LaunchStatus.NeedsAttention);
        supervisor.Start();
        supervisor.Observe(Snapshot(100, game: true));
        check("A game without a visible window is not considered absent", supervisor.Observe(Snapshot(110, game: true)) is null);
        supervisor.Observe(Snapshot(281, game: true));
        check("A stalled startup pauses without killing or relaunching the game", supervisor.Status == LaunchStatus.NeedsAttention);
        supervisor.Stop();
        check("Stopped supervision cannot launch", supervisor.Observe(Snapshot(1000)) is null);
    }
}
