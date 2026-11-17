using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Notifications;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Notifications;

/// <summary>Security alerts go to the app and by e-mail.</summary>
public class NewDeviceSignInHandler(IBankDbContext db, IEmailSender email, TimeProvider clock) : IDomainEventHandler<NewDeviceSignIn>
{
    public async Task HandleAsync(NewDeviceSignIn e, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == e.UserId, ct);
        var when = BankTime.ToLocal(e.At).ToString("dd/MM/yyyy 'às' HH:mm");
        var body = $"Novo acesso pelo dispositivo \"{e.DeviceName}\" em {when}. Se não foi você, troque sua senha e remova o dispositivo.";
        db.Notifications.Add(Notification.Create(user.Id, "Acesso em novo dispositivo", body, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        await email.SendAsync(user.Email, "Vela: acesso em novo dispositivo", body, ct);
    }
}

public class SignInLockedOutHandler(IBankDbContext db, IEmailSender email, TimeProvider clock) : IDomainEventHandler<SignInLockedOut>
{
    public async Task HandleAsync(SignInLockedOut e, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == e.UserId, ct);
        var until = BankTime.ToLocal(e.Until).ToString("HH:mm");
        var body = $"Bloqueamos o acesso à sua conta até {until} após várias tentativas de senha erradas.";
        db.Notifications.Add(Notification.Create(user.Id, "Acesso bloqueado temporariamente", body, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        await email.SendAsync(user.Email, "Vela: acesso bloqueado temporariamente", body, ct);
    }
}
