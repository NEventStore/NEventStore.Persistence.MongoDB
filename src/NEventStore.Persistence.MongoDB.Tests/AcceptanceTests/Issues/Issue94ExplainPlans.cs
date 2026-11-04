using FluentAssertions;
using global::MongoDB.Bson;
using NEventStore.Persistence.AcceptanceTests.BDD;

#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
#if MSTEST
    [TestClass]
#endif
    public class Issue_94_exact_id_explain_forms : SpecificationBase
    {
        private readonly Dictionary<string, ExplainPlanResult> _plans = new();

        protected override void Because()
        {
            // IDHACK omits indexName; classic, SBE and EXPRESS plans can expose it
            // at different depths. These fixtures complement the live-server audit.
            _plans["idhack"] = Parse("{ stage: 'IDHACK' }");
            _plans["classic"] = Parse("{ stage: 'FETCH', inputStage: { stage: 'IXSCAN', indexName: '_id_' } }");
            _plans["sbe"] = Parse("{ queryPlan: { stage: 'FETCH', inputStage: { stage: 'IXSCAN', indexName: '_id_' } } }");
            _plans["express"] = Parse("{ stage: 'EXPRESS_IXSCAN', indexName: '_id_' }");
            _plans["array"] = Parse("{ stage: 'SHARD_MERGE', shards: [{ winningPlan: { stage: 'IDHACK' } }] }");
            _plans["scan"] = Parse("{ stage: 'COLLSCAN' }");
            _plans["otherIndex"] = Parse("{ stage: 'IXSCAN', indexName: 'other_index' }");

            var rejected = Explain("{ stage: 'COLLSCAN' }");
            rejected["queryPlanner"]["rejectedPlans"] = new BsonArray
            {
                BsonDocument.Parse("{ stage: 'IXSCAN', indexName: '_id_' }"),
                BsonDocument.Parse("{ stage: 'IDHACK' }")
            };
            _plans["rejected"] = ExplainPlanResult.From(rejected);
        }

        [Fact]
        public void Idhack_without_an_index_name_still_uses_the_id_index()
        {
            _plans["idhack"].Indexes.Should().BeEmpty();
            _plans["idhack"].UsesBuiltinIdIndex.Should().BeTrue();
        }

        [Fact]
        public void Named_id_indexes_are_recognized_in_classic_sbe_and_express_plans()
        {
            foreach (var name in new[] { "classic", "sbe", "express" })
                _plans[name].UsesBuiltinIdIndex.Should().BeTrue(name);
        }

        [Fact]
        public void Plans_nested_in_arrays_are_recognized()
        {
            _plans["array"].UsesBuiltinIdIndex.Should().BeTrue();
        }

        [Fact]
        public void Collection_scans_other_indexes_and_rejected_id_plans_are_not_id_access()
        {
            foreach (var name in new[] { "scan", "otherIndex", "rejected" })
                _plans[name].UsesBuiltinIdIndex.Should().BeFalse(name);
        }

        [Fact]
        public void Execution_work_and_result_count_are_preserved()
        {
            _plans["idhack"].TotalKeysExamined.Should().Be(1);
            _plans["idhack"].TotalDocsExamined.Should().Be(1);
            _plans["idhack"].Returned.Should().Be(1);
        }

        private static ExplainPlanResult Parse(string winningPlan) => ExplainPlanResult.From(Explain(winningPlan));

        private static BsonDocument Explain(string winningPlan) => new()
        {
            ["queryPlanner"] = new BsonDocument("winningPlan", BsonDocument.Parse(winningPlan)),
            ["executionStats"] = new BsonDocument
            {
                ["totalKeysExamined"] = 1,
                ["totalDocsExamined"] = 1,
                ["nReturned"] = 1,
            },
        };
    }
}
