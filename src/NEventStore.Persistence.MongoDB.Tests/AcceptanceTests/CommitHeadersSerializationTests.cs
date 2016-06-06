using MongoDB.Bson;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Should;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
	/// <summary>
	/// Be carefull! this test will fail with 'Catastrophic Failure' and Visual Studio IDE will report this as not run.
	/// </summary>
	public class when_serializing_headers_as_Document_and_a_commit_header_has_a_name_that_contains_a_period : SpecificationBase
	{
		// private ICommit _persisted;
		private string _streamId;
		private IPersistStreams Persistence;

		private Exception _thrown;

		protected override void Context()
		{
			Persistence = new AcceptanceTestMongoPersistenceFactory(
				new MongoPersistenceOptions
				{
					CommitHeadersDictionaryRepresentation =
						global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.Document
				}
				).Build();
		}

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
					new List<EventMessage> { new EventMessage { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } } });
				Persistence.Commit(attempt);
			});

			// _persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
		}

		[Fact]
		public void should_throw_serialization_exception_due_to_invalid_key()
		{
			// _persisted.Headers.Keys.ShouldContain("key.1");

			_thrown.ShouldBeInstanceOf<BsonSerializationException>();
			_thrown.Message.ShouldContain("key.1");
		}
	}

	public class when_serializing_headers_as_Document_and_a_commit_header_has_a_valid_name : SpecificationBase
	{
		private ICommit _persisted;
		private string _streamId;
		private IPersistStreams Persistence;

		protected override void Context()
		{
			Persistence = new AcceptanceTestMongoPersistenceFactory(
				new MongoPersistenceOptions
				{
					CommitHeadersDictionaryRepresentation =
						global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.Document
				}
				).Build();

			_streamId = Guid.NewGuid().ToString();
			var attempt = new CommitAttempt(_streamId,
				2,
				Guid.NewGuid(),
				1,
				DateTime.Now,
				new Dictionary<string, object> { { "key", "value" } },
				new List<EventMessage> { new EventMessage { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } } });
			Persistence.Commit(attempt);
		}

		protected override void Because()
		{
			_persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
		}

		[Fact]
		public void should_correctly_deserialize_headers()
		{
			_persisted.Headers.Keys.ShouldContain("key");
		}
	}

	public class when_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_name_that_contains_a_period : SpecificationBase
	{
		private ICommit _persisted;
		private string _streamId;
		private IPersistStreams Persistence;

		protected override void Context()
		{
			Persistence = new AcceptanceTestMongoPersistenceFactory(
				new MongoPersistenceOptions
				{
					CommitHeadersDictionaryRepresentation =
					global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.ArrayOfArrays
				}
				).Build();

			_streamId = Guid.NewGuid().ToString();
			var attempt = new CommitAttempt(_streamId,
				2,
				Guid.NewGuid(),
				1,
				DateTime.Now,
				new Dictionary<string, object> { { "key.1", "value" } },
				new List<EventMessage> { new EventMessage { Body = new NEventStore.Persistence.AcceptanceTests.ExtensionMethods.SomeDomainEvent { SomeProperty = "Test" } } });
			Persistence.Commit(attempt);
		}

		protected override void Because()
		{
			_persisted = Persistence.GetFrom(_streamId, 0, int.MaxValue).First();
		}

		[Fact]
		public void should_correctly_deserialize_headers()
		{
			_persisted.Headers.Keys.ShouldContain("key.1");
		}
	}
}
