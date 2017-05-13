﻿// 
// IArray.cs
// 
// Author:
//     Jim Borden  <jim.borden@couchbase.com>
// 
// Copyright (c) 2017 Couchbase, Inc All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// 
using System.Collections;

namespace Couchbase.Lite
{
    /// <summary>
    /// An interface representing a writeable collection of objects
    /// </summary>
    public interface IArray : IReadOnlyArray, IArrayFragment
    {
        #region Properties

        /// <summary>
        /// Gets the value of the given index, or lack thereof,
        /// wrapped inside of a <see cref="Fragment"/>
        /// </summary>
        /// <param name="index">The index to check</param>
        /// <returns>The value of the given index, or lack thereof</returns>
        new Fragment this[int index] { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds an entry to this collection
        /// </summary>
        /// <param name="value">The value to add</param>
        /// <returns>Itself for further processing</returns>
        IArray Add(object value);

        /// <summary>
        /// Gets the value at the given index as an array
        /// </summary>
        /// <param name="index">The index to lookup</param>
        /// <returns>The value at the index, or <c>null</c></returns>
        new IArray GetArray(int index);

        /// <summary>
        /// Gets the value at the given index as an <see cref="IDictionaryObject"/>
        /// </summary>
        /// <param name="index">The index to lookup</param>
        /// <returns>The value at the index, or <c>null</c></returns>
        new IDictionaryObject GetDictionary(int index);

        /// <summary>
        /// Inserts a given value at the given index
        /// </summary>
        /// <param name="index">The index to insert the item at</param>
        /// <param name="value">The item to insert</param>
        /// <returns>The value at the index, or <c>null</c></returns>
        IArray Insert(int index, object value);

        /// <summary>
        /// Removes the item at the given index
        /// </summary>
        /// <param name="index">The index at which to remove the item</param>
        /// <returns>The value at the index, or <c>null</c></returns>
        IArray RemoveAt(int index);

        /// <summary>
        /// Replaces the contents of this collection with the contents of the given one
        /// </summary>
        /// <param name="array">The contents to replace the current contents</param>
        /// <returns>The value at the index, or <c>null</c></returns>
        IArray Set(IList array);

        /// <summary>
        /// Overwrites the value at the given index with the given value
        /// </summary>
        /// <param name="index">The index to overwrite</param>
        /// <param name="value">The value to insert</param>
        /// <returns>The value at the index, or <c>null</c></returns>
        IArray Set(int index, object value);

        #endregion
    }
}
