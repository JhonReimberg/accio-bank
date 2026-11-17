using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Application.Onboarding;
using Bank.Domain.Common;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Accounts;

/// <summary>The signed-in user's profile photo (JPG or PNG, checked by content, not by name).</summary>
public class ProfilePhotoService(IBankDbContext db, ICurrentUser currentUser, IFileStorage storage, IAuditLog audit)
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public async Task SaveAsync(Stream content, long length, CancellationToken ct)
    {
        if (length is <= 0 or > MaxBytes)
            throw new DomainException("photo.size", "A foto deve ter até 2 MB.");

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes) throw new DomainException("photo.size", "A foto deve ter até 2 MB.");
        var file = FileSignatures.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (file is null || !file.ContentType.StartsWith("image/", StringComparison.Ordinal))
            throw new DomainException("photo.type", "A foto deve ser uma imagem JPG ou PNG.");

        var user = await MeAsync(ct);
        // shortcut: the previous file stays in storage (IFileStorage has no delete); add one if storage costs matter.
        var path = $"photos/{user.Id}/{Guid.NewGuid():N}.{file.Extension}";
        buffer.Position = 0;
        await storage.SaveAsync(path, buffer, ct);
        user.SetPhoto(path);
        audit.Record("profile.photo_updated", nameof(User), user.Id, after: new { file.ContentType, Size = buffer.Length });
        await db.SaveChangesAsync(ct);
    }

    public async Task<StoredFile> OpenAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var path = await db.Users.Where(u => u.Id == userId).Select(u => u.PhotoPath).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Você ainda não tem foto de perfil.");
        var extension = path[(path.LastIndexOf('.') + 1)..];
        return new StoredFile(await storage.OpenReadAsync(path, ct), extension == "png" ? "image/png" : "image/jpeg", $"foto.{extension}");
    }

    public async Task RemoveAsync(CancellationToken ct)
    {
        var user = await MeAsync(ct);
        if (user.PhotoPath is null) return;
        user.SetPhoto(null);
        audit.Record("profile.photo_removed", nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
    }

    private async Task<User> MeAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new UnauthorizedException("Usuário não encontrado.");
    }
}
