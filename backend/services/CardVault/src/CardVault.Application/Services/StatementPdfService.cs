using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CardVault.Application.Services;

public sealed class StatementPdfService
{
    private readonly CardVaultDbContext _db;

    public StatementPdfService(CardVaultDbContext db)
    {
        _db = db;

        // Community license is fine for demos.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GenerateAsync(Guid statementId, CancellationToken ct)
    {
        var st = await _db.Statements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == statementId, ct);
        if (st is null) throw new InvalidOperationException("Statement not found");

        var lines = await _db.StatementLines.AsNoTracking()
            .Where(x => x.StatementId == statementId)
            .OrderBy(x => x.PostedOn)
            .ToListAsync(ct);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("CardSwitchPlatform").Bold().FontSize(16);
                        col.Item().Text("Estado de Cuenta").FontSize(12);
                    });

                    row.ConstantItem(170).AlignRight().Column(col =>
                    {
                        col.Item().Text($"Statement: {st.Id}").FontSize(9);
                        col.Item().Text($"Fecha: {st.StatementDate:yyyy-MM-dd}").FontSize(9);
                        col.Item().Text($"Vence: {st.DueDate:yyyy-MM-dd}").FontSize(9);
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Text($"Cuenta: {st.AccountId}");
                    col.Item().Text($"Ciclo: {st.CycleStart:yyyy-MM-dd} .. {st.CycleEnd:yyyy-MM-dd}");

                    col.Item().LineHorizontal(1);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        void Row(string k, string v)
                        {
                            table.Cell().Padding(2).Text(k).SemiBold();
                            table.Cell().Padding(2).AlignRight().Text(v);
                        }

                        Row("Saldo anterior", st.PreviousBalance.ToString("0.00"));
                        Row("Compras", st.Purchases.ToString("0.00"));
                        Row("Pagos", st.Payments.ToString("0.00"));
                        Row("Fees", st.Fees.ToString("0.00"));
                        Row("Interés", st.Interest.ToString("0.00"));
                        Row("Nuevo saldo", st.NewBalance.ToString("0.00"));
                        Row("Pago mínimo", st.MinimumPayment.ToString("0.00"));
                        Row("Pagado", st.PaidAmount.ToString("0.00"));

                        if (st.LateFeeAppliedOn is not null)
                            Row("Late fee", st.LateFeeAmount.ToString("0.00"));

                        Row("ADB", st.AverageDailyBalance.ToString("0.00"));
                        Row("APR", (st.InterestApr * 100m).ToString("0.00") + "%");
                        Row("Días interés", st.InterestDays.ToString());
                    });

                    col.Item().LineHorizontal(1);

                    col.Item().Text("Detalle de movimientos").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Padding(2).Text("Fecha").SemiBold();
                            header.Cell().Padding(2).Text("Tipo").SemiBold();
                            header.Cell().Padding(2).AlignRight().Text("Monto").SemiBold();
                            header.Cell().Padding(2).Text("Descripción").SemiBold();
                        });

                        foreach (var l in lines)
                        {
                            table.Cell().Padding(2).Text(l.PostedOn.ToString("yyyy-MM-dd"));
                            table.Cell().Padding(2).Text(l.Type.ToString());
                            table.Cell().Padding(2).AlignRight().Text(l.Amount.ToString("0.00"));
                            table.Cell().Padding(2).Text(l.Description);
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generado por CardSwitchPlatform • ").FontSize(9);
                    x.Span(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")).FontSize(9);
                });
            });
        });

        return doc.GeneratePdf();
    }
}
