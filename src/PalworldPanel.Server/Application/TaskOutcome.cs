namespace PalworldPanel.Server.Application;

public static class TaskOutcome
{
    public static string TerminalState(string currentState, string phase) =>
        currentState == "Queued" || phase is "Preflight" or "Save" ? "Failed" : "NeedsAttention";
}
