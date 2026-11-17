using Bank.Application.Common;
using Bank.Application.Credit;
using Bank.Domain.Credit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Bank.Infrastructure.Documents;

/// <summary>Loan contract (CCB-like, simplified) with the full amortization table.</summary>
internal class QuestPdfLoanContractRenderer : ILoanContractRenderer
{
    private const string Brand = "#0D5C54";

    public byte[] RenderPdf(Loan loan, string customerName, string cpf, string accountNumber) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Colors.Grey.Darken4));

        page.Header().Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("AccioBank").FontSize(20).Bold().FontColor(Brand);
                c.Item().Text("Banco digital fictício — projeto de portfólio").FontSize(8).FontColor(Colors.Grey.Medium);
            });
            row.AutoItem().AlignBottom().Text("Contrato de Empréstimo Pessoal").FontSize(13).SemiBold();
        });

        page.Content().PaddingVertical(14).Column(col =>
        {
            col.Spacing(10);
            col.Item().Text($"Contrato nº {loan.Id.ToString().ToUpperInvariant()} · {BankTime.ToLocal(loan.ContractedAt):dd/MM/yyyy HH:mm}");
            col.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
            {
                c.Item().Text("Cliente (emitente)").FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text(customerName).SemiBold();
                c.Item().Text($"CPF {cpf} · AccioBank · Conta {accountNumber}");
            });

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                void Row(string label, string value)
                {
                    t.Cell().PaddingVertical(2).Text(label).FontColor(Colors.Grey.Darken1);
                    t.Cell().PaddingVertical(2).Text(value);
                }
                Row("Valor do empréstimo", Formatting.Brl(loan.Amount));
                Row("IOF", Formatting.Brl(loan.Iof));
                Row("Valor líquido creditado", Formatting.Brl(loan.NetAmount));
                Row("Parcelas", $"{loan.InstallmentCount}x de {Formatting.Brl(loan.InstallmentAmount)} (Tabela Price)");
                Row("Taxa de juros", $"{Formatting.Percent(loan.MonthlyRate)} a.m.");
                Row("CET", $"{Formatting.Percent(loan.CetMonthly)} a.m. · {Formatting.Percent(loan.CetYearly)} a.a.");
                Row("Total a pagar", Formatting.Brl(loan.Installments.Sum(i => i.Amount)));
            });

            col.Item().Text("Cronograma de pagamento").SemiBold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });
                t.Header(header =>
                {
                    foreach (var h in new[] { "Nº", "Vencimento", "Parcela", "Juros", "Amortização", "Saldo devedor" })
                        header.Cell().BorderBottom(0.5f).PaddingBottom(3).Text(h).SemiBold();
                });
                foreach (var i in loan.Installments.OrderBy(i => i.Number))
                {
                    t.Cell().Text(i.Number.ToString());
                    t.Cell().Text(i.DueDate.ToString("dd/MM/yyyy"));
                    t.Cell().Text(Formatting.Brl(i.Amount));
                    t.Cell().Text(Formatting.Brl(i.Interest));
                    t.Cell().Text(Formatting.Brl(i.Principal));
                    t.Cell().Text(Formatting.Brl(i.BalanceAfter));
                }
            });

            col.Item().Text(
                "Débito automático na conta no vencimento. Em caso de atraso incidem multa de 2% e juros de mora diários sobre a parcela. " +
                "A antecipação de parcelas tem desconto proporcional dos juros. Contratação autorizada com senha transacional.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
        });

        page.Footer().AlignCenter().Text("Documento sem valor jurídico. Instituição fictícia, sem relação com bancos reais.")
            .FontSize(7).FontColor(Colors.Grey.Medium);
    })).GeneratePdf();
}
