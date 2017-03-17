﻿// 
// IThreadSafe.cs
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
using System.Threading.Tasks;

namespace Couchbase.Lite
{
    /// <summary>
    /// An interface for an object that guarantees thread safety via
    /// the use of dispatch queues
    /// </summary>
    public interface IThreadSafe
    {
        #region Properties

        /// <summary>
        /// Gets the queue that is used for scheduling operations on
        /// the object.  If operations are performed outside of this queue
        /// for properties marked with <see cref="AccessMode.FromQueueOnly"/>
        /// a <see cref="ThreadSafetyViolationException"/> will be thrown. 
        /// </summary>
        IDispatchQueue ActionQueue { get; set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Convenience method for asynchronously scheduling a job for this
        /// object
        /// </summary>
        /// <param name="a">The job to schedule</param>
        /// <returns>An awaitable task representing the scheduled job</returns>
        Task DoAsync(Action a);

        /// <summary>
        /// Convenience method for asynchronously scheduling a job for this
        /// object
        /// </summary>
        /// <typeparam name="T">The return value for the job</typeparam>
        /// <param name="f">The job to schedule</param>
        /// <returns>An awaitable task representing the scheduled job</returns>
        Task<T> DoAsync<T>(Func<T> f);

        /// <summary>
        /// Convenience method for scheduling and waiting for a job
        /// on this object's queue
        /// </summary>
        /// <param name="a">The job to schedule</param>
        void DoSync(Action a);

        /// <summary>
        /// Convenience method for scheduling a job on this object's queue,
        /// waiting for it to finish and returning the result
        /// </summary>
        /// <typeparam name="T">The return type of the scheduled job</typeparam>
        /// <param name="f">The job to schedule</param>
        /// <returns>The result of the job</returns>
        T DoSync<T>(Func<T> f);

        #endregion
    }
}
