using System;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using NEventStore.Persistence.AcceptanceTests;
using FluentAssertions;
using MongoDB.Driver;
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

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
#if MSTEST
	[TestClass]
#endif
	public class Issue8 : SpecificationBase
	{
		private Exception? _error;
		private const string InvalidConnectionStringName = "this_is_not_a_connection_string";

		protected override void Context()
		{
		}

		protected override void Because()
		{
			_error = Catch.Exception(() =>
			{
				Wireup.Init()
					.UsingMongoPersistence(InvalidConnectionStringName, new DocumentObjectSerializer())
					.Build();
			});
		}

#if NET472_OR_GREATER
		/// <summary>
		/// in net462, UsingMongoPersistence will look for a connection string name in the app.config
		/// and it will not find anything
		/// </summary>
		[Fact]
		public void A_configuration_exception_should_be_thrown()
		{
			_error!.Should().BeOfType<NEventStore.Persistence.MongoDB.ConfigurationException>();
		}
#endif

#if !NET472_OR_GREATER
		/// <summary>
		/// in netstandard2.0, UsingMongoPersistence will accept a connectionString which will be invalid
		/// </summary>
		[Fact]
		public void A_configuration_exception_should_be_thrown()
		{
			_error!.Should().BeOfType<MongoConfigurationException>();
		}
#endif

		[Fact]
		public void A_configuration_error_should_be_thrown()
		{
			_error!.Message.Should().Contain(InvalidConnectionStringName);
		}
	}
}
