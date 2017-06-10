﻿// 
// ArrayObject.cs
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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Couchbase.Lite.Internal.Doc;
using Couchbase.Lite.Support;
using Newtonsoft.Json;

namespace Couchbase.Lite
{
    internal sealed class ArrayObjectConverter : JsonConverter
    {
        #region Properties

        public override bool CanRead => false;

        public override bool CanWrite => true;

        #endregion

        #region Overrides

        public override bool CanConvert(Type objectType)
        {
            return objectType.GetTypeInfo().IsAssignableFrom(typeof(ArrayObject).GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var arr = (ArrayObject)value;
            arr.LockedForRead(() =>
            {
                writer.WriteStartArray();
                foreach (var obj in arr) {
                    serializer.Serialize(writer, obj);
                }
                writer.WriteEndArray();
            });
        }

        #endregion
    }

    /// <summary>
    /// A class representing an editable collection of objects
    /// </summary>
    [JsonConverter(typeof(ArrayObjectConverter))]
    public sealed class ArrayObject : ReadOnlyArray, IArray
    {
        #region Variables

        private readonly ThreadSafety _changedSafety = new ThreadSafety(false);
        private readonly ThreadSafety _threadSafety = new ThreadSafety(true);

        internal event EventHandler<ObjectChangedEventArgs<ArrayObject>> Changed;
        private bool _changed;
        private IList _list;

        #endregion

        #region Properties

        /// <inheritdoc />
        public override int Count
        {
            get {
                return _threadSafety.LockedForRead(() =>
                {
                    if (_list == null) {
                        return base.Count;
                    }

                    return _list.Count;
                });
            }
        }

        /// <inheritdoc />
        public new Fragment this[int index]
        {
            get {
                var value = index >= 0 && index < Count ? GetObject(index) : null;
                return new Fragment(value, this, index);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Default Constructor
        /// </summary>
        public ArrayObject()
            : base(default(FleeceArray))
        {
            
        }

        /// <summary>
        /// Creates an array with the given data
        /// </summary>
        /// <param name="array">The data to populate the array with</param>
        public ArrayObject(IList array)
            : this()
        {
            Set(array);
        }

        internal ArrayObject(FleeceArray data)
            : base(data)
        {
            
        }

        #endregion

        #region Internal Methods

        internal void LockedForRead(Action a)
        {
            _threadSafety.LockedForRead(a);
        }

        #endregion

        #region Private Methods

        private void CopyFleeceData()
        {
            Debug.Assert(_list == null);
            var count = base.Count;
            _list = new List<object>(count);
            for (int i = 0; i < count; i++) {
                var value = base.GetObject(i);
                _list.Add(DataOps.ConvertValue(value, ObjectChanged, ObjectChanged));
            }
        }

        private void ObjectChanged(object sender, ObjectChangedEventArgs<DictionaryObject> args)
        {
            SetChanged();
        }

        private void ObjectChanged(object sender, ObjectChangedEventArgs<ArrayObject> args)
        {
            SetChanged();
        }

        private void RemoveAllChangedListeners()
        {
            if (_list == null) {
                return;
            }

            foreach (var obj in _list) {
                RemoveChangedListener(obj);
            }
        }

        private void RemoveChangedListener(object value)
        {
            switch (value) {
                case DictionaryObject subdoc:
                    subdoc.Changed -= ObjectChanged;
                    break;
                case ArrayObject array:
                    array.Changed -= ObjectChanged;
                    break;
            }
        }

        private void SetChanged()
        {
            _changedSafety.LockedForWrite(() =>
            {
                if (!_changed) {
                    _changed = true;
                    Changed?.Invoke(this, new ObjectChangedEventArgs<ArrayObject>(this));
                }
            });
        }

        private void SetValue(int index, object value, bool isChange)
        {
            _list[index] = value;
            if (isChange) {
                SetChanged();
            }
        }

        #endregion

        #region Overrides

        /// <inheritdoc />
        public override Blob GetBlob(int index)
        {
            return GetObject(index) as Blob;
        }

        /// <inheritdoc />
        public override bool GetBoolean(int index)
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    return base.GetBoolean(index);
                }

                var value = _list[index];
                return DataOps.ConvertToBoolean(value);
            });
        }

