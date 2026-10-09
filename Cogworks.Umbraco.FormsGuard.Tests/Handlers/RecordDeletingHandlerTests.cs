using Cogworks.Umbraco.FormsGuard.Handlers;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core.Events;
using Record = Umbraco.Forms.Core.Persistence.Dtos.Record;
using Umbraco.Forms.Core.Services.Notifications;

namespace Cogworks.Umbraco.FormsGuard.Tests.Handlers;

public class RecordDeletingHandlerTests
{
    [Fact]
    public async Task SingleDelete_DeletesForThatRecord()
    {
        var id = Guid.NewGuid();
        var repo = new DeleteRepo { Result = 1 };

        await Handle(repo, new RecordDeletingNotification(new Record { UniqueId = id }, new EventMessages()));

        var call = Assert.Single(repo.Calls);
        Assert.Equal([id], call.Ids);
        Assert.Equal("Forms record deleted", call.Detail);
    }

    [Fact]
    public async Task BulkDelete_PassesDistinctIdsOnce()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var records = new[] { new Record { UniqueId = a }, new Record { UniqueId = b }, new Record { UniqueId = a } };
        var repo = new DeleteRepo { Result = 1 };

        await Handle(repo, new RecordDeletingNotification(records, new EventMessages()));

        var call = Assert.Single(repo.Calls);
        Assert.Equal(new HashSet<Guid> { a, b }, call.Ids.ToHashSet());
        Assert.Equal(2, call.Ids.Length);
    }

    [Fact]
    public async Task NoRecords_DoesNotCallRepository()
    {
        var repo = new DeleteRepo();

        await Handle(repo, new RecordDeletingNotification(Array.Empty<Record>(), new EventMessages()));

        Assert.Empty(repo.Calls);
    }

    [Fact]
    public async Task RepositoryThrows_DoesNotThrowOrCancel()
    {
        var repo = new DeleteRepo { Throw = true };
        var notification = new RecordDeletingNotification(new Record { UniqueId = Guid.NewGuid() }, new EventMessages());

        await Handle(repo, notification);

        Assert.False(notification.Cancel);
    }

    private static Task Handle(DeleteRepo repo, RecordDeletingNotification notification) =>
        new RecordDeletingHandler(repo, NullLogger<RecordDeletingHandler>.Instance).HandleAsync(notification, CancellationToken.None);

    private sealed class DeleteRepo : RepositoryStub
    {
        public List<(Guid[] Ids, string Detail)> Calls { get; } = [];
        public int Result { get; init; }
        public bool Throw { get; init; }

        public override int DeleteForRecords(IReadOnlyCollection<Guid> recordIds, string detail)
        {
            if (Throw)
            {
                throw new InvalidOperationException("db down");
            }

            Calls.Add((recordIds.ToArray(), detail));
            return Result;
        }
    }
}
