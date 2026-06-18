namespace Stashr.Ui.Services;

/// <summary>An error returned by the stashr API (carries the HTTP status + server message).</summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
