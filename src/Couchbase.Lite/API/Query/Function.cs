﻿// 
// QueryFunction.cs
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

namespace Couchbase.Lite.Query
{
    /// <summary>
    /// A class for creating <see cref="IFunction"/> instances
    /// </summary>
    public static class Function
    {
        #region Public Methods

        /// <summary>
        /// Creates a function that will get the absolute value of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the absolute value of the expression in question</returns>
        public static IFunction Abs(object expression) => new QueryFunction("ABS()", expression);

        /// <summary>
        /// Creates a function that will get the inverse cosine of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the inverse cosine of the expression in question</returns>
        public static IFunction Acos(object expression) => new QueryFunction("ACOS()", expression);

        /// <summary>
        /// Creates a function that will get the inverse sin of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the inverse sin of the expression in question</returns>
        public static IFunction Asin(object expression) => new QueryFunction("ASIN()", expression);

        /// <summary>
        /// Creates a function that will get the inverse tangent of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the  inverse tangent of the expression in question</returns>
        public static IFunction Atan(object expression) => new QueryFunction("ATAN()", expression);

        /// <summary>
        /// Creates a function that will get the arctangent of the point expressed by
        /// expressions calculating X and Y of the point for the formula
        /// </summary>
        /// <param name="expressionX">An expression or literal to evaluate to get the X coordinate to use</param>
        /// <param name="expressionY">An expression or literal to evaluate to get the Y coordinate to use</param>
        /// <returns>A function that will get the arctangent of the point in question</returns>
        public static IFunction Atan2(object expressionX, object expressionY) => new QueryFunction("ATAN2()", expressionX, expressionY);

        /// <summary>
        /// Creates a function that will calculate the average of the
        /// expression in question across the results in a particular query
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will calculate the average</returns>
        public static IFunction Avg(object expression) => new QueryFunction("AVG()", expression);

        /// <summary>
        /// Creates a function that will get the ceiling value of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the ceiling value of the expression in question</returns>
        public static IFunction Ceil(object expression) => new QueryFunction("CEIL()", expression);

        /// <summary>
        /// Creates a function that will calculate if a given string is inside of another
        /// in question
        /// </summary>
        /// <param name="str">The string or expression that evaluates to a string to search</param>
        /// <param name="item">The string or expression that evaluates to a string to search for</param>
        /// <returns>A function that will return true if the string contains the other, or false if it does not</returns>
        public static IFunction Contains(object str, object item) => new QueryFunction("CONTAINS()", str, item);

        /// <summary>
        /// Creates a function that will get the cosine of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the cosine of the expression in question</returns>
        public static IFunction Cos(object expression) => new QueryFunction("COS()", expression);

        /// <summary>
        /// Creates a function that will count the occurrences of 
        /// expression in question across the results in a particular query
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will calculate the count</returns>
        public static IFunction Count(object expression) => new QueryFunction("COUNT()", expression);

        /// <summary>
        /// Creates a function that will convert a numeric expression to degrees from radians
        /// in question
        /// </summary>
        /// <param name="expression">The numeric expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the value of the expression in question expressed in degrees</returns>
        public static IFunction Degrees(object expression) => new QueryFunction("DEGREES()", expression);

        /// <summary>
        /// Creates a function that will return the value of the mathemetical constant 'e'
        /// </summary>
        /// <returns>The value of 'e'</returns>
        public static IFunction E() => new QueryFunction("E()");

        /// <summary>
        /// Returns the mathematical constant 'e' raised to the given power
        /// </summary>
        /// <param name="expression">The numerical expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the mathematical constant 'e' raised to the given power</returns>
        public static IFunction Exp(object expression) => new QueryFunction("EXP()", expression);

        /// <summary>
        /// Creates a function that will get the floor value of the expression
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the floor value of the expression in question</returns>
        public static IFunction Floor(object expression) => new QueryFunction("FLOOR()", expression);

        /// <summary>
        /// Creates a function that checks if the given expression is an array type
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will check if the given expression is an array type</returns>
        public static IFunction IsArray(object expression) => new QueryFunction("ISARRAY()", expression);

        /// <summary>
        /// Creates a function that checks if the given expression is a numeric type
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will check if the given expression is a numeric type</returns>
        public static IFunction IsNumber(object expression) => new QueryFunction("ISNUMBER()", expression);

        /// <summary>
        /// Creates a function that checks if the given expression is a dictionary (i.e. JSON Object) type
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will check if the given expression is a dictionary type</returns>
        public static IFunction IsDictionary(object expression) => new QueryFunction("ISOBJECT()", expression);

        /// <summary>
        /// Creates a function that checks if the given expression is a string type
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will check if the given expression is a string type</returns>
        public static IFunction IsString(object expression) => new QueryFunction("ISSTRING()", expression);

        /// <summary>
        /// Creates a function that gets the length of a string
        /// in question
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result (must be or evaluate to a string)</param>
        /// <returns>The length of the string in question</returns>
        public static IFunction Length(object expression) => new QueryFunction("LENGTH()", expression);

        /// <summary>
        /// Creates a function that gets the natural log of the numerical expression
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that gets the natural log of the expression</returns>
        public static IFunction Ln(object expression) => new QueryFunction("LN()", expression);

        /// <summary>
        /// Creates a function that gets the base 10 log of the numerical expression
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that gets the base 10 log of the expression</returns>
        public static IFunction Log(object expression) => new QueryFunction("LOG()", expression);

