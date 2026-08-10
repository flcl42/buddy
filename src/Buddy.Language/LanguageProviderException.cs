using System.Net;
using Buddy.Core.Abstractions;

namespace Buddy.Language;

public sealed class LanguageProviderException : InvalidOperationException
{
    public LanguageProviderException(
        string message,
        ProviderHealthStatus status,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        StatusCode = statusCode;
    }

    public ProviderHealthStatus Status { get; }

    public HttpStatusCode? StatusCode { get; }
}
