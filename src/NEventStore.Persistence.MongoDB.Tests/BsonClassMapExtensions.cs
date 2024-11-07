using MongoDB.Bson.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NEventStore.Persistence.MongoDB.Tests
{
    /// <summary>
    /// MongoDB BsonClassMap Extensions
    /// </summary>
    internal static class BsonClassMapExtensions
    {
        /// <summary>
        /// Register a class if not already registered
        /// </summary>
        /// <typeparam name="TClass"></typeparam>
        public static void RegisterClassMapIfNotRegistered<TClass>()
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(TClass)))
                BsonClassMap.RegisterClassMap<TClass>();
        }

        /// <summary>
        /// Register a class if not already registered
        /// </summary>
        /// <typeparam name="TClass"></typeparam>
        /// <param name="classMapInitializer">class map initializer</param>
        public static void RegisterClassMapIfNotRegistered<TClass>(this Action<BsonClassMap<TClass>> classMapInitializer)
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(TClass)))
                BsonClassMap.RegisterClassMap(classMapInitializer);
        }

        /// <summary>
        /// Register a BsonClassMap if the ClassType was not already registered
        /// </summary>
        /// <param name="map">The class map</param>
        public static void RegisterClassMapIfNotRegistered(this BsonClassMap map)
        {
            if (!BsonClassMap.IsClassMapRegistered(map.ClassType))
                BsonClassMap.RegisterClassMap(map);
        }

        /// <summary>
        /// <para>Remove a registered ClassMap for the specified Type</para>
        /// <para>WARNING: use only in test environments</para>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void UnregisterClassMap<T>()
        {
            var classType = typeof(T);
            GetClassMaps()?.Remove(classType);
        }

        private static Dictionary<Type, BsonClassMap>? GetClassMaps()
        {
            var cm = BsonClassMap.GetRegisteredClassMaps().FirstOrDefault();
            if (cm == null)
                return null;

            var fi = typeof(BsonClassMap).GetField("__classMaps", BindingFlags.Static | BindingFlags.NonPublic);
            return fi == null
                ? throw new InvalidOperationException("BsonClassMap.__classMaps field not found. Internal implementation changed!")
                : fi?.GetValue(cm) as Dictionary<Type, BsonClassMap>;
        }

        /// <summary>
        /// <para>Clear all the registered ClassMaps</para>
        /// <para>WARNING: use only in test environments</para>
        /// </summary>
        public static void ClearClassMaps()
        {
            var classMaps = GetClassMaps();
            classMaps?.Clear();
        }
    }
}
