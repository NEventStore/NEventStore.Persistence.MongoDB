using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NEventStore.Persistence.MongoDB
{
    /// <summary>
    /// Keeps a cache of MongoClient instances.
    /// MongoDB best practices recommend using a single MongoClient instance per application.
    /// </summary>
    internal static class MongoClientCache
    {
        private static readonly ConcurrentDictionary<MongoClientCacheKey, IMongoClient> _cache = new ConcurrentDictionary<MongoClientCacheKey, IMongoClient>();

        public static IMongoClient GetClient(MongoClientCacheKey key)
        {
            return _cache.TryGetValue(key, out IMongoClient client) ? client : null;
        }

        public static bool TryAddClient(MongoClientCacheKey key, IMongoClient client)
        {
            return _cache.TryAdd(key, client);
        }

        public static void Clear()
        {
            _cache.Clear();
        }

        internal struct MongoClientCacheKey
        {
            public string ConnectionString { get; set; }
            public Action<MongoClientSettings> ConfigureClientSettingsAction { get; set; }

            public MongoClientCacheKey(string connectionString, Action<MongoClientSettings> configureClientSettingsAction)
            {
                ConnectionString = connectionString;
                ConfigureClientSettingsAction = configureClientSettingsAction;
            }

            public override readonly bool Equals(object obj)
            {
                return obj is MongoClientCacheKey key &&
                       ConnectionString == key.ConnectionString &&
                       EqualityComparer<Action<MongoClientSettings>>.Default.Equals(ConfigureClientSettingsAction, key.ConfigureClientSettingsAction);
            }

            public override readonly int GetHashCode()
            {
#if NET471_OR_GREATER
                int hashCode = -1168874633;
                hashCode = (hashCode * -1521134295) + EqualityComparer<string>.Default.GetHashCode(ConnectionString);
                hashCode = (hashCode * -1521134295) + EqualityComparer<Action<MongoClientSettings>>.Default.GetHashCode(ConfigureClientSettingsAction);
                return hashCode;
#else
                return HashCode.Combine(ConnectionString, ConfigureClientSettingsAction);
#endif
            }
        }
    }
}
