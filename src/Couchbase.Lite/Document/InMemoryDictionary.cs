﻿// 
// InMemoryDictionary.cs
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
using System.Linq;
using Couchbase.Lite.Util;
using LiteCore.Interop;

namespace Couchbase.Lite.Internal.Doc
{
    internal sealed class InMemoryDictionary : IDictionaryObject, IFLEncodable
    {
        private IDictionary<string, object> _dict;
       
        public bool HasChanges { get; private set; }

        public InMemoryDictionary()
        {
            _dict = new Dictionary<string, object>();
        }

        public InMemoryDictionary(IDictionary<string, object> dictionary)
        {
            _dict = new Dictionary<string, object>(dictionary);
            HasChanges = _dict.Count > 0;
        }

        public InMemoryDictionary(InMemoryDictionary other)
            : this(other._dict)
        {
            
        }

        private void SetObject(string key, object value)
        {
            value = DataOps.ToCouchbaseObject(value);
            var oldValue = _dict.Get(key);
            if (!_dict.ContainsKey(key) || (value != oldValue && value?.Equals(oldValue) != true)) {
                _dict[key] = value;
                HasChanges = true;
            }
        }

        ReadOnlyFragment IReadOnlyDictionaryFragment.this[string key] => new ReadOnlyFragment(this, key);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _dict.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => _dict.Count;

        public ICollection<string> Keys => _dict.Keys;

        public bool Contains(string key)
        {
            return _dict.ContainsKey(key);
        }

        public IArray GetArray(string key)
        {
            return GetObject(key) as IArray;
        }

        public IDictionaryObject GetDictionary(string key)
        {
            return GetObject(key) as IDictionaryObject;
        }

        public IDictionaryObject Remove(string key)
        {
            if (_dict.ContainsKey(key)) {
                _dict.Remove(key);
                HasChanges = true;
            }

            return this;
        }

        public IDictionaryObject Set(string key, object value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(IDictionary<string, object> dictionary)
        {
            _dict = dictionary.ToDictionary(x => x.Key, x => DataOps.ToCouchbaseObject(x.Value));
            HasChanges = true;
            return this;
        }

        public IDictionaryObject Set(string key, string value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, int value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, long value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, float value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, double value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, bool value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, Blob value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, DateTimeOffset value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, ArrayObject value)
        {
            SetObject(key, value);
            return this;
        }

        public IDictionaryObject Set(string key, DictionaryObject value)
        {
            SetObject(key, value);
            return this;
        }

        public Fragment this[string key] => new Fragment(this, key);

        IReadOnlyArray IReadOnlyDictionary.GetArray(string key)
        {
            return GetObject(key) as IReadOnlyArray;
        }

        public Blob GetBlob(string key)
        {
             return GetObject(key) as Blob;
        }

        public bool GetBoolean(string key)
        {
            return DataOps.ConvertToBoolean(GetObject(key));
        }

        public DateTimeOffset GetDate(string key)
        {
            return DataOps.ConvertToDate(GetObject(key));
        }

        IReadOnlyDictionary IReadOnlyDictionary.GetDictionary(string key)
        {
            return GetObject(key) as IReadOnlyDictionary;
        }

        public double GetDouble(string key)
        {
            return DataOps.ConvertToDouble(GetObject(key));
        }

        public float GetFloat(string key)
        {
            return DataOps.ConvertToFloat(GetObject(key));
        }

        public int GetInt(string key)
        {
            return DataOps.ConvertToInt(GetObject(key));
        }

        public long GetLong(string key)
        {
            return DataOps.ConvertToLong(GetObject(key));
        }

        public object GetObject(string key)
        {
            object obj;
            if (!_dict.TryGetValue(key, out obj)) {
                return null;
            }

            var cblObj = DataOps.ToCouchbaseObject(obj);
            if (cblObj != obj && cblObj?.GetType() != obj.GetType()) {
                _dict[key] = cblObj;
            }

            return cblObj;
        }

        public string GetString(string key)
        {
            return GetObject(key) as string;
        }

        public IDictionary<string, object> ToDictionary()
        {
            return _dict.ToDictionary(x => x.Key, x => DataOps.ToNetObject(x.Value));
        }

        public unsafe void FLEncode(FLEncoder* enc)
        {
            _dict.FLEncode(enc);
        }
    }
}