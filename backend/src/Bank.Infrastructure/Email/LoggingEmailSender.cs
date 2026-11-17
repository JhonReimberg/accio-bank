using Bank.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bank.Infrastructure.Email;

/// <summary>Simulated e-mail provider: writes the message to the structured log.</summary>
internal class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("E-mail (simulado) para {To}: {Subject} — {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
