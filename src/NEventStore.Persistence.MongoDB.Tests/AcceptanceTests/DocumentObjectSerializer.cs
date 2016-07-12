using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NEventStore.Logging;
using NEventStore.Serialization;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
    public class DocumentObjectSerializer : IDocumentSerializer
    {
        private static readonly ILog Logger = LogFactory.BuildLogger(typeof(DocumentObjectSerializer));

        public object Serialize<T>(T graph)
        {
            return graph;
        }

        public T Deserialize<T>(object document)
        {
            return (T)document;
        }
    }
}
