using Archiva.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Archiva.Application.TodoItems.EventHandlers;

public class LogTodoItemCompleted : INotificationHandler<TodoItemCompletedEvent>
{
    private readonly ILogger<LogTodoItemCompleted> _logger;

    public LogTodoItemCompleted(ILogger<LogTodoItemCompleted> logger)
    {
        _logger = logger;
    }

    public Task Handle(TodoItemCompletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Archiva Domain Event: {DomainEvent}", notification.GetType().Name);

        return Task.CompletedTask;
    }
}
