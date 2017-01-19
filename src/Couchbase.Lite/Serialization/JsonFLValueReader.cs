﻿//
// JsonFLValueReader.cs
//
// Author:
// 	Jim Borden  <jim.borden@couchbase.com>
//
// Copyright (c) 2015 Couchbase, Inc All rights reserved.
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
using Newtonsoft.Json;
using System.Collections.Generic;

using Couchbase.Lite.Logging;
using LiteCore;
using LiteCore.Interop;

namespace Couchbase.Lite.Serialization
{
    internal sealed unsafe class JsonFLValueReader : JsonReader
    {
        private const string Tag = nameof(JsonFLValueReader);
        private enum NextActionCode
        {
            ReadValue,
            ReadObjectKey,
            EndObject,
            EndArray
        }

        private Stack<object> _sequenceStack = new Stack<object>();
        private FLValue* _currentValue;
        private readonly SharedStringCache _stringCache;
        private bool _inValue;
        private bool _ended;

        public JsonFLValueReader(FLValue *root, SharedStringCache stringCache)
        {
            _currentValue = root;
            _stringCache = stringCache;
        }

        private void BeginObject(FLDict* d)
        {
            FLDictIterator i;
            Native.FLDictIterator_Begin(d, &i);
            _sequenceStack.Push(i);
        }

        private void BeginArray(FLArray* a)
        {
            FLArrayIterator i;
            Native.FLArrayIterator_Begin(a, &i);
            _sequenceStack.Push(i);
        }

        private NextActionCode NextAction()
        {
            if(_ended) {
                _ended = false;
                var last = _sequenceStack.Pop();
                return (last is FLDictIterator) ? NextActionCode.EndObject : NextActionCode.EndArray;
            }

            if(_sequenceStack.Count == 0) {
                return NextActionCode.ReadValue;
            }

            var i = _sequenceStack.Pop();
            if(i is FLDictIterator) {
                var iter = (FLDictIterator)i;
                if(_inValue) {
                    _inValue = false;
                    _currentValue = Native.FLDictIterator_GetValue(&iter);
                    if(!Native.FLDictIterator_Next(&iter)) {
                        _ended = true;
                    }

                    _sequenceStack.Push(iter);
                    return NextActionCode.ReadValue;
                } else {
                    _inValue = true;
                }

                _currentValue = Native.FLDictIterator_GetKey(&iter);
                return NextActionCode.ReadObjectKey;
            } else {
                var iter = (FLArrayIterator)i;
                _currentValue = Native.FLArrayIterator_GetValue(&iter);
                if(!Native.FLArrayIterator_Next(&iter)) {
                    _ended = true;
                }

                _sequenceStack.Push(iter);
                return NextActionCode.ReadValue;
            }
        }

        private string GetKey()
        {
            var key = default(string);
            if(Native.FLValue_GetType(_currentValue) == FLValueType.Number) {
                key = _stringCache.GetKey((int)Native.FLValue_AsInt(_currentValue));
                if(key == null) {
                    Log.To.Database.W(Tag, "Corrupt key found during deserialization, skipping...");
                }
            } else {
                key = Native.FLValue_AsString(_currentValue);
            }

            return key;
        }

        public override bool Read()
        {
            if(_sequenceStack.Count == 0 && _currentValue == null) {
                return false;
            }

            switch(NextAction()) {
                case NextActionCode.EndArray:
                    SetToken(JsonToken.EndArray);
                    break;
                case NextActionCode.EndObject:
                    SetToken(JsonToken.EndObject);
                    break;
                case NextActionCode.ReadObjectKey:
                    var key = GetKey();
                    if(key == null) {
                        return false;
                    }

                    SetToken(JsonToken.PropertyName, key);
                    break;
                case NextActionCode.ReadValue:
                    switch(Native.FLValue_GetType(_currentValue)) {
                        case FLValueType.Array:
                            BeginArray(Native.FLValue_AsArray(_currentValue));
                            SetToken(JsonToken.StartArray);
                            break;
                        case FLValueType.Boolean:
                            SetToken(JsonToken.Boolean, Native.FLValue_AsBool(_currentValue));
                            break;
                        case FLValueType.Dict:
                            BeginObject(Native.FLValue_AsDict(_currentValue));
                            SetToken(JsonToken.StartObject);
                            break;
                        case FLValueType.Null:
                            SetToken(JsonToken.Null, null);
                            break;
                        case FLValueType.Number:
                            if(Native.FLValue_IsInteger(_currentValue)) {
                                if(Native.FLValue_IsUnsigned(_currentValue)) {
                                    SetToken(JsonToken.Integer, Native.FLValue_AsUnsigned(_currentValue));
                                }

                                SetToken(JsonToken.Integer, Native.FLValue_AsInt(_currentValue));
                            } else if(Native.FLValue_IsDouble(_currentValue)) {
                                SetToken(JsonToken.Float, Native.FLValue_AsDouble(_currentValue));
                            }

                            SetToken(JsonToken.Float, Native.FLValue_AsFloat(_currentValue));
                            break;
                        case FLValueType.String:
                            SetToken(JsonToken.String, Native.FLValue_AsString(_currentValue));
                            break;
                        case FLValueType.Undefined:
                        default:
                            return false;
                    }
                    break;
                default:
                    return false;

            }

            return true;
        }

        public override decimal? ReadAsDecimal()
        {
            if(Native.FLValue_GetType(_currentValue) != FLValueType.Number) {
                return null;
            }

            if(Native.FLValue_IsInteger(_currentValue)) {
                if(Native.FLValue_IsUnsigned(_currentValue)) {
                    return Native.FLValue_AsUnsigned(_currentValue);
                }

                return Native.FLValue_AsInt(_currentValue);
            } else if(Native.FLValue_IsDouble(_currentValue)) {
                return (decimal)Native.FLValue_AsDouble(_currentValue);
            }

            return (decimal)Native.FLValue_AsFloat(_currentValue);
        }

        public override double? ReadAsDouble()
        {
            if(Native.FLValue_GetType(_currentValue) != FLValueType.Number 
                || Native.FLValue_IsInteger(_currentValue)) {
                return null;
            }

            if(Native.FLValue_IsDouble(_currentValue)) {
                return Native.FLValue_AsDouble(_currentValue);
            }

            return Native.FLValue_AsFloat(_currentValue);
        }

        public override int? ReadAsInt32()
        {
            if(Native.FLValue_GetType(_currentValue) != FLValueType.Number
                || !Native.FLValue_IsInteger(_currentValue)) {
                return null;
            }

            if(Native.FLValue_IsUnsigned(_currentValue)) {
                return (int)Native.FLValue_AsUnsigned(_currentValue);
            }

            return (int)Native.FLValue_AsInt(_currentValue);
        }

        public override bool? ReadAsBoolean()
        {
            if(Native.FLValue_GetType(_currentValue) != FLValueType.Boolean) {
                return null;
            }

            return Native.FLValue_AsBool(_currentValue);
        }

        public override string ReadAsString()
        {
            if(Native.FLValue_GetType(_currentValue) != FLValueType.String) {
                return null;
            }

            return Native.FLValue_AsString(_currentValue);
        }

        public override byte[] ReadAsBytes()
        {
            if(Native.FLValue_GetType(_currentValue) != FLValueType.Data) {
                return null;
            }

            return Native.FLValue_AsData(_currentValue);
        }

        public override DateTime? ReadAsDateTime()
        {
            return null;
        }

        public override DateTimeOffset? ReadAsDateTimeOffset()
        {
            return null;
        }
    }
}