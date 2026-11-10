using System.Net;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Infrastructure;

/// <summary>
/// Feature 013 US3 (FR-024): one place decides whether an exception means "the data store did not answer" (a 503 the
/// owner can retry) as opposed to a defect or a rejected write (a 4xx or 500).
/// </summary>
public sealed class DatastoreFailureTests
{
    private static ConnectionId Connection() =>
        new(new ServerId(new ClusterId(1), new DnsEndPoint("db.fictional.invalid", 27017)));

    private static MongoWriteException DuplicateKeyWrite()
    {
        var writeErrorCtor = typeof(WriteError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length == 4 && c.GetParameters()[0].ParameterType == typeof(ServerErrorCategory));
        var writeError = (WriteError)writeErrorCtor.Invoke([ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key error", new BsonDocument()]);

        return new MongoWriteException(Connection(), writeError, null, null);
    }

    private static MongoNotPrimaryException NotPrimary() =>
        new(Connection(), new BsonDocument("insert", "fictional"), new BsonDocument { ["ok"] = 0, ["code"] = 10107, ["errmsg"] = "not primary" });

    [Fact]
    public void ConnectionFailure_IsUnavailable() =>
        Assert.True(DatastoreFailure.IsUnavailable(new MongoConnectionException(Connection(), "fictional connection failure")));

    [Fact]
    public void ServerSelectionTimeout_IsUnavailable() =>
        Assert.True(DatastoreFailure.IsUnavailable(new TimeoutException("A timeout occurred after 3000ms selecting a server using a fictional selector.")));

    [Fact]
    public void NotPrimary_IsUnavailable() =>
        Assert.True(DatastoreFailure.IsUnavailable(NotPrimary()));

    [Fact]
    public void DuplicateKeyWrite_IsNotUnavailable() =>
        Assert.False(DatastoreFailure.IsUnavailable(DuplicateKeyWrite()));

    [Fact]
    public void ArgumentException_IsNotUnavailable() =>
        Assert.False(DatastoreFailure.IsUnavailable(new ArgumentException("fictional bad argument")));

    [Fact]
    public void InvalidOperationException_IsNotUnavailable() =>
        Assert.False(DatastoreFailure.IsUnavailable(new InvalidOperationException("fictional invalid state")));

    [Fact]
    public void AnUnavailableCause_InsideAnotherException_IsStillUnavailable()
    {
        var cause = new MongoConnectionException(Connection(), "fictional connection failure");

        Assert.True(DatastoreFailure.IsUnavailable(new InvalidOperationException("wrapped", cause)));
        Assert.True(DatastoreFailure.IsUnavailable(new AggregateException(new ArgumentException("other"), cause)));
    }

    [Fact]
    public void ACallerCancellation_IsNotUnavailable() =>
        Assert.False(DatastoreFailure.IsUnavailable(new OperationCanceledException("the caller gave up")));
}
