﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Adletec.Sonic.Util
{
    /// <summary>
    /// An in-memory based cache to store objects. The implementation is thread safe and supports
    /// the multiple platforms (.NET, WinRT, WP7 and WP8).
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    public class MemoryCache<TKey, TValue>
    {
        private readonly int maximumSize;
        private readonly int reductionSize;

        private long counter; // We cannot use DateTime.Now, because the precision is not high enough.

        private readonly ConcurrentDictionary<TKey, CacheItem> dictionary;

        /// <summary>
        /// Create a new instance of the <see cref="MemoryCache{TKey,TValue}"/>.
        /// </summary>
        /// <param name="maximumSize">The maximum allowed number of items in the cache.</param>
        /// <param name="reductionSize">The number of items to be deleted per cleanup of the cache.</param>
        public MemoryCache(int maximumSize, int reductionSize)
        {
            if (maximumSize < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumSize),
                    "The maximum allowed number of items in the cache must be at least one.");

            if (reductionSize < 1)
                throw new ArgumentOutOfRangeException(nameof(reductionSize),
                    "The cache reduction size must be at least one.");

            this.maximumSize = maximumSize;
            this.reductionSize = reductionSize;

            this.dictionary = new ConcurrentDictionary<TKey, CacheItem>();
        }

        /// <summary>
        /// Get the value in the cache for the given key.
        /// </summary>
        /// <param name="key">The key to lookup in the cache.</param>
        /// <returns>The value for the given key.</returns>
        public TValue this[TKey key]
        {
            get
            {
                CacheItem cacheItem = dictionary[key];
                cacheItem.Accessed();
                return cacheItem.Value;
            }
        }

        /// <summary>
        /// Gets the number of items in the cache.
        /// </summary>
        public int Count => dictionary.Count;

        /// <summary>
        /// Returns true if an item with the given key is present in the cache.
        /// </summary>
        /// <param name="key">The key to lookup in the cache.</param>
        /// <returns>True if an item is present in the cache for the given key.</returns>
        public bool ContainsKey(TKey key)
        {
            return dictionary.ContainsKey(key);
        }

        public bool TryGetValue (TKey key, out TValue result)
        {
            if (dictionary.TryGetValue(key, out var cachedItem))
            {
                cachedItem.Accessed();
                result = cachedItem.Value;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// If for a given key an item is present in the cache, this method will return
        /// the value for the given key. If no item is present in the cache for the given
        /// key, the valueFactory is executed to produce the value. This value is stored in
        /// the cache and returned to the caller.
        /// </summary>
        /// <param name="key">The key to lookup in the cache.</param>
        /// <param name="valueFactory">The factory to produce the value matching with the key.</param>
        /// <returns>The value for the given key.</returns>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            if (valueFactory == null)
                throw new ArgumentNullException(nameof(valueFactory));

            CacheItem cacheItem = dictionary.GetOrAdd(key, k => 
                {
                    EnsureCacheStorageAvailable();
                    return new CacheItem(this, valueFactory(k));
                });
            return cacheItem.Value;
        }

        /// <summary>
        /// Ensure that the cache has room for an additional item.
        /// If there is not enough room anymore, force a removal of oldest
        /// accessed items in the cache.
        /// </summary>
        private void EnsureCacheStorageAvailable()
        {
            if (dictionary.Count < maximumSize) return; // < because we want to add an item after this method
            
            IList<TKey> keysToDelete = (from p in dictionary.ToArray()
                where p.Key != null && p.Value != null
                orderby p.Value.LastAccessed
                select p.Key).Take(reductionSize).ToList();

            foreach (var key in keysToDelete)
            {
                dictionary.TryRemove(key, out _);
            }
        }

        private class CacheItem
        {
            private readonly MemoryCache<TKey, TValue> cache;

            public CacheItem(MemoryCache<TKey, TValue> cache, TValue value)
            {
                this.cache = cache;
                this.Value = value;

                Accessed();
            }

            public TValue Value { get; }

            public long LastAccessed { get; private set; }

            public void Accessed()
            {
                this.LastAccessed = Interlocked.Increment(ref cache.counter);
            }
        }
    }
}