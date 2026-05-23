namespace ContextMenuMgr.Frontend.Services;

public sealed class BackendRequestException : InvalidOperationException
{
    public BackendRequestException(string message, string? errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
