using Archiva.Application.Auth.Command.SyncUser;
using Archiva.Application.Common.Behaviours;
using Archiva.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Common.Behaviours;

public sealed class SanitizedLoggingBehaviourTests
{
    [Test]
    public async Task Auth_sync_request_log_contains_only_request_type_and_user_id()
    {
        var logger = new CaptureLogger<SyncUserCommand>();
        var user = new Mock<IUser>();
        user.SetupGet(value => value.Id).Returns("entra-object-id");
        user.SetupGet(value => value.Name).Returns("Sensitive Person");
        user.SetupGet(value => value.Email).Returns("sensitive@example.invalid");
        var behaviour = new LoggingBehaviour<SyncUserCommand>(logger, user.Object);

        await behaviour.Process(new SyncUserCommand(), CancellationToken.None);

        var entry = logger.Entries.Single();
        entry.Fields["RequestName"].ShouldBe(nameof(SyncUserCommand));
        entry.Fields["UserId"].ShouldBe("entra-object-id");
        entry.Fields.ShouldNotContainKey("UserName");
        entry.Fields.ShouldNotContainKey("Email");
        entry.Fields.ShouldNotContainKey("Request");
        entry.Message.ShouldNotContain("Sensitive Person");
        entry.Message.ShouldNotContain("sensitive@example.invalid");
    }

    [Test]
    public async Task Request_payload_and_exception_log_do_not_serialize_sensitive_input()
    {
        var logger = new CaptureLogger<SensitiveRequest>();
        var user = new Mock<IUser>();
        user.SetupGet(value => value.Id).Returns("entra-object-id");
        var request = new SensitiveRequest("secret@example.invalid");
        var preprocessor = new LoggingBehaviour<SensitiveRequest>(logger, user.Object);
        var exceptionBehaviour = new UnhandledExceptionBehaviour<SensitiveRequest, int>(logger);

        await preprocessor.Process(request, CancellationToken.None);
        var error = new InvalidOperationException("Database unavailable");
        RequestHandlerDelegate<int> next = _ => Task.FromException<int>(error);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            exceptionBehaviour.Handle(request, next, CancellationToken.None)
        );

        logger.Entries.Count.ShouldBe(2);
        foreach (var entry in logger.Entries)
        {
            entry.Fields.ShouldNotContainKey("Request");
            entry.Fields.ShouldNotContainKey("Email");
            entry.Message.ShouldNotContain(request.Email);
        }
        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        failure.Fields["RequestName"].ShouldBe(nameof(SensitiveRequest));
        failure.Exception.ShouldBe(error);
    }

    private sealed record SensitiveRequest(string Email);

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Fields,
        Exception? Exception
    );

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(value => value.Key, value => value.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), fields, exception));
        }
    }
}
