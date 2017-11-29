﻿// 
// QueryIndex.cs
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
using System.Linq;
using Couchbase.Lite.Query;
using LiteCore.Interop;

namespace Couchbase.Lite.Internal.Query
{
    internal sealed class QueryIndex : IValueIndex, IFTSIndex
    {
        #region Variables

        private readonly IFTSIndexItem[] _ftsItems;
        private bool _ignoreAccents;
        private string _locale;
        private readonly IValueIndexItem[] _valueItems;

        #endregion

        #region Properties

        internal C4IndexType IndexType => _ftsItems != null ? C4IndexType.FullTextIndex : C4IndexType.ValueIndex;

        internal C4IndexOptions Options
        {
            get {
                if (_ftsItems != null) {
                    return new C4IndexOptions {
                        ignoreDiacritics = _ignoreAccents,
                        language = _locale
                    };
                }

                return new C4IndexOptions();
            }
        }

        #endregion

        #region Internal Methods

        internal object ToJSON()
        {
            object jsonObj;
            if (_ftsItems != null) {
                jsonObj = QueryExpression.EncodeToJSON(_ftsItems.OfType<QueryIndexItem>().Select(x => x.Expression)
                    .ToList());
            } else {
                jsonObj = QueryExpression.EncodeToJSON(_valueItems.OfType<QueryIndexItem>().Select(x => x.Expression)
                    .ToList());
            }

            return jsonObj;
        }

        #endregion

        public QueryIndex(params IFTSIndexItem[] items)
        {
            _ftsItems = items;
        }

        public QueryIndex(params IValueIndexItem[] items)
        {
            _valueItems = items;
        }

        #region IFTSIndex

        public IFTSIndex IgnoreAccents(bool ignoreAccents)
        {
            _ignoreAccents = ignoreAccents;
            return this;
        }

        public IFTSIndex SetLocale(string localeCode)
        {
            _locale = localeCode;
            return this;
        }

        #endregion
    }
}