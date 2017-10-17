﻿// 
// DataOps.cs
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
using System.Globalization;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json.Linq;

namespace Couchbase.Lite.Internal.Doc
{
    internal static class DataOps
    {
        #region Constants

        private static readonly TypeInfo[] ValidTypes = {
            typeof(string).GetTypeInfo(),
            typeof(DateTimeOffset).GetTypeInfo(),
            typeof(Blob).GetTypeInfo(),
            typeof(IReadOnlyArray).GetTypeInfo(),
            typeof(IReadOnlyDictionary).GetTypeInfo(),
            typeof(IDictionary<string,object>).GetTypeInfo(),
            typeof(IList<>).GetTypeInfo()
        };

        #endregion

        #region Internal Methods

        internal static DictionaryObject ConvertDictionary(IDictionary<string, object> dictionary)
        {
            var subdocument = new DictionaryObject();
            subdocument.Set(dictionary);
            return subdocument;
        }

        internal static ArrayObject ConvertList(IList list)
        {
            var array = new ArrayObject();
            array.Set(list);
            return array;
        }

        internal static ArrayObject ConvertROArray(ReadOnlyArray readOnlyArray)
        {
            var array = new ArrayObject(readOnlyArray.Data);
            return array;
        }

        internal static DictionaryObject ConvertRODictionary(ReadOnlyDictionary readOnlySubdoc)
        {
            var subdocument = new DictionaryObject(readOnlySubdoc.Data);
            return subdocument;
        }

        internal static bool ConvertToBoolean(object value)
        {
            switch (value) {
                case null:
                    return false;
                case string s:
                    return true; // string is IConvertible, but will throw on things other than true or false
                case IConvertible c:
                    return c.ToBoolean(CultureInfo.InvariantCulture);
                default:
                    return !ReferenceEquals(value, DictionaryObject.RemovedValue);
            }
        }

        internal static DateTimeOffset ConvertToDate(object value)
        {
            switch (value) {
                case null:
                    return DateTimeOffset.MinValue;
                case DateTimeOffset dto:
                    return dto;
                case DateTime dt:
                    return new DateTimeOffset(dt.ToUniversalTime());
                case string s:
                    if (DateTimeOffset.TryParseExact(s, "o", CultureInfo.InvariantCulture, DateTimeStyles.None,
                        out var retVal)) {
                        return retVal;
                    }

                    return DateTimeOffset.MinValue;
                default:
                    return DateTimeOffset.MinValue;
            }
        }

        internal static double ConvertToDouble(object value)
        {
            // NOTE: Cannot use ConvertToDecimal because double has a greater range
            switch (value) {
                case string s: // string is IConvertible, but will throw for non-numeric strings
                    return 0.0;
                case IConvertible c:
                    return c.ToDouble(CultureInfo.InvariantCulture);
                default:
                    return 0.0;
            }
        }
        internal static float ConvertToFloat(object value)
        {
            // NOTE: Cannot use ConvertToDecimal because float has a greater range
            switch (value) {
                case string s: // string is IConvertible, but will throw for non-numeric strings
                    return 0.0f;
                case IConvertible c:
                    return c.ToSingle(CultureInfo.InvariantCulture);
                default:
                    return 0.0f;
            }
        }

        internal static int ConvertToInt(object value)
        {
            return (int)Math.Truncate(ConvertToDecimal(value));
        }

        internal static long ConvertToLong(object value)
        {
            return (long)Math.Truncate(ConvertToDecimal(value));
        }

        internal static object ConvertValue(object value)
        {
            switch (value) {
                case null:
                    return null;
                case DateTimeOffset dto:
                    return dto.ToString("o");
                case ReadOnlyDictionary rosubdoc when !(rosubdoc is DictionaryObject):
                    return ConvertRODictionary(rosubdoc);
                case ReadOnlyArray roarr when !(roarr is ArrayObject):
                    return ConvertROArray(roarr);
                case JObject jobj:
                    return ConvertDictionary(jobj.ToObject<IDictionary<string, object>>());
                case JArray jarr:
                    return ConvertList(jarr.ToObject<IList>());
                case IDictionary<string, object> dict:
                    return ConvertDictionary(dict);
                case IList list:
                    return ConvertList(list);
                default:
                    return value;
            }
        }

        #endregion

        #region Private Methods

        private static decimal ConvertToDecimal(object value)
        {
            switch (value) {
                case string s: // string is IConvertible, but will throw for non-numeric strings
                    return 0;
                case IConvertible c:
                    return c.ToDecimal(CultureInfo.InvariantCulture);
                default:
                    return 0;
            }
        }

        #endregion
    }
}
