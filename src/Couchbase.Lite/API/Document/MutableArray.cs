﻿// 
//  MutableArray.cs
// 
//  Author:
//   Jim Borden  <jim.borden@couchbase.com>
// 
//  Copyright (c) 2017 Couchbase, Inc All rights reserved.
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
// 
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
// 

using System;
using System.Collections;

using Couchbase.Lite.Internal.Doc;
using Couchbase.Lite.Internal.Serialization;

namespace Couchbase.Lite
{
    /// <summary>
    /// A class representing an editable collection of objects
    /// </summary>
    public sealed class MutableArray : ArrayObject, IMutableArray
    {
        #region Properties

        /// <inheritdoc />
        public new IMutableFragment this[int index] => index >= _array.Count
            ? Fragment.Null
            : new Fragment(this, index);

        #endregion

        #region Constructors

        /// <summary>
        /// Default Constructor
        /// </summary>
        public MutableArray()
        {
            
        }

        /// <summary>
        /// Creates an array with the given data
        /// </summary>
        /// <param name="array">The data to populate the array with</param>
        public MutableArray(IList array)
            : this()
        {
            Set(array);
        }

        internal MutableArray(MArray array, bool isMutable)
        {
            _array.InitAsCopyOf(array, isMutable);
        }

        internal MutableArray(MValue mv, MCollection parent)
            : base(mv, parent)
        {
            
        }

        #endregion

        #region Private Methods

        private void SetValueInternal(int index, object value)
        {
            _threadSafety.DoLocked(() =>
            {
                if (DataOps.ValueWouldChange(value, _array.Get(index), _array)) {
                    _array.Set(index, DataOps.ToCouchbaseObject(value));
                }
            });
        }

        #endregion

        #region Overrides

        public override ArrayObject ToImmutable()
        {
            return new ArrayObject(_array, false);
        }

        #endregion

        #region IMutableArray

        /// <inheritdoc />
        public IMutableArray AddValue(object value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(DataOps.ToCouchbaseObject(value));
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddString(string value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddInt(int value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddLong(long value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddFloat(float value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddDouble(double value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddBoolean(bool value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddBlob(Blob value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddDate(DateTimeOffset value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value.ToString("o"));
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddArray(ArrayObject value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray AddDictionary(DictionaryObject value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Add(value);
                return this;
            });
        }

        /// <inheritdoc />
        public new MutableArray GetArray(int index)
        {
            return base.GetArray(index) as MutableArray;
        }

        /// <inheritdoc />
        public new MutableDictionary GetDictionary(int index)
        {
            return base.GetDictionary(index) as MutableDictionary;
        }

        /// <inheritdoc />
        public IMutableArray InsertValue(int index, object value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, DataOps.ToCouchbaseObject(value));
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertString(int index, string value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertInt(int index, int value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertLong(int index, long value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertFloat(int index, float value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertDouble(int index, double value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertBoolean(int index, bool value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertBlob(int index, Blob value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertDate(int index, DateTimeOffset value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value.ToString("o"));
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertArray(int index, ArrayObject value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray InsertDictionary(int index, DictionaryObject value)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Insert(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray RemoveAt(int index)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.RemoveAt(index);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray Set(IList array)
        {
            return _threadSafety.DoLocked(() =>
            {
                _array.Clear();
                foreach (var item in array) {
                    _array.Add(DataOps.ToCouchbaseObject(item));
                }

                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetValue(int index, object value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetString(int index, string value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetInt(int index, int value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetLong(int index, long value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetFloat(int index, float value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetDouble(int index, double value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetBoolean(int index, bool value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetBlob(int index, Blob value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetDate(int index, DateTimeOffset value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetArray(int index, ArrayObject value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        /// <inheritdoc />
        public IMutableArray SetDictionary(int index, DictionaryObject value)
        {
            return _threadSafety.DoLocked(() =>
            {
                SetValueInternal(index, value);
                return this;
            });
        }

        #endregion
    }
}
