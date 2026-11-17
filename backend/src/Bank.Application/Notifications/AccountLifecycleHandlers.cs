using Bank.Application.Abstractions;
using Bank.Domain.Accounts;
using Bank.Domain.Customers;
using Bank.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Notifications;

public class KycReviewedHandler(IBankDbContext db, IEmailSender email, TimeProvider clock) : IDomainEventHandler<KycReviewed>
{
    public async Task HandleAsync(KycReviewed e, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == e.UserId, ct);
        var (title, body) = e.Approved
            ? ("Conta aprovada!", "Sua conta Vela está ativa. Já pode receber e fazer PIX.")
            : ("Cadastro precisa de ajustes", $"Não conseguimos aprovar seu cadastro: {e.Reason}. Corrija e envie novamente.");
        db.Notifications.Add(Notification.Create(user.Id, title, body, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        await email.SendAsync(user.Email, $"Vela: {title}", body, ct);
    }
}

public class AccountStatusChangedHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<AccountStatusChanged>
{
    public async Task HandleAsync(AccountStatusChanged e, CancellationToken ct)
    {
        if (e.UserId is not { } userId) return;
        var (title, body) = e.Status switch
        {
            AccountStatus.Blocked => ("Conta bloqueada", $"Sua conta foi bloqueada: {e.Reason}. Entre em contato com o atendimento."),
            AccountStatus.Active => ("Conta desbloqueada", "Sua conta voltou a operar normalmente."),
            AccountStatus.Closed => ("Conta encerrada", "Sua conta foi encerrada."),
            _ => (null, null),
        };
        if (title is null) return;
        db.Notifications.Add(Notification.Create(userId, title, body!, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
