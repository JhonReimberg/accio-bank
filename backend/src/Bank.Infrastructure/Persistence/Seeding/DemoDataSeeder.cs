using Bank.Application.Abstractions;
using Bank.Domain.Accounts;
using Bank.Domain.Boletos;
using Bank.Domain.Cards;
using Bank.Domain.Credit;
using Bank.Domain.Customers;
using Bank.Domain.Fraud;
using Bank.Domain.Investments;
using Bank.Domain.Ledger;
using Bank.Domain.Pix;
using Bank.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo customers in different situations, with back-dated, ledger-consistent history. Runs once
/// (skips if the seed users exist). Only fictitious people and generated CPFs.
/// </summary>
public class DemoDataSeeder(BankDbContext db, ISecretHasher hasher, IFileStorage storage, TimeProvider clock, ILogger<DemoDataSeeder> logger)
{
    public const string Password = "Vela@demo123";
    public const string Pin = "2580";
    private const string DocumentPath = "seed/sample-document.png";

    // 1x1 PNG, enough for the back-office document viewer.
    private static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly Random _random = new(2026); // deterministic CPFs and ids across resets
    private DateTime _now;

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == "carla@vela.dev", ct)) return;
        _now = clock.GetUtcNow().UtcDateTime;
        await storage.SaveAsync(DocumentPath, new MemoryStream(SamplePng), ct);
        var cash = await db.Accounts.SingleAsync(a => a.Id == SystemAccounts.Cash, ct);

        // Eligible for credit (phase 2): 4-month-old Plus account with steady recent movement.
        var carla = CreateCustomer("Carla", "carla@vela.dev", "Carla Mendes de Souza", 12_000, "Engenheira de dados", daysAgo: 120, "+5511987650001");
        // Blocked by account age: opened 10 days ago.
        var diego = CreateCustomer("Diego", "diego@vela.dev", "Diego Rocha Alves", 3_200, "Vendedor", daysAgo: 10, "+5521987650002");
        // Delinquent: took a loan 75 days ago, paid the 1st installment, the 2nd is overdue.
        var eduardo = CreateCustomer("Eduardo", "eduardo@vela.dev", "Eduardo Lima Pereira", 2_100, "Motorista", daysAgo: 300, "+5531987650003");
        // Submitted and waiting in the KYC queue (visible when auto-approval is off).
        CreateCustomer("Fernanda", "fernanda@vela.dev", "Fernanda Alves Costa", 6_500, "Advogada", daysAgo: 1, "+5541987650004", approve: false);

        AddStaff("Ana Analista", "analista@vela.dev", Role.Analyst);
        AddStaff("Admin Vela", "admin@vela.dev", Role.Admin);

        AddKey(carla.Account, PixKeyType.Email, "carla@vela.dev", daysAgo: 115);
        AddKey(carla.Account, PixKeyType.Random, null, daysAgo: 115);
        AddKey(diego.Account, PixKeyType.Phone, "+5521987650002", daysAgo: 9);
        AddKey(eduardo.Account, PixKeyType.Cpf, eduardo.Customer.Cpf, daysAgo: 290);

        var history = new List<(int DaysAgo, Action<int> Post)>
        {
            (280, d => Deposit(cash, eduardo, 450, d)),
            (110, d => Deposit(cash, carla, 8_000, d)),
            (70, d => Pix(carla, eduardo, 180, "Conserto do carro", d)),
            (45, d => Pix(eduardo, carla, 60, "Parte do almoço", d)),
            (21, d => Pix(carla, diego, 350, "Ingresso do show", d)),
            (12, d => Pix(diego, carla, 120, "Pizza", d)),
            (9, d => Deposit(cash, diego, 900, d)),
            (6, d => Pix(carla, eduardo, 95.90m, "Presente", d)),
            (3, d => Pix(eduardo, diego, 40, null, d)),
            (2, d => Pix(carla, diego, 1_250, "Aluguel da casa de praia", d)),
        };
        for (var month = 3; month >= 0; month--) history.Add((month * 30 + 5, d => Deposit(cash, carla, 6_200, d)));

        // Eduardo's loan: disbursed, mostly spent on a PIX the next day, 1st installment paid on its due date.
        var today = DateOnly.FromDateTime(Application.Common.BankTime.ToLocal(_now));
        var portfolio = await db.Accounts.SingleAsync(a => a.Id == SystemAccounts.LoanPortfolio, ct);
        var revenue = await db.Accounts.SingleAsync(a => a.Id == SystemAccounts.CreditRevenue, ct);
        var iofPayable = await db.Accounts.SingleAsync(a => a.Id == SystemAccounts.IofPayable, ct);
        var quote = LoanMath.Quote(1_200, 6, 0.069m, today.AddDays(-75), new IofRates());
        Loan? loan = null;
        history.Add((75, d =>
        {
            var tx = LedgerTransaction.Post(TransactionKind.LoanDisbursement, At(d),
                new Posting(portfolio, EntryDirection.Debit, quote.RequestedAmount, "Empréstimo concedido"),
                new Posting(eduardo.Account, EntryDirection.Credit, quote.NetAmount, "Empréstimo pessoal (6x)"),
                new Posting(iofPayable, EntryDirection.Credit, quote.Iof, "IOF sobre empréstimo"));
            Post(tx);
            loan = Loan.Contract(eduardo.Account.Id, eduardo.User.Id, quote, 520, tx.Id, tx.CreatedAt);
            db.Loans.Add(loan);
        }));
        history.Add((74, d => Pix(eduardo, carla, 1_000, "Devolução do empréstimo da Carla", d)));
        var firstDue = quote.Schedule[0].DueDate;
        history.Add((today.DayNumber - firstDue.DayNumber, d =>
        {
            var installment = loan!.Installment(1);
            var charge = installment.ChargeOn(firstDue, loan.MonthlyRate, 0.02m, 0.00033m);
            var tx = LedgerTransaction.Post(TransactionKind.LoanPayment, At(d),
                new Posting(eduardo.Account, EntryDirection.Debit, charge.Total, "Parcela 1/6 do empréstimo"),
                new Posting(portfolio, EntryDirection.Credit, charge.Principal, "Amortização de empréstimo"),
                new Posting(revenue, EntryDirection.Credit, charge.Revenue, "Juros e encargos de empréstimo"));
            Post(tx);
            loan.RegisterPayment(installment, charge, tx.Id, firstDue, tx.CreatedAt);
        }));

        // Phase 3. Carla: a goal caixinha that receives card round-ups, a CDB, a credit card with an installment purchase.
        var custody = await db.Accounts.SingleAsync(a => a.Id == SystemAccounts.InvestmentCustody, ct);
        var settlement = await db.Accounts.SingleAsync(a => a.Id == SystemAccounts.CardSettlement, ct);
        var trip = Investment.Open(carla.Account.Id, InvestmentType.Caixinha, "Viagem para o Nordeste", 6_000, 1m, At(60));
        trip.SetRoundUp(true);
        var cdb = Investment.Open(carla.Account.Id, InvestmentType.Cdb, "CDB Liquidez Diária", null, 1m, At(60));
        db.Investments.AddRange(trip, cdb);
        void Invest(Investment investment, decimal amount, int daysAgo)
        {
            var at = At(daysAgo);
            investment.Apply(amount, at);
            Post(LedgerTransaction.Transfer(TransactionKind.InvestmentApplication, carla.Account, custody, amount,
                $"Aplicação: {investment.Name}", $"Custódia: {investment.Name}", at));
        }
        history.Add((60, d => Invest(trip, 1_500, d)));
        history.Add((30, d => Invest(trip, 800, d)));
        history.Add((25, d => Invest(cdb, 3_000, d)));

        var line = CreditLine.Open(carla.Account.Id, 6_000, dueDay: 10);
        db.CreditLines.Add(line);
        var creditCard = Card.Issue(carla.Account.Id, CardFunction.Credit, CardForm.Virtual, carla.Customer.FullName!, At(40), _random);
        var debitCard = Card.Issue(carla.Account.Id, CardFunction.Debit, CardForm.Physical, carla.Customer.FullName!, At(40), _random);
        db.Cards.AddRange(creditCard, debitCard);
        void CreditPurchase(string merchant, decimal amount, int installments, int daysAgo)
        {
            var at = At(daysAgo);
            var purchase = CardPurchase.Record(creditCard.Id, carla.Account.Id, merchant, amount, installments, true, at);
            line.RegisterPurchase(purchase.Id, merchant, amount, installments, DateOnly.FromDateTime(Application.Common.BankTime.ToLocal(at)));
            db.CardPurchases.Add(purchase);
            Post(LedgerTransaction.Transfer(TransactionKind.CardPurchase, settlement, cash, amount,
                $"A receber: compra no crédito em {merchant}", $"Repasse ao estabelecimento: {merchant}", at));
        }
        history.Add((8, d => CreditPurchase("Livraria Página Aberta", 459.90m, 3, d)));
        history.Add((4, d => CreditPurchase("Restaurante Maré Alta", 186.40m, 1, d)));
        history.Add((1, d =>
        {
            var at = At(d);
            var purchase = CardPurchase.Record(debitCard.Id, carla.Account.Id, "Padaria Aurora", 18.40m, 1, false, at);
            purchase.SetRoundUp(0.60m);
            db.CardPurchases.Add(purchase);
            Post(LedgerTransaction.Transfer(TransactionKind.CardPurchase, carla.Account, cash, 18.40m, "Compra no débito: Padaria Aurora",
                "Repasse ao estabelecimento: Padaria Aurora", at));
            Invest(trip, 0.60m, d);
        }));
        db.Boletos.Add(Boleto.Issue(carla.Account.Id, carla.Customer.FullName!, 280, today.AddDays(12), "Aula de fotografia", today, _now, _random));

        // Posted oldest first: running balances (BalanceAfter) depend on the order.
        foreach (var (daysAgo, post) in history.OrderByDescending(h => h.DaysAgo)) post(daysAgo);

        // Diego: a PIX far above his average held by anti-fraud, waiting in the analyst's queue.
        var diegoAverage = 80m; // his two previous PIX: 120 + 40
        var reasons = new FraudPolicy().Evaluate(new FraudSignals(1_200, diegoAverage, 0, NewDevice: true));
        var hold = diego.Account.PlaceHold(1_200, "Análise antifraude", At(0));
        db.BalanceHolds.Add(hold);
        var carlaKey = db.PixKeys.Local.First(k => k.AccountId == carla.Account.Id);
        var review = FraudReview.Hold(diego.Account.Id, diego.User.Id, 1_200, carla.Account.Id, carla.Customer.FullName!, carlaKey.Type, carlaKey.Value,
            "Notebook usado", hold.Id, reasons, At(0));
        review.ClearDomainEvents();
        db.FraudReviews.Add(review);
        // The 2nd installment is past due and his balance is short of it: overdue (raises the "Parcela em atraso" alert).
        loan!.MarkOverdue(today);
        db.PixSchedules.Add(PixSchedule.Create(carla.Account.Id, PixKeyType.Phone, "+5521987650002", 150, "Mesada",
            ScheduleFrequency.Monthly, today.AddDays(5), null, today, _now));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo data seeded: customers carla/diego/eduardo/fernanda@vela.dev, staff analista/admin@vela.dev");
    }

    private sealed record Seeded(User User, Account Account, Customer Customer, string Name);

    private Seeded CreateCustomer(
        string nickname, string email, string fullName, decimal income, string occupation, int daysAgo, string phone, bool approve = true)
    {
        var openedAt = _now.AddDays(-daysAgo);
        var user = User.Create(nickname, email, openedAt);
        user.SetPasswordHash(hasher.Hash(Password));
        user.SetTransactionPin(Pin, hasher.Hash);
        var account = Account.OpenCustomer(user.Id, openedAt);
        var customer = Customer.Start(user.Id, openedAt);
        customer.SetPersonalData(fullName, Cpf.Generate(_random), new DateOnly(1985 + _random.Next(15), _random.Next(1, 13), _random.Next(1, 28)),
            phone, DateOnly.FromDateTime(openedAt));
        customer.SetAddress(new Address("01310100", "Avenida Paulista", (100 + _random.Next(1800)).ToString(), null, "Bela Vista", "São Paulo", "SP"));
        customer.SetFinancialProfile(income, occupation);
        customer.AttachDocument(DocumentType.IdentityDocument, DocumentPath, "image/png", SamplePng.Length, openedAt);
        customer.AttachDocument(DocumentType.Selfie, DocumentPath, "image/png", SamplePng.Length, openedAt);
        customer.Submit(hasTransactionPin: true, openedAt);
        if (approve)
        {
            customer.Approve(reviewerId: null, openedAt.AddHours(2));
            account.Activate(Account.TierForIncome(income), openedAt.AddHours(2));
        }
        // Seeding should not flood the new users with "account approved" notifications.
        customer.ClearDomainEvents();

        db.Users.Add(user);
        db.Accounts.Add(account);
        db.Customers.Add(customer);
        db.Devices.Add(Device.Register(user.Id, $"seed-{nickname.ToLowerInvariant()}", "Chrome no Android", null, openedAt, alertOwner: false));
        return new Seeded(user, account, customer, fullName);
    }

    private void AddStaff(string name, string email, Role role)
    {
        var user = User.Create(name, email, _now.AddDays(-365), role);
        user.SetPasswordHash(hasher.Hash(Password));
        db.Users.Add(user);
    }

    private void AddKey(Account account, PixKeyType type, string? value, int daysAgo)
    {
        var ownerCpf = db.Customers.Local.Single(c => c.UserId == account.UserId).Cpf!;
        db.PixKeys.Add(PixKey.Register(account.Id, type, value, ownerCpf, db.PixKeys.Local.Count(k => k.AccountId == account.Id), _now.AddDays(-daysAgo)));
    }

    private void Deposit(Account cash, Seeded to, decimal amount, int daysAgo) =>
        Post(LedgerTransaction.Transfer(TransactionKind.Deposit, cash, to.Account, amount, "Depósito de cliente", "Depósito", At(daysAgo)));

    private void Pix(Seeded from, Seeded to, decimal amount, string? description, int daysAgo)
    {
        var at = At(daysAgo);
        var tx = LedgerTransaction.Transfer(TransactionKind.Pix, from.Account, to.Account, amount,
            $"PIX enviado para {to.Name}", $"PIX recebido de {from.Name}", at);
        Post(tx);
        var key = db.PixKeys.Local.First(k => k.AccountId == to.Account.Id);
        db.PixTransfers.Add(PixTransfer.Record(from.Account.Id, to.Account.Id, amount, description, key.Type, key.Value, tx.Id, at, _random));
    }

    private void Post(LedgerTransaction tx)
    {
        tx.ClearDomainEvents();
        db.LedgerTransactions.Add(tx);
    }

    // Spread events over the business day (12h-20h UTC = 9h-17h in Brasília) so the statement looks natural.
    // Same-day events get increasing times, keeping the posting order.
    private DateTime At(int daysAgo)
    {
        var at = _now.Date.AddDays(-daysAgo).AddHours(12 + _random.Next(8)).AddMinutes(_random.Next(60));
        if (at <= _lastAt) at = _lastAt.AddMinutes(7);
        _lastAt = at;
        return at;
    }

    private DateTime _lastAt = DateTime.MinValue;
}
