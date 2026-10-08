using System.Collections;
using System.Linq.Expressions;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Queries.GetDocumentContent;
using Archiva.Domain.Entities;
using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Documents;

public class GetDocumentContentQueryHandlerTests
{
    [TestCase(2, 2)]
    [TestCase(1, 2)]
    public async Task DeniesDocumentsOutsideTheCurrentOrganisation(
        int documentOrganizationId,
        int meetingOrganizationId
    )
    {
        var storage = new Mock<IStorageService>();
        var handler = CreateHandler(
            [
                new Document
                {
                    Id = 42,
                    OrganizationId = documentOrganizationId,
                    Meeting = new Meeting { OrganizationId = meetingOrganizationId },
                    BlobName = "private/foreign.pdf",
                    FileType = "PDF",
                },
            ],
            storage
        );

        await Should.ThrowAsync<NotFoundException>(() =>
            handler.Handle(new GetDocumentContentQuery(42), CancellationToken.None)
        );

        storage.Verify(
            service => service.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public async Task ReturnsContentForADocumentInTheCurrentOrganisation()
    {
        var expectedContent = new byte[] { 1, 2, 3 };
        var storage = new Mock<IStorageService>();
        storage
            .Setup(service =>
                service.DownloadAsync("org-1/private.pdf", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(expectedContent);
        var handler = CreateHandler(
            [
                new Document
                {
                    Id = 42,
                    OrganizationId = 1,
                    Meeting = new Meeting { OrganizationId = 1 },
                    BlobName = "org-1/private.pdf",
                    FileType = "PDF",
                },
            ],
            storage
        );

        var result = await handler.Handle(new GetDocumentContentQuery(42), CancellationToken.None);

        result.Content.ShouldBeSameAs(expectedContent);
        result.ContentType.ShouldBe("application/pdf");
        storage.Verify(
            service => service.DownloadAsync("org-1/private.pdf", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Test]
    public async Task DeniesUsersWithoutAnOrganisationMembership()
    {
        var storage = new Mock<IStorageService>();
        var handler = CreateHandler(
            [
                new Document
                {
                    Id = 42,
                    OrganizationId = 1,
                    Meeting = new Meeting { OrganizationId = 1 },
                    BlobName = "org-1/private.pdf",
                    FileType = "PDF",
                },
            ],
            storage,
            []
        );

        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetDocumentContentQuery(42), CancellationToken.None)
        );

        storage.Verify(
            service => service.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    private static GetDocumentContentQueryHandler CreateHandler(
        IEnumerable<Document> documents,
        Mock<IStorageService> storage,
        IEnumerable<OrganizationUser>? members = null
    )
    {
        var context = new Mock<IApplicationDbContext>();
        context
            .SetupGet(database => database.OrganizationUsers)
            .Returns(
                CreateDbSet(
                    members ?? [new OrganizationUser { UserId = "user-1", OrganizationId = 1 }]
                )
            );
        context.SetupGet(database => database.Documents).Returns(CreateDbSet(documents));

        var currentUser = new Mock<IUser>();
        currentUser.SetupGet(user => user.Id).Returns("user-1");

        return new GetDocumentContentQueryHandler(
            context.Object,
            storage.Object,
            currentUser.Object,
            Mock.Of<ILogger<GetDocumentContentQueryHandler>>()
        );
    }

    private static DbSet<T> CreateDbSet<T>(IEnumerable<T> values)
        where T : class
    {
        var queryable = values.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IAsyncEnumerable<T>>()
            .Setup(source => source.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(
                (CancellationToken _) => new TestAsyncEnumerator<T>(queryable.GetEnumerator())
            );
        mock.As<IQueryable<T>>()
            .Setup(source => source.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(source => source.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(source => source.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>()
            .Setup(source => source.GetEnumerator())
            .Returns(() => queryable.GetEnumerator());
        return mock.Object;
    }

    private sealed class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAsyncQueryProvider<T>(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) =>
            new TestAsyncEnumerable<T>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) =>
            inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(
            Expression expression,
            CancellationToken cancellationToken = default
        )
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executeMethod = typeof(IQueryProvider)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(IQueryProvider.Execute) && method.IsGenericMethod
                )
                .MakeGenericMethod(resultType);
            var result = executeMethod.Invoke(inner, [expression]);
            var fromResult = typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType);
            return (TResult)fromResult.Invoke(null, [result])!;
        }
    }

    private sealed class TestAsyncEnumerable<T>
        : EnumerableQuery<T>,
            IAsyncEnumerable<T>,
            IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable)
            : base(enumerable) { }

        public TestAsyncEnumerable(Expression expression)
            : base(expression) { }

        public IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }
}
