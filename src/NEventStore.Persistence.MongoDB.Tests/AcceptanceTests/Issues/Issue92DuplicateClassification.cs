using System.Reflection;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
#if NUNIT
using NUnit.Framework;
namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues;
[TestFixture(11000, "_id", "Checkpoint")]
[TestFixture(11000, "CommitId", "Commit")]
[TestFixture(11000, "StreamRevision", "Other")]
[TestFixture(121, "_id", "None")]
public class when_Issue92_structured_duplicate_details_are_available(int code, string key, string expected) : SpecificationBase
{
    private string? _actual;
    protected override void Because()
    {
        var error = new MongoCommandException(new global::MongoDB.Driver.Core.Connections.ConnectionId(new global::MongoDB.Driver.Core.Servers.ServerId(new global::MongoDB.Driver.Core.Clusters.ClusterId(), new System.Net.DnsEndPoint("localhost", 27017))), "message without index information", new BsonDocument(),
            new BsonDocument { { "code", code }, { "keyPattern", key == "CommitId" ? new BsonDocument { { "BucketId", 1 }, { "StreamId", 1 }, { "CommitId", 1 } } : new BsonDocument(key, 1) } });
        var classifier = typeof(MongoPersistenceEngine).Assembly.GetType("NEventStore.Persistence.MongoDB.DuplicateKeyClassifier");
        _actual = classifier?.GetMethod("Classify", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { error })!.ToString();
    }
    [Fact] public void Should_use_structured_error_identity_instead_of_message_text() => _actual.Should().Be(expected);
}
#endif


#if NUNIT
[TestFixture("E11000 duplicate key error collection: db.c index: _id_ dup key: { _id: 1 }", "Checkpoint")]
[TestFixture("E11000 duplicate key error index: db.c.$_id_ dup key: { : 1 }", "Checkpoint")]
[TestFixture("E11001 duplicate key error index: CommitId_Index dup key: { : 1 }", "Commit")]
[TestFixture("E11000 duplicate key error index: db.c.$CommitId_Index dup key: { : 1 }", "Commit")]
[TestFixture("E11000 duplicate key error index: StreamRevision_Index dup key: { value: '_id_ CommitId_Index' }", "Other")]
[TestFixture("E11000 duplicate key error index: custom_id_ dup key: { : 1 }", "Other")]
[TestFixture("E11000 duplicate key error index: CommitId_Index_extra dup key: { : 1 }", "Other")]
[TestFixture("E11000 duplicate key error without index", "Other")]
[TestFixture("validation rejected user text E11000 duplicate key error index: _id_ dup key: {}", "None")]
[TestFixture("E11002 duplicate key error index: _id_ dup key: {}", "None")]
public class when_Issue92_legacy_duplicate_messages_are_classified(string message, string expected) : SpecificationBase
{
    private string? _actual;
    protected override void Because()
    {
        var classifier = typeof(MongoPersistenceEngine).Assembly.GetType("NEventStore.Persistence.MongoDB.DuplicateKeyClassifier");
        _actual = classifier?.GetMethod("Classify", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { new MongoException(message) })!.ToString();
    }
    [Fact] public void Should_only_use_the_duplicate_prefix_and_complete_index_token() => _actual.Should().Be(expected);
}
#endif
