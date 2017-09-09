namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
    using System;
    using NEventStore.Persistence.AcceptanceTests;
    using NEventStore.Persistence.AcceptanceTests.BDD;
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

#if MSTEST
    [TestClass]
#endif
    public class when_a_commit_is_persisted_from_a_second_process : SpecificationBase
    {
        private ICommit _commit1;
        private ICommit _commit2;
        private IPersistStreams _process1;
        private IPersistStreams _process2;

        protected override void Context()
        {
            _process1 = new AcceptanceTestMongoPersistenceFactory().Build();
            _process1.Initialize();
            _commit1 = _process1.Commit(Guid.NewGuid().ToString().BuildAttempt());

            _process2 = new AcceptanceTestMongoPersistenceFactory().Build();
            _process2.Initialize();
        }

        protected override void Because()
        {
            _commit2 = _process2.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        [Fact]
        public void should_have_a_checkpoint_greater_than_the_previous_commit_on_the_other_process()
        {
            long chkNum1 = _commit1.CheckpointToken;
            long chkNum2 = _commit2.CheckpointToken;

            chkNum2.Should().BeGreaterThan(chkNum1);
        }

        protected override void Cleanup()
        {
            _process1.Drop();
            _process1.Dispose();
            _process2.Dispose();
        }
    }
}