using System;
using System.Configuration;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using Xunit;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public class Issue8 : SpecificationBase
    {
        private Exception _error;
        private const string InvalidConnectionStringName = "this_is_not_a_connection_string";

        protected override void Context()
        {
            
        }

        protected override void Because()
        {
            _error = Assert.Throws<ConfigurationErrorsException>(() =>
            {
                Wireup.Init()
                    .UsingMongoPersistence(InvalidConnectionStringName, new DocumentObjectSerializer())
                    .Build();
            });
        }

        [Fact]
        public void a_configuration_error_should_be_thrown()
        {
            Assert.True(_error.Message.Contains(InvalidConnectionStringName));
        }
    }
}
