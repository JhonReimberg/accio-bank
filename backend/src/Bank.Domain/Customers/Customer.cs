using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Customers;

[JsonConverter(typeof(JsonStringEnumConverter<KycStatus>))]
public enum KycStatus
{
    /// <summary>Onboarding in progress.</summary>
    NotSubmitted,
    PendingReview,
    Approved,
    /// <summary>The customer can fix the data and submit again.</summary>
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter<OnboardingStep>))]
public enum OnboardingStep
{
    PersonalData,
    Address,
    Financial,
    Documents,
    TransactionPin,
    Submit,
    Done,
}

[JsonConverter(typeof(JsonStringEnumConverter<DocumentType>))]
public enum DocumentType
{
    IdentityDocument,
    Selfie,
}

public sealed record Address(
    string Cep, string Street, string Number, string? Complement, string Neighborhood, string City, string State);

/// <summary>Personal and KYC data of an account holder, filled step by step during onboarding.</summary>
public class Customer : Entity
{
    public const int MinimumAge = 18;

    private readonly List<KycDocument> _documents = [];

    private Customer() { }

    public Guid UserId { get; private set; }
    public string? FullName { get; private set; }
    public string? Cpf { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    /// <summary>E.164, e.g. +5511912345678.</summary>
    public string? Phone { get; private set; }
    public Address? Address { get; private set; }
    public decimal? MonthlyIncome { get; private set; }
    public string? Occupation { get; private set; }
    public KycStatus KycStatus { get; private set; }
    public string? KycRejectionReason { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyList<KycDocument> Documents => _documents;

    public static Customer Start(Guid userId, DateTime now) =>
        new() { UserId = userId, KycStatus = KycStatus.NotSubmitted, CreatedAt = now };

    public void SetPersonalData(string fullName, string cpf, DateOnly birthDate, string phone, DateOnly today)
    {
        EnsureEditable();
        if (birthDate > today.AddYears(-MinimumAge))
            throw new DomainException("customer.underage", $"É preciso ter pelo menos {MinimumAge} anos para abrir a conta.");
        FullName = fullName.Trim();
        Cpf = Customers.Cpf.Normalize(cpf);
        BirthDate = birthDate;
        Phone = phone;
    }

    public void SetAddress(Address address)
    {
        EnsureEditable();
        Address = address;
    }

    public void SetFinancialProfile(decimal monthlyIncome, string occupation)
    {
        EnsureEditable();
        Money.EnsureValidAmount(monthlyIncome);
        MonthlyIncome = monthlyIncome;
        Occupation = occupation.Trim();
    }

    /// <summary>Uploading again replaces the previous file of the same type.</summary>
    public KycDocument AttachDocument(DocumentType type, string storagePath, string contentType, long sizeBytes, DateTime now)
    {
        EnsureEditable();
        _documents.RemoveAll(d => d.Type == type);
        var document = KycDocument.Create(Id, type, storagePath, contentType, sizeBytes, now);
        _documents.Add(document);
        return document;
    }

    public OnboardingStep NextStep(bool hasTransactionPin) =>
        KycStatus is KycStatus.PendingReview or KycStatus.Approved ? OnboardingStep.Done
        : FullName is null ? OnboardingStep.PersonalData
        : Address is null ? OnboardingStep.Address
        : MonthlyIncome is null ? OnboardingStep.Financial
        : !HasAllDocuments ? OnboardingStep.Documents
        : !hasTransactionPin ? OnboardingStep.TransactionPin
        : OnboardingStep.Submit;

    public bool HasAllDocuments => Enum.GetValues<DocumentType>().All(t => _documents.Any(d => d.Type == t));

    public void Submit(bool hasTransactionPin, DateTime now)
    {
        EnsureEditable();
        if (NextStep(hasTransactionPin) != OnboardingStep.Submit)
            throw new DomainException("onboarding.incomplete", "Complete todas as etapas do cadastro antes de enviar.");
        KycStatus = KycStatus.PendingReview;
        KycRejectionReason = null;
        SubmittedAt = now;
        Raise(new KycSubmitted(Id, UserId));
    }

    public void Approve(Guid? reviewerId, DateTime now)
    {
        EnsurePendingReview();
        KycStatus = KycStatus.Approved;
        ReviewedAt = now;
        ReviewedBy = reviewerId;
        Raise(new KycReviewed(Id, UserId, Approved: true, null));
    }

    public void Reject(Guid reviewerId, string reason, DateTime now)
    {
        EnsurePendingReview();
        KycStatus = KycStatus.Rejected;
        KycRejectionReason = reason;
        ReviewedAt = now;
        ReviewedBy = reviewerId;
        Raise(new KycReviewed(Id, UserId, Approved: false, reason));
    }

    private void EnsureEditable()
    {
        if (KycStatus is KycStatus.PendingReview or KycStatus.Approved)
            throw new DomainException("onboarding.locked", "Seu cadastro já foi enviado e não pode mais ser alterado.");
    }

    private void EnsurePendingReview()
    {
        if (KycStatus != KycStatus.PendingReview)
            throw new DomainException("kyc.not_pending", "Este cadastro não está aguardando análise.");
    }
}

public class KycDocument : Entity
{
    private KycDocument() { }

    public Guid CustomerId { get; private set; }
    public DocumentType Type { get; private set; }
    public string StoragePath { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public DateTime UploadedAt { get; private set; }

    internal static KycDocument Create(Guid customerId, DocumentType type, string storagePath, string contentType, long sizeBytes, DateTime now) => new()
    {
        CustomerId = customerId,
        Type = type,
        StoragePath = storagePath,
        ContentType = contentType,
        SizeBytes = sizeBytes,
        UploadedAt = now,
    };
}

public sealed record KycSubmitted(Guid CustomerId, Guid UserId) : IDomainEvent;

public sealed record KycReviewed(Guid CustomerId, Guid UserId, bool Approved, string? Reason) : IDomainEvent;
