﻿// 
// QueryJoin.cs
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
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite.Query;

namespace Couchbase.Lite.Internal.Query
{
    internal sealed class QueryJoin : LimitedQuery, IJoinOn
    {
        #region Properties

        private readonly IList<IJoin> _joins;
        private readonly string _joinType;
        private readonly IDataSource _source;
        private IExpression _on;

        #endregion

        #region Constructors

        internal QueryJoin(IList<IJoin> joins)
        {
            _joins = joins;
            JoinImpl = this;
        }

        internal QueryJoin(XQuery source, IList<IJoin> joins)
        {
            Copy(source);
            _joins = joins; 
            JoinImpl = this;
        }

        internal QueryJoin(string joinType, IDataSource dataSource)
        {
            _joinType = joinType;
            _source = dataSource;
        }

        #endregion

        public object ToJSON()
        {
            if (_joins != null) {
                var obj = new List<object>();
                foreach (var o in _joins.OfType<QueryJoin>()) {
                    obj.Add(o.ToJSON());
                }

                return obj;
            }

            var asObj = (_source as QueryDataSource)?.ToJSON() as Dictionary<string, object>;
            if (asObj == null) {
                throw new InvalidOperationException("Missing AS clause for JOIN");
            }

            var onObj = _on as QueryExpression;
            asObj["ON"] = onObj?.ConvertToJSON() ?? throw new InvalidOperationException("Missing ON statement for JOIN");
            if (_joinType != null) {
                asObj["JOIN"] = _joinType;
            }

            return asObj;
        }

        #region IJoinOn

        public IJoin On(IExpression expression)
        {
            _on = expression;
            return this;
        }

        #endregion

        #region IOrderByRouter

        public IOrdering OrderBy(params IOrdering[] ordering)
        {
            return new QueryOrdering(this, ordering);
        }

        #endregion

        #region IWhereRouter

        public IWhere Where(IExpression expression)
        {
            return new Where(this, expression);
        }

        #endregion
    }
}
