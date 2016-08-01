using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NEventStore.Persistence.MongoDB.Tests.MongoDbSerializationTests
{
	public class BsonDocumentWrapperSerialization
	{
		[Fact]
		public void SerializeByteArray()
		{
			var serialized = BsonDocumentWrapper.Create(new byte[] { });
			Assert.True(serialized.IsBsonDocument);
		}
	}
}
