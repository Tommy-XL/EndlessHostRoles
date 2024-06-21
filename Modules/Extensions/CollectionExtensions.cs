﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

// ReSharper disable ConvertIfStatementToReturnStatement
// ReSharper disable ForCanBeConvertedToForeach

namespace EHR
{
    public static class CollectionExtensions
    {
        /// <summary>
        /// Returns the key of a dictionary by its value
        /// </summary>
        /// <param name="dictionary">The <see cref="Dictionary{TKey,TValue}"/> to search</param>
        /// <param name="value">The <typeparamref name="TValue"/> used to search for the corresponding key</param>
        /// <typeparam name="TKey">The type of the keys in the <paramref name="dictionary"/></typeparam>
        /// <typeparam name="TValue">The type of the values in the <paramref name="dictionary"/></typeparam>
        /// <returns>The key of the <paramref name="dictionary"/> that corresponds to the given <paramref name="value"/>, or the default value of <typeparamref name="TKey"/> if the <paramref name="value"/> is not found in the <paramref name="dictionary"/></returns>
        public static TKey GetKeyByValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TValue value)
        {
            foreach (KeyValuePair<TKey, TValue> pair in dictionary)
            {
                if (pair.Value.Equals(value))
                {
                    return pair.Key;
                }
            }

            return default;
        }

        /// <summary>
        /// Returns a random element from a collection
        /// </summary>
        /// <param name="collection">The collection</param>
        /// <typeparam name="T">The type of the collection</typeparam>
        /// <returns>A random element from the collection, or the default value of <typeparamref name="T"/> if the collection is empty</returns>
        public static T RandomElement<T>(this IList<T> collection)
        {
            if (collection.Count == 0) return default;
            return collection[IRandom.Instance.Next(collection.Count)];
        }

        /// <summary>
        /// Combines multiple collections into a single collection
        /// </summary>
        /// <param name="firstCollection">The collection to start with</param>
        /// <param name="collections">The other collections to add to <paramref name="firstCollection"/></param>
        /// <typeparam name="T">The type of the elements in the collections to combine</typeparam>
        /// <returns>A collection containing all elements of <paramref name="firstCollection"/> and all <paramref name="collections"/></returns>
        public static IEnumerable<T> CombineWith<T>(this IEnumerable<T> firstCollection, params IEnumerable<T>[] collections)
        {
            return firstCollection.Concat(collections.SelectMany(x => x));
        }

        /// <summary>
        /// Executes an action for each element in a collection
        /// </summary>
        /// <param name="collection">The collection to iterate over</param>
        /// <param name="action">The action to execute for each element</param>
        /// <typeparam name="T">The type of the elements in the collection</typeparam>
        public static IEnumerable<T> Do<T>(this IEnumerable<T> collection, Action<T> action)
        {
            if (collection is List<T> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    action(list[i]);
                }

                return collection;
            }

            foreach (T element in collection)
            {
                action(element);
            }

