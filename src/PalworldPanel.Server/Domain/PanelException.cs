namespace PalworldPanel.Server.Domain;

public sealed class PanelException(string code, string message, int status = 422) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
