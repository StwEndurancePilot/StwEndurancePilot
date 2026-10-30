using EndurancePilot.Core;

namespace EndurancePilot.App;

public sealed partial class MainForm
{
    private bool automaticTransitionFocus;
    private readonly TransitionFocus transitionFocus = new();
    private nint transitionPreviousWindow, transitionGameWindow;
    private DateTimeOffset? screenSleepSince;
    private string? transitionError;
    private bool transitionWaitingForInput;

    private GameLogPlaytime? SleepSignal => gameLog.Playtime is { NeedsKeepPlaying: true } sleeping ? sleeping
        : screenSleepSince is { } seen && (gameLog.Playtime is null || gameLog.Playtime.ChangedAt < seen)
            ? new(true, seen) : null;

    private async Task<bool> HandleTransitionFocusAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var game = GameWindow.Find();
        bool held = InputIsHeld();
        transitionWaitingForInput = held;
        if (game?.IsForeground == true) transitionError = null;
        if (transitionFocus.Active is not null)
        {
            if (!automaticTransitionFocus || !runner.IsRunning || runner.ControlsPaused
                || controller.Status == ControllerStatus.PausedForAttention || game?.IsForeground != true
                || game.Handle != transitionGameWindow)
            {
                CancelInput();
                runner.ReacquireInput();
                interactionPause.Begin();
                transitionError = "Automatic action interrupted. Return to Fortnite to continue.";
            }
            else if (transitionFocus.Expired(now))
            {
                ReleaseTransitionFocus();
                controller.Pause("Automatic transition timed out. Check Fortnite, then Stop and Start to retry.");
            }
            return false;
        }
        if (gameLog.CatchingUp) return false;
        int endedIncludingCurrent = runSession.AttemptsEnded + (tracker.Current is { Outcome: RunOutcome.InProgress } ? 1 : 0);
        bool canRepeat = runnerMode && runSession.Phase == SessionPhase.Running
            && (runSession.AttemptLimit is null || endedIncludingCurrent < runSession.AttemptLimit);
        var request = transitionFocus.Request(automaticTransitionFocus, runner.IsRunning, runner.ControlsPaused,
            controller.Status is ControllerStatus.Stopped or ControllerStatus.PausedForAttention, game?.IsForeground == true,
            canRepeat, runner.StartedAt ?? now, SleepSignal, gameLog.ResultAvailableAt, now, held);
        if (request is null) return false;
        transitionError = null;
        int capturedSession = session, generation = runner.InputGeneration;
        var token = inputCancellation.Token;
        busy = true;
        try
        {
            var focused = await GameWindow.FocusForTransitionAsync(() => !closed && capturedSession == session
                && runner.CanControl(generation) && automaticTransitionFocus, token);
            if (focused is null)
            {
                transitionFocus.Cancel();
                status.Text = transitionError = "Windows did not allow the automatic transition. Return to Fortnite to continue.";
                AddActivity(status.Text);
                return true;
            }
            (transitionGameWindow, transitionPreviousWindow) = focused.Value;
            interactionPause.Begin();
            runner.ReacquireInput();
            status.Text = request.Reason == TransitionReason.Wake ? "Checking Keep Playing before waking Fortnite"
                : "Checking the result screen before rewards and the next run";
            AddActivity(status.Text);
            log.Write("transition-focus", new { request.Reason, request.EvidenceAt });
            timer.Interval = 250;
            return true;
        }
        finally { busy = false; }
    }

    private void ReleaseTransitionFocus()
    {
        bool returned = GameWindow.ReturnTransitionFocus(transitionGameWindow, transitionPreviousWindow);
        CancelInput();
        runner.ReacquireInput();
        interactionPause.Begin();
        if (!returned)
        {
            controller.Pause("Automatic action finished, but Windows could not restore the previous window. Check the desktop, then Stop and Start to continue.");
            status.Text = controller.Detail;
        }
    }

    internal void SetAutomaticTransitionFocus(bool enabled)
    {
        settingsStore.Save(ReadSettings() with { AutomaticTransitionFocus = enabled });
        automaticTransitionFocus = enabled;
        transitionError = null;
        if (enabled) transitionFocus.Reset();
        if (!enabled) { CancelInput(); runner.ReacquireInput(); }
    }
}
