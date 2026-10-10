using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Infrastructure.Analysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Archiva.Infrastructure.IntegrationTests;

public sealed class AnalysisBudgetTests : IntegrationTestBase
{
    [Test]
    public async Task Concurrent_reservations_never_exceed_the_monthly_billable_limit()
    {
        var budget = CreateBudget();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[] { 101, 102 }
            .Select(async documentId =>
            {
                await start.Task;
                return await budget.TryReserveBillableUnitsAsync(documentId, 3, 5);
            })
            .ToArray();

        start.SetResult();
        var accepted = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(60));
        var usage = await IntegrationHost.WithDbAsync(db =>
            db.DocumentAnalysisUsages.ToListAsync()
        );

        accepted.Count(value => value).ShouldBe(1);
        usage.Sum(row => row.BillableUnits).ShouldBe(3);
    }

    [Test]
    public async Task Reservations_are_idempotent_and_a_new_month_has_a_fresh_limit()
    {
        var budget = CreateBudget();

        (await budget.TryReserveBillableUnitsAsync(201, 5, 5)).ShouldBeTrue();
        (await budget.TryReserveBillableUnitsAsync(201, 5, 5)).ShouldBeTrue();
        (await budget.TryReserveBillableUnitsAsync(201, 4, 5)).ShouldBeFalse();
        (await budget.TryReserveBillableUnitsAsync(202, 1, 5)).ShouldBeFalse();

        IntegrationHost.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        (await budget.TryReserveBillableUnitsAsync(202, 5, 5)).ShouldBeTrue();

        var rows = await IntegrationHost.WithDbAsync(db =>
            db.DocumentAnalysisUsages.OrderBy(row => row.MonthStartUtc).ToListAsync()
        );
        rows.Count.ShouldBe(2);
        rows.Select(row => row.MonthStartUtc.Month).ShouldBe([10, 11]);
    }

    [Test]
    public async Task Summary_reservation_counts_only_growth_and_rejects_monthly_overage()
    {
        var budget = CreateBudget();

        (await budget.TryReserveSummaryCharactersAsync(301, 80, 100)).ShouldBeTrue();
        (await budget.TryReserveSummaryCharactersAsync(301, 80, 100)).ShouldBeTrue();
        (await budget.TryReserveSummaryCharactersAsync(301, 90, 100)).ShouldBeTrue();
        (await budget.TryReserveSummaryCharactersAsync(302, 11, 100)).ShouldBeFalse();

        var rows = await IntegrationHost.WithDbAsync(db => db.DocumentAnalysisUsages.ToListAsync());
        rows.Sum(row => row.SummaryInputCharacters).ShouldBe(90);
    }

    private static IDocumentAnalysisBudget CreateBudget() =>
        new DocumentAnalysisBudget(
            IntegrationHost.Services.GetRequiredService<IServiceScopeFactory>(),
            IntegrationHost.Services.GetRequiredService<IOptions<DocumentAnalysisOptions>>(),
            IntegrationHost.Clock
        );
}
