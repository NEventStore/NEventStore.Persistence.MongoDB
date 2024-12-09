using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NEventStore.Persistence.AcceptanceTests;
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NEventStore.Persistence.AcceptanceTests.BDD;
#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if NUNIT
using NUnit.Framework;
#endif
#if XUNIT
    using Xunit;
    using Xunit.Should;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
    /// <summary>
    /// the problem here is that this is 'static', no way to change it once it defined, so the tests need to be run
    /// manually :(
    /// or I need to implement a fully featured serializer specifically designed for testing
    /// or I reset the ClassMap (maybe using reflection and re-init it again)
    /// </summary>
    internal static class MapMongoCommit
    {
        public static void MapMongoCommit_Header_as_Document()
        {
            BsonClassMapExtensions.UnregisterClassMap<MongoCommit>();
            if (!BsonClassMap.IsClassMapRegistered(typeof(MongoCommit)))
            {
                BsonClassMap.RegisterClassMap<MongoCommit>(cm =>
                {
                    cm.AutoMap();
                    cm.MapMember(c => c.Headers)
                        // I cannot use this directly, because the the Headers collection is declared as an interface and it's serialized with: ImpliedImplementationInterfaceSerializer
                        //.SetSerializer(new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>(global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.Document));
                        .SetSerializer(
                            new ImpliedImplementationInterfaceSerializer<IDictionary<string, object>, Dictionary<string, object>>()
                                .WithImplementationSerializer(
                                    new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>(global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.Document)
                                ));
                });
            }
        }

        // this is the default behavior
        public static void MapMongoCommit_Header_as_ArrayOfArray()
        {
            BsonClassMapExtensions.UnregisterClassMap<MongoCommit>();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_serializing_headers_as_Document_and_a_commit_header_has_a_name_that_contains_a_period : PersistenceEngineConcern
    {
        private ICommit? _persisted;
        private string? _streamId;

        private Exception? _thrown;

        public When_serializing_headers_as_Document_and_a_commit_header_has_a_name_that_contains_a_period()
        {
            MapMongoCommit.MapMongoCommit_Header_as_Document();
        }

        protected override void Context()
        { }

        protected override void Because()
        {
            _thrown = Catch.Exception(() =>
            {
                _streamId = Guid.NewGuid().ToString();
                var attempt = new CommitAttempt(_streamId,
                    2,
                    Guid.NewGuid(),
                    1,
                    DateTime.Now,
                    new Dictionary<string, object> { { "key.1", "value" } },
                    [new() { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } }]);
                Persistence.Commit(attempt);
            });

            _persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
        }

        [Fact]
        public void Should_correctly_deserialize_headers()
        {
            // with previous drivers this resulted in an error
            // _thrown.Should().BeOfType<BsonSerializationException>();
            // _thrown.Message.Should().Contain("key.1");

            _thrown.Should().BeNull();
            _persisted!.Headers.Keys.Should().Contain("key.1");
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_serializing_headers_as_Document_and_a_commit_header_has_a_valid_name : PersistenceEngineConcern
    {
        private ICommit? _persisted;
        private string? _streamId;

        public When_serializing_headers_as_Document_and_a_commit_header_has_a_valid_name()
        {
            MapMongoCommit.MapMongoCommit_Header_as_Document();
        }

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var attempt = new CommitAttempt(_streamId,
                2,
                Guid.NewGuid(),
                1,
                DateTime.Now,
                new Dictionary<string, object> { { "key", "value" } },
                [new() { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } }]);
            Persistence.Commit(attempt);
        }

        protected override void Because()
        {
            _persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
        }

        [Fact]
        public void Should_correctly_deserialize_headers()
        {
            _persisted.Should().NotBeNull();
            _persisted!.Headers.Keys.Should().Contain("key");
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_name_that_contains_a_period : PersistenceEngineConcern
    {
        private ICommit? _persisted;
        private string? _streamId;

        public When_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_name_that_contains_a_period()
        {
            // the default is ArrayOfArray defined using an attribute.
            MapMongoCommit.MapMongoCommit_Header_as_ArrayOfArray();
        }

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var attempt = new CommitAttempt(_streamId,
                2,
                Guid.NewGuid(),
                1,
                DateTime.Now,
                new Dictionary<string, object> { { "key.1", "value" } },
                [new() { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } }]);
            Persistence.Commit(attempt);
        }

        protected override void Because()
        {
            _persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
        }

        [Fact]
        public void Should_correctly_deserialize_headers()
        {
            _persisted.Should().NotBeNull();
            _persisted!.Headers.Keys.Should().Contain("key.1");
        }
    }

    // guid serialization tests (inside headers)

#if MSTEST
    [TestClass]
#endif
    public class When_serializing_headers_as_Document_and_a_commit_header_has_a_Guid : PersistenceEngineConcern
    {
        private ICommit? _persisted;
        private string? _streamId;
        private readonly Guid _guid = Guid.NewGuid();

        private Exception? _thrown;

        public When_serializing_headers_as_Document_and_a_commit_header_has_a_Guid()
        {
            MapMongoCommit.MapMongoCommit_Header_as_Document();
        }

        protected override void Context()
        { }

        protected override void Because()
        {
            _thrown = Catch.Exception(() =>
            {
                _streamId = Guid.NewGuid().ToString();
                var attempt = new CommitAttempt(_streamId,
                    2,
                    Guid.NewGuid(),
                    1,
                    DateTime.Now,
                    new Dictionary<string, object> { { "guid", _guid } },
                    [new() { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } }]);
                Persistence.Commit(attempt);
            });

            Assert.That(_thrown, Is.Null);

            _persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
        }

        [Fact]
        public void Should_correctly_deserialize_headers()
        {
            _thrown.Should().BeNull();
            _persisted!.Headers.Keys.Should().Contain("guid");
            _persisted.Headers["guid"].Should().Be(_guid);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_Guid : PersistenceEngineConcern
    {
        private ICommit? _persisted;
        private string? _streamId;
        private readonly Guid _guid = Guid.NewGuid();

        public When_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_Guid()
        {
            // the default is ArrayOfArray defined using an attribute.
            MapMongoCommit.MapMongoCommit_Header_as_ArrayOfArray();
        }

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var attempt = new CommitAttempt(_streamId,
                2,
                Guid.NewGuid(),
                1,
                DateTime.Now,
                new Dictionary<string, object> { { "guid", _guid } },
                [new() { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } }]);
            Persistence.Commit(attempt);
        }

        protected override void Because()
        {
            _persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
        }

        [Fact]
        public void Should_correctly_deserialize_headers()
        {
            _persisted!.Headers.Keys.Should().Contain("guid");
            _persisted.Headers["guid"].Should().Be(_guid);
        }
    }
}