            return collection;
        }

        /// <summary>
        /// Executes an action for each element in a collection if the predicate is true
        /// </summary>
        /// <param name="collection">The collection to iterate over</param>
        /// <param name="fast">Whether to use a fast loop or linq</param>
        /// <param name="predicate">The predicate to check for each element</param>
        /// <param name="action">The action to execute for each element that satisfies the predicate</param>
        /// <typeparam name="T">The type of the elements in the collection</typeparam>
        public static IEnumerable<T> DoIf<T>(this IEnumerable<T> collection, Func<T, bool> predicate, Action<T> action, bool fast = false)
        {
            if (fast)
            {
                foreach (T element in collection)
                {
                    if (predicate(element))
                    {
                        action(element);
                    }
                }

                return collection;
            }

            var partitioner = Partitioner.Create(collection.Where(predicate));
            return partitioner.GetDynamicPartitions().Do(action);
        }

        /// <summary>
        /// Removes an element from a collection
        /// </summary>
        /// <param name="collection">The collection to remove the element from</param>
        /// <param name="element">The element to remove</param>
        /// <typeparam name="T">The type of the elements in the collection</typeparam>
        /// <returns>A collection containing all elements of <paramref name="collection"/> except for <paramref name="element"/></returns>
        public static IEnumerable<T> Without<T>(this IEnumerable<T> collection, T element)
        {
            return collection.Where(x => !x.Equals(element));
        }

        /// <summary>
        /// Splits a collection into two collections based on a predicate
        /// </summary>
        /// <param name="collection">The collection to split</param>
        /// <param name="predicate">The predicate to split the collection by</param>
        /// <typeparam name="T">The type of the elements in the collection</typeparam>
        /// <returns>A tuple containing two collections: one with elements that satisfy the predicate, and one with elements that do not</returns>
        public static (List<T> TrueList, List<T> FalseList) Split<T>(this IEnumerable<T> collection, Func<T, bool> predicate)
        {
            var list1 = new List<T>();
            var list2 = new List<T>();
            foreach (T element in collection)
            {
                if (predicate(element))
                {
                    list1.Add(element);
                }
                else
                {
                    list2.Add(element);
                }
            }

            return (list1, list2);
        }

        #region Shuffle

        /// <summary>
        /// Shuffles all elements in a collection randomly
        /// </summary>
        /// <typeparam name="T">The type of the collection</typeparam>
        /// <param name="collection">The collection to be shuffled</param>
        /// <returns>A new, shuffled collection as a <see cref="List{T}"/></returns>
        public static List<T> Shuffle<T>(this IEnumerable<T> collection)
        {
            var list = collection.ToList();
            int n = list.Count;
            var r = IRandom.Instance;
            while (n > 1)
            {
                n--;
                int k = r.Next(n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }

            return list;
        }

        /// <summary>
        /// Shuffles all elements in a collection randomly
        /// </summary>
        /// <param name="collection">The collection to be shuffled</param>
        /// <typeparam name="T">The type of the collection</typeparam>
        /// <returns>The same collection with its elements shuffled</returns>
        public static List<T> Shuffle<T>(this List<T> collection)
        {
            int n = collection.Count;
            var r = IRandom.Instance;
            while (n > 1)
            {
                n--;
                int k = r.Next(n + 1);
                (collection[n], collection[k]) = (collection[k], collection[n]);
            }

            return collection;
        }

        /// <summary>
        /// Shuffles all elements in an array randomly
        /// </summary>
        /// <param name="collection">The array to be shuffled</param>
        /// <typeparam name="T">The type of the array</typeparam>
        /// <returns>The same array with its elements shuffled</returns>
        public static T[] Shuffle<T>(this T[] collection)
        {
            int n = collection.Length;
            var r = IRandom.Instance;
            while (n > 1)
            {
                n--;
                int k = r.Next(n + 1);
                (collection[n], collection[k]) = (collection[k], collection[n]);
            }

            return collection;
        }

        #endregion

        #region Partition

        /// <summary>
        /// Partitions a collection into a specified number of parts
        /// </summary>
        /// <param name="collection">The collection to partition</param>
        /// <param name="parts">The number of parts to partition the collection into</param>
        /// <typeparam name="T">The type of the elements in the collection</typeparam>
        /// <returns>A collection of collections, each containing a part of the original collection</returns>
        public static IEnumerable<IEnumerable<T>> Partition<T>(this IEnumerable<T> collection, int parts)
        {
            var list = collection.ToList();
            var length = list.Count;
            if (parts <= 0 || length == 0) yield break;
            if (parts > length) parts = length;
            int size = length / parts;
            int remainder = length % parts;
            int index = 0;
            for (int i = 0; i < parts; i++)
            {
                int partSize = size + (i < remainder ? 1 : 0);
                yield return list.Skip(index).Take(partSize);
                index += partSize;
            }
        }

        /// <summary>
        /// Partitions a list into a specified number of parts
        /// </summary>
        /// <param name="collection">The list to partition</param>
        /// <param name="parts">The number of parts to partition the list into</param>
        /// <typeparam name="T">The type of the elements in the list</typeparam>
        /// <returns>A list of lists, each containing a part of the original list</returns>
        public static IEnumerable<IEnumerable<T>> Partition<T>(this IList<T> collection, int parts)
        {
            var length = collection.Count;
            if (parts <= 0 || length == 0) yield break;
            if (parts > length) parts = length;
            int size = length / parts;
            int remainder = length % parts;
            int index = 0;
            for (int i = 0; i < parts; i++)
            {
                int partSize = size + (i < remainder ? 1 : 0);
                yield return collection.Skip(index).Take(partSize);
                index += partSize;
            }
        }

        #endregion
    }
}