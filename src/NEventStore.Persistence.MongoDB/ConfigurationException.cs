using System;

namespace NEventStore.Persistence.MongoDB
{
	[Serializable]
	public class ConfigurationException : Exception
	{
		public ConfigurationException()
		{
		}

		public ConfigurationException(string message) : base(message)
		{
		}

		public ConfigurationException(string message, Exception inner) : base(message, inner)
		{
		}

#if !NETSTANDARD1_6
		protected ConfigurationException(
		  System.Runtime.Serialization.SerializationInfo info,
		  System.Runtime.Serialization.StreamingContext context) : base(info, context) 
		{
		}
#endif
	}
}
