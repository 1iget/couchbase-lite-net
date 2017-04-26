﻿// 
// ReadOnlyFragment.cs
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
using Couchbase.Lite.Internal.Doc;

namespace Couchbase.Lite
{
    public class ReadOnlyFragment
    {
        #region Properties

        public virtual bool Exists => Value != null;

        public ReadOnlyFragment this[int index]
        {
            get {
                if (Value is IReadOnlyArray a) {
                    return a[index];
                }

                return new ReadOnlyFragment(null);
            }
        }

        public ReadOnlyFragment this[string key]
        {
            get {
                if (Value is IReadOnlyDictionary d) {
                    return d[key];
                }

                return new ReadOnlyFragment(null);
            }
        }

        public object Value { get; }

        #endregion

        #region Constructors

        internal ReadOnlyFragment(object value)
        {
            Value = value;
        }

        #endregion

        #region Public Methods

        public IReadOnlyArray ToArray()
        {
            return Value as IReadOnlyArray;
        }

        public Blob ToBlob()
        {
            return Value as Blob;
        }

        public bool ToBoolean()
        {
            try {
                return Convert.ToBoolean(Value);
            } catch (InvalidCastException) {
                return false;
            }
        }

        public DateTimeOffset ToDate()
        {
            if (Value is DateTimeOffset dto) {
                return dto;
            }

            return DateTimeOffset.MinValue;
        }

        public double ToDouble()
        {
            try {
                return Convert.ToDouble(Value);
            } catch (InvalidCastException) {
                return 0;
            }
        }

        public int ToInt()
        {
            try {
                return Convert.ToInt32(Value);
            } catch (InvalidCastException) {
                return 0;
            }
        }

        public long ToLong()
        {
            try {
                return Convert.ToInt64(Value);
            } catch (InvalidCastException) {
                return 0;
            }
        }

        public object ToObject()
        {
            return Value;
        }

        public ReadOnlySubdocument ToSubdocument()
        {
            return Value as ReadOnlySubdocument;
        }

        #endregion

        #region Overrides

        public override string ToString()
        {
            return Value as string;
        }

        #endregion
    }
}
