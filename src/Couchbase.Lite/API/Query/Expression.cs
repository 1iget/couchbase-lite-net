﻿// 
// Expression.cs
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

using Couchbase.Lite.Internal.Query;

using JetBrains.Annotations;

namespace Couchbase.Lite.Query
{
    /// <summary>
    /// A factory for unary IExpression operators
    /// </summary>
    public static class Expression
    {
        #region Public Methods

        /// <summary>
        /// Returns an expression to represent '*' in things like COUNT(*) and
        /// SELECT *
        /// </summary>
        /// <returns></returns>
        public static IPropertyExpression All() => new QueryTypeExpression("", ExpressionType.KeyPath);

        /// <summary>
        /// Returns an expression representing the negated result of an expression
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The negated result of the expression</returns>
        [NotNull]
        public static IExpression Negated(IExpression expression) => new QueryCompoundExpression("NOT", expression);

        /// <summary>
        /// Returns an expression representing the negated result of an expression
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The negated result of the expression</returns>
        [NotNull]
        public static IExpression Not(IExpression expression) => Negated(expression);

        /// <summary>
        /// Gets an expression representing a named parameter (as set in
        /// <see cref="IQuery.Parameters"/>) for use in a query
        /// </summary>
        /// <param name="name">The name of the parameter in the parameter set</param>
        /// <returns>The expression representing the parameter</returns>
        [NotNull]
        public static IExpression Parameter(string name) => new QueryTypeExpression(name, ExpressionType.Parameter);

        /// <summary>
        /// Returns an expression representing the value of a named property
        /// </summary>
        /// <param name="property">The name of the property to fetch</param>
        /// <returns>An expression representing the value of a named property</returns>
        [NotNull]
        public static IPropertyExpression Property(string property) => new QueryTypeExpression(property, ExpressionType.KeyPath);

        #endregion
    }
}