        /// <summary>
        /// Creates a function that converts a string to lower case
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that converts a string to lower case</returns>
        public static IFunction Lower(object expression) => new QueryFunction("LOWER()", expression);

        /// <summary>
        /// Creates a function that removes whitespace from the beginning of a string
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that removes whitespace from the beginning of a string</returns>
        public static IFunction Ltrim(object expression) => new QueryFunction("LTRIM()", expression);

        /// <summary>
        /// Creates a function that will calculate the max value of the
        /// expression in question across the results in a particular query
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will calculate the max value</returns>
        public static IFunction Max(object expression) => new QueryFunction("MAX()", expression);

        /// <summary>
        /// Creates a function that will calculate the min value of the
        /// expression in question across the results in a particular query
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will calculate the min value</returns>
        public static IFunction Min(object expression) => new QueryFunction("MIN()", expression);

        /// <summary>
        /// Creates a function that will return the value of the mathemetical constant 'π'
        /// </summary>
        /// <returns>The value of 'π'</returns>
        public static IFunction Pi() => new QueryFunction("PI()");

        /// <summary>
        /// Creates a function that will raise the given numeric expression
        /// to an expression that determines the exponent
        /// </summary>
        /// <param name="b">A numeric literal or expression that provides the base</param>
        /// <param name="exponent">A numeric literal or expression that provides the exponent</param>
        /// <returns>A function that will raise the base to the given exponent</returns>
        public static IFunction Power(object b, object exponent) => new QueryFunction("POWER()", b, exponent);

        /// <summary>
        /// Creates a function that will convert a numeric expression to radians from degrees
        /// in question
        /// </summary>
        /// <param name="expression">The numeric expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will get the value of the expression in question expressed in radians</returns>
        public static IFunction Radians(object expression) => new QueryFunction("RADIANS()", expression);

        /// <summary>
        /// Creates a function that will round the given expression
        /// in question
        /// </summary>
        /// <param name="expression">The numeric expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will round the expression (using midpoint rounding)</returns>
        public static IFunction Round(object expression) => new QueryFunction("ROUND()", expression);

        /// <summary>
        /// Creates a function that will round the given expression to the number of digits indicated
        /// in question
        /// </summary>
        /// <param name="expression">The numeric expression to take data from when calculating
        /// the result</param>
        /// <param name="digits">The number of digits to round to</param>
        /// <returns>A function that will round the expression (using midpoint rounding)</returns>
        public static IFunction Round(object expression, int digits) => new QueryFunction("ROUND()", expression, digits);

        /// <summary>
        /// Creates a function that removes whitespace from the end of a string
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that removes whitespace from the end of a string</returns>
        public static IFunction Rtrim(object expression) => new QueryFunction("RTRIM()", expression);

        /// <summary>
        /// Creates a function that returns the sign (positive, negative, or neither) of
        /// the expression in question
        /// </summary>
        /// <param name="expression">The numeric expression to evaluate</param>
        /// <returns>A function that returns the sign of the expression in question</returns>
        public static IFunction Sign(object expression) => new QueryFunction("SIGN()", expression);

        /// <summary>
        /// Creates a function that returns the sin of the expression in question
        /// </summary>
        /// <param name="expression">The numeric expression to evaluate</param>
        /// <returns>A function that returns the sin of the expression in question</returns>
        public static IFunction Sin(object expression) => new QueryFunction("SIN()", expression);

        /// <summary>
        /// Creates a function that returns the square root of the expression in question
        /// </summary>
        /// <param name="expression">The numeric expression to evaluate</param>
        /// <returns>A function that returns the square root of the expression in question</returns>
        public static IFunction Sqrt(object expression) => new QueryFunction("SQRT()", expression);

        /// <summary>
        /// Creates a function that will calculate the sum of the
        /// expression in question across the results in a particular query
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will calculate the sum</returns>
        public static IFunction Sum(object expression) => new QueryFunction("SUM()", expression);

        /// <summary>
        /// Creates a function that returns the tangent of the expression in question
        /// </summary>
        /// <param name="expression">The numeric expression to evaluate</param>
        /// <returns>A function that returns the tangent of the expression in question</returns>
        public static IFunction Tan(object expression) => new QueryFunction("TAN()", expression);

        /// <summary>
        /// Creates a function that removes whitespace from the start and end of a string
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that removes whitespace from the start and end of a string</returns>
        public static IFunction Trim(object expression) => new QueryFunction("TRIM()", expression);

        /// <summary>
        /// Creates a function that will truncate the given expression (i.e remove all the
        /// digits after the decimal place)
        /// in question
        /// </summary>
        /// <param name="expression">The numeric expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that will truncate the expressoin</returns>
        public static IFunction Trunc(object expression) => new QueryFunction("TRUNC()", expression);

        /// <summary>
        /// Creates a function that will truncate the given expression to the number of digits indicated
        /// in question
        /// </summary>
        /// <param name="expression">The numeric expression to take data from when calculating 
        /// the result</param>
        /// <param name="digits">The number of digits to truncate to</param>
        /// <returns>A function that will truncate the expression</returns>
        public static IFunction Trunc(object expression, int digits) => new QueryFunction("TRUNC()", expression, digits);

        /// <summary>
        /// Creates a function that converts a string to upper case
        /// </summary>
        /// <param name="expression">The expression to take data from when calculating
        /// the result</param>
        /// <returns>A function that converts a string to upper case</returns>
        public static IFunction Upper(object expression) => new QueryFunction("UPPER()", expression);

        #endregion
    }
}
