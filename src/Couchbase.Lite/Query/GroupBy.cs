﻿// 
// GroupBy.cs
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
    internal sealed class GroupBy : LimitedQuery, IGroupBy
    {
        #region Variables

        private readonly IExpression _expression;
        private readonly IList<IGroupBy> _groupings;

        #endregion

        #region Constructors

        internal GroupBy(IList<IGroupBy> groupBy)
        {
            _groupings = groupBy;
            GroupByImpl = this;
        }

        internal GroupBy(XQuery query, IList<IGroupBy> groupBy)
            : this(groupBy)
        {
            Copy(query);
            GroupByImpl = this;
        }

        internal GroupBy(IExpression expression)
        {
            _expression = expression;
            GroupByImpl = this;
        }

        #endregion

        #region Public Methods

        public object ToJSON()
        {
            var exp = _expression as QueryExpression;
            if (exp != null) {
                return exp.ConvertToJSON();
            }

            var obj = new List<object>();
            foreach (var o in _groupings.OfType<GroupBy>()) {
                obj.Add(o.ToJSON());
            }

            return obj;
        }

        #endregion

        #region IHavingRouter

        public IHaving Having(IExpression expression)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region IOrderByRouter

        public IOrderBy OrderBy(params IOrderBy[] orderBy)
        {
            return new OrderBy(this, orderBy);
        }

        #endregion
    }
}
