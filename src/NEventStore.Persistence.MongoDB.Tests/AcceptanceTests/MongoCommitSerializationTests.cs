using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NEventStore.Persistence.AcceptanceTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Should;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
	/// <summary>
	/// the problem here is that this is 'static', no way to change it once it it defined, so the tests need to be run
	/// manually :(	
	/// or I need to implement a fully featured serializer specifically designed for testing
	/// </summary>
	static class MapMongoCommit
	{
		public static void MapMongoCommit_Header_as_Document()
		{


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

		}
	}

	/// <summary>
	/// Be carefull! this test will fail with 'Catastrophic Failure' and Visual Studio IDE will report this as not run.
	/// </summary>
	public class when_serializing_headers_as_Document_and_a_commit_header_has_a_name_that_contains_a_period : PersistenceEngineConcern
	{
		// private ICommit _persisted;
		private string _streamId;

		private Exception _thrown;

		public when_serializing_headers_as_Document_and_a_commit_header_has_a_name_that_contains_a_period()
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

	public class when_serializing_headers_as_Document_and_a_commit_header_has_a_valid_name : PersistenceEngineConcern
	{
		private ICommit _persisted;
		private string _streamId;

		public when_serializing_headers_as_Document_and_a_commit_header_has_a_valid_name()
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

	public class when_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_name_that_contains_a_period : PersistenceEngineConcern
	{
		private ICommit _persisted;
		private string _streamId;

		public when_serializing_headers_as_ArrayOfArrays_and_a_commit_header_has_a_name_that_contains_a_period()
		{
			// the default is ArrayOfArray defined using an attribute.
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
