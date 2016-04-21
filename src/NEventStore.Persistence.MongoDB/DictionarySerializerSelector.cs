using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;
using System.Collections.Generic;

namespace NEventStore.Persistence.MongoDB
{
	/// <summary>
	/// this is used in an extension method, so it should be passed as argument or cached in a static field
	/// </summary>
	internal static class DictionarySerializerSelector
	{
		internal static DictionaryRepresentation DictionaryRepresentation;
		internal static IBsonSerializer DictionarySerializer;

		public static void SetDictionaryRepresentation(DictionaryRepresentation dictionaryRepresentation)
		{
			DictionaryRepresentation = dictionaryRepresentation;
			DictionarySerializer = new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>(DictionaryRepresentation);
		}
	}
}
