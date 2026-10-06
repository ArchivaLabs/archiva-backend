using System.Data;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Domain.Entities;
using Archiva.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Archiva.Infrastructure.Analysis;

public sealed class DocumentAnalysisBudget : IDocumentAnalysisBudget
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DocumentAnalysisOptions _options;
    private readonly TimeProvider _timeProvider;

    public DocumentAnalysisBudget(
        IServiceScopeFactory scopeFactory,
        IOptions<DocumentAnalysisOptions> options,
        TimeProvider timeProvider
    )
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<bool> TryReserveBillableUnitsAsync(
        int documentId,
        int billableUnits,
        int perDocumentLimit,
        CancellationToken cancellationToken = default
    )
    {
        if (billableUnits < 0 || billableUnits > perDocumentLimit)
            return Task.FromResult(false);

        if (billableUnits == 0)
            return Task.FromResult(true);

        var monthStart = GetMonthStartUtc();
        return ExecuteSerializableAsync(
            async (context, token) =>
            {
                var reservation = await GetOrAddReservationAsync(
                    context,
                    documentId,
                    monthStart,
                    token
                );

                if (reservation.BillableUnits > 0)
                    return reservation.BillableUnits == billableUnits;

                var monthlyUnits =
                    await context
                        .DocumentAnalysisUsages.Where(usage => usage.MonthStartUtc == monthStart)
                        .SumAsync(usage => (int?)usage.BillableUnits, token)
                    ?? 0;

                if (monthlyUnits + billableUnits > _options.MonthlyBillableUnitLimit)
                {
                    if (reservation.SummaryInputCharacters == 0)
                    {
                        context.DocumentAnalysisUsages.Remove(reservation);
                        await context.SaveChangesAsync(token);
                    }

                    return false;
                }

                reservation.BillableUnits = billableUnits;
                await context.SaveChangesAsync(token);
                return true;
            },
            cancellationToken
        );
    }

    public Task<bool> TryReserveSummaryCharactersAsync(
        int documentId,
        int characterCount,
        int perDocumentLimit,
        CancellationToken cancellationToken = default
    )
    {
        if (characterCount < 0 || characterCount > perDocumentLimit)
            return Task.FromResult(false);

        var monthStart = GetMonthStartUtc();
        return ExecuteSerializableAsync(
            async (context, token) =>
            {
                var reservation = await GetOrAddReservationAsync(
                    context,
                    documentId,
                    monthStart,
                    token
                );

                var additionalCharacters = Math.Max(
                    0,
                    characterCount - reservation.SummaryInputCharacters
                );
                var monthlyCharacters =
                    await context
                        .DocumentAnalysisUsages.Where(usage => usage.MonthStartUtc == monthStart)
                        .SumAsync(usage => (int?)usage.SummaryInputCharacters, token)
                    ?? 0;

                if (
                    monthlyCharacters + additionalCharacters
                    > _options.MonthlySummaryCharacterLimit
                )
                {
                    if (reservation.BillableUnits == 0 && reservation.SummaryInputCharacters == 0)
                    {
                        context.DocumentAnalysisUsages.Remove(reservation);
                        await context.SaveChangesAsync(token);
                    }

                    return false;
                }

                reservation.SummaryInputCharacters += additionalCharacters;
                await context.SaveChangesAsync(token);
                return true;
            },
            cancellationToken
        );
    }

    private async Task<TResult> ExecuteSerializableAsync<TResult>(
        Func<ApplicationDbContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        await using var strategyScope = _scopeFactory.CreateAsyncScope();
        var strategyContext =
            strategyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var operationScope = _scopeFactory.CreateAsyncScope();
            var context = operationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken
            );

            var result = await operation(context, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private static async Task<DocumentAnalysisUsage> GetOrAddReservationAsync(
        ApplicationDbContext context,
        int documentId,
        DateTime monthStart,
        CancellationToken cancellationToken
    )
    {
        var reservation = await context.DocumentAnalysisUsages.FirstOrDefaultAsync(
            usage => usage.DocumentId == documentId && usage.MonthStartUtc == monthStart,
            cancellationToken
        );

        if (reservation is not null)
            return reservation;

        reservation = new DocumentAnalysisUsage
        {
            DocumentId = documentId,
            MonthStartUtc = monthStart,
        };
        context.DocumentAnalysisUsages.Add(reservation);
        await context.SaveChangesAsync(cancellationToken);
        return reservation;
    }

    private DateTime GetMonthStartUtc()
    {
        var now = _timeProvider.GetUtcNow();
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
