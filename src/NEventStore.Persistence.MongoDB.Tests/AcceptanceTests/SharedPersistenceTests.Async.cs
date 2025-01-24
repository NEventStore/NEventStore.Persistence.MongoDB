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

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Async
{
#if MSTEST
    [TestClass]
#endif
    public class When_a_commit_is_persisted_from_a_second_process : SpecificationBase
    {
        private ICommit? _commit1;
        private ICommit? _commit2;
        private IPersistStreams? _process1;
        private IPersistStreams? _process2;

        protected override async Task ContextAsync()
        {
            _process1 = new AcceptanceTestMongoPersistenceFactory().Build();
            _process1.Initialize();
            _commit1 = await _process1.CommitAsync(Guid.NewGuid().ToString().BuildAttempt()).ConfigureAwait(false);

            _process2 = new AcceptanceTestMongoPersistenceFactory().Build();
            _process2.Initialize();
        }

        protected override async Task BecauseAsync()
        {
            _commit2 = await _process2!.CommitAsync(Guid.NewGuid().ToString().BuildAttempt()).ConfigureAwait(false);
        }

        [Fact]
        public void Should_have_a_checkpoint_greater_than_the_previous_commit_on_the_other_process()
        {
            Assert.That(_commit1, Is.Not.Null);
            long chkNum1 = _commit1!.CheckpointToken;
            Assert.That(_commit2, Is.Not.Null);
            long chkNum2 = _commit2!.CheckpointToken;

            chkNum2.Should().BeGreaterThan(chkNum1);
        }

        protected override void Cleanup()
        {
            _process1?.Drop();
            _process1?.Dispose();
            _process2?.Dispose();
        }
    }
}