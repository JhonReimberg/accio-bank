using Bank.Application.Accounts;
using Bank.Application.Common;
using Bank.Domain.Ledger;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Bank.Infrastructure.Documents;

internal class QuestPdfReceiptRenderer : IReceiptRenderer
{
    private const string Brand = "#0D5C54";

    public byte[] RenderPdf(ReceiptData r) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A5);
        page.Margin(28);
        page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

        page.Header().Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("Vela").FontSize(20).Bold().FontColor(Brand);
                c.Item().Text("Banco digital fictício — projeto de portfólio").FontSize(8).FontColor(Colors.Grey.Medium);
            });
            row.AutoItem().AlignBottom().Text(Title(r.Kind)).FontSize(12).SemiBold();
        });

        page.Content().PaddingVertical(16).Column(col =>
        {
            col.Spacing(12);
            col.Item().Text(Formatting.Brl(r.Amount)).FontSize(24).Bold();
            col.Item().Text(BankTime.ToLocal(r.CreatedAt).ToString("dd/MM/yyyy 'às' HH:mm:ss") + " (horário de Brasília)");
            col.Item().Text(r.Description);

            if (r.Payer is not null) PartyBlock(col, "Origem", r.Payer);
            if (r.Payee is not null) PartyBlock(col, "Destino", r.Payee);

            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            foreach (var (label, value) in r.Details) Line(col, label, value);
            Line(col, "Autenticação", r.TransactionId.ToString().ToUpperInvariant());
        });

        page.Footer().AlignCenter().Text("Documento sem valor fiscal. Instituição fictícia, sem relação com bancos reais.")
            .FontSize(7).FontColor(Colors.Grey.Medium);
    })).GeneratePdf();

    private static void PartyBlock(ColumnDescriptor col, string title, ReceiptParty party) =>
        col.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
        {
            c.Item().Text(title).FontSize(8).FontColor(Colors.Grey.Darken1);
            c.Item().Text(party.Name).SemiBold();
            if (party.MaskedCpf is not null) c.Item().Text($"CPF {party.MaskedCpf}");
            if (party.AccountNumber is not null) c.Item().Text($"Vela · Conta {party.AccountNumber}");
        });

    private static void Line(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(row =>
        {
            row.ConstantItem(110).Text(label).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().Text(value);
        });

    private static string Title(TransactionKind kind) => kind switch
    {
        TransactionKind.Deposit => "Comprovante de depósito",
        TransactionKind.InternalTransfer => "Comprovante de transferência",
        TransactionKind.Pix => "Comprovante de PIX",
        TransactionKind.PixRefund => "Comprovante de devolução de PIX",
        _ => "Comprovante",
    };
}
