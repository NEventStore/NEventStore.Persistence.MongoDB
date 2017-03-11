using System;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using NEventStore.Persistence.AcceptanceTests;
using FluentAssertions;
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
		private Exception _error;
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

		[Fact]
		public void a_configuration_exception_should_be_thrown()
		{
			_error.Should().BeOfType<ConcurrencyException>();
		}

		[Fact]
		public void a_configuration_error_should_be_thrown()
		{
			_error.Message.Should().Contain(InvalidConnectionStringName);
		}
	}
}