        /// <inheritdoc />
        public override DateTimeOffset GetDate(int index)
        {
            return DataOps.ConvertToDate(GetObject(index));
        }

        /// <inheritdoc />
        public override double GetDouble(int index)
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    return base.GetDouble(index);
                }

                var value = _list[index];
                return DataOps.ConvertToDouble(value);
            });
        }

        /// <inheritdoc />
        public override IEnumerator<object> GetEnumerator()
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    return base.GetEnumerator();
                }

                return _list.Cast<object>().GetEnumerator();
            });
        }

        /// <inheritdoc />
        public override int GetInt(int index)
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    return base.GetInt(index);
                }

                var value = _list[index];
                return DataOps.ConvertToInt(value);
            });
        }

        /// <inheritdoc />
        public override long GetLong(int index)
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    return base.GetLong(index);
                }

                var value = _list[index];
                return DataOps.ConvertToLong(value);
            });
        }

        /// <inheritdoc />
        public override object GetObject(int index)
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    var value = base.GetObject(index);
                    if (value is IReadOnlyDictionary || value is IReadOnlyArray) {
                        CopyFleeceData();
                    } else {
                        return value;
                    }
                }
                return _list[index];
            });
        }

        /// <inheritdoc />
        public override string GetString(int index)
        {
            return GetObject(index) as string;
        }

        /// <inheritdoc />
        public override IList<object> ToList()
        {
            return _threadSafety.LockedForRead(() =>
            {
                if (_list == null) {
                    CopyFleeceData();
                }

                var array = new List<object>();
                foreach (var item in _list) {
                    switch (item) {
                        case IReadOnlyDictionary dict:
                            array.Add(dict.ToDictionary());
                            break;
                        case IReadOnlyArray arr:
                            array.Add(arr.ToList());
                            break;
                        default:
                            array.Add(item);
                            break;
                    }
                }

                return array;
            });
        }

        #endregion

        #region IArray

        /// <inheritdoc />
        public IArray Add(object value)
        {
            return _threadSafety.LockedForWrite(() =>
            {
                if (_list == null) {
                    CopyFleeceData();
                }

                _list.Add(DataOps.ConvertValue(value, ObjectChanged, ObjectChanged));
                SetChanged();
                return this;
            });
        }

        /// <inheritdoc />
        public new IArray GetArray(int index)
        {
            return GetObject(index) as IArray;
        }

        /// <inheritdoc />
        public new IDictionaryObject GetDictionary(int index)
        {
            return GetObject(index) as IDictionaryObject;
        }

        /// <inheritdoc />
        public IArray Insert(int index, object value)
        {
            return _threadSafety.LockedForWrite(() =>
            {
                if (_list == null) {
                    CopyFleeceData();
                }

                _list.Insert(index, DataOps.ConvertValue(value, ObjectChanged, ObjectChanged));
                SetChanged();
                return this;
            });
        }

        /// <inheritdoc />
        public IArray RemoveAt(int index)
        {
            return _threadSafety.LockedForWrite(() =>
            {
                if (_list == null) {
                    CopyFleeceData();
                }

                var value = _list[index];
                RemoveChangedListener(value);
                _list.RemoveAt(index);
                SetChanged();
                return this;
            });
        }

        /// <inheritdoc />
        public IArray Set(IList array)
        {
            return _threadSafety.LockedForWrite(() =>
            {
                RemoveAllChangedListeners();

                var result = new List<object>();
                foreach (var item in array) {
                    result.Add(DataOps.ConvertValue(item, ObjectChanged, ObjectChanged));
                }

                _list = result;
                SetChanged();
                return this;
            });
        }

        /// <inheritdoc />
        public IArray Set(int index, object value)
        {
            return _threadSafety.LockedForWrite(() =>
            {
                if (_list == null) {
                    CopyFleeceData();
                }

                var oldValue = _list[index];
                if (value?.Equals(oldValue) == false) {
                    value = DataOps.ConvertValue(value, ObjectChanged, ObjectChanged);
                    RemoveChangedListener(oldValue);
                    SetValue(index, value, true);
                }

                return this;
            });
        }

        #endregion
    }
}
