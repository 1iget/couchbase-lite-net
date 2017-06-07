﻿// 
// ReplicatorConfiguration.cs
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

namespace Couchbase.Lite.Sync
{
    /// <summary>
    /// An enum representing the direction of a <see cref="Replicator"/>
    /// </summary>
    [Flags]
    public enum ReplicatorType
    {
        /// <summary>
        /// The replication will push data from local to remote
        /// </summary>
        Push = 1 << 0,

        /// <summary>
        /// The replication will pull data from remote to local
        /// </summary>
        Pull = 1 << 1,

        /// <summary>
        /// The replication will operate in both directions
        /// </summary>
        PushAndPull = Push | Pull
    }

    /// <summary>
    /// A class representing configuration options for a <see cref="Replicator"/>
    /// </summary>
    public sealed class ReplicatorConfiguration
    {
        /// <summary>
        /// Gets or sets the local database participating in the replication.  This property
        /// is required to create an <see cref="Replicator"/>
        /// </summary>
        public Database Database { get; set; }

        /// <summary>
        /// Gets or sets the target to replicate with.  This property
        /// is required to create an <see cref="Replicator"/>
        /// </summary>
        public ReplicatorTarget Target { get; set; }

        /// <summary>
        /// A value indicating the direction of the replication.  The default is
        /// <see cref="ReplicatorType.PushAndPull"/> which is bidirectional
        /// </summary>
        public ReplicatorType ReplicatorType { get; set; }

        /// <summary>
        /// Gets or sets whether or not the <see cref="Replicator"/> should stay
        /// active indefinitely.  The default is <c>false</c>
        /// </summary>
        public bool Continuous { get; set; }

        /// <summary>
        /// Gets or sets the object to use when resolving incoming conflicts.  The default
        /// is <c>null</c> which will set up the default algorithm of the most active revision
        /// </summary>
        public IConflictResolver ConflictResolver { get; set; }

        /// <summary>
        /// Gets or sets extra options affecting replication.
        /// </summary>
        public ReplicatorOptionsDictionary Options { get; set; } = new ReplicatorOptionsDictionary();

        /// <summary>
        /// Default constructor
        /// </summary>
        public ReplicatorConfiguration()
        {
            ReplicatorType = ReplicatorType.PushAndPull;
        }

        internal static ReplicatorConfiguration Clone(ReplicatorConfiguration source)
        {
            return (ReplicatorConfiguration) source.MemberwiseClone();
        }

        internal void Validate()
        {
            if (Database == null) {
                throw new ArgumentNullException(nameof(Database));
            }

            if (Target == null) {
                throw new ArgumentNullException(nameof(Target));
            }
        }

        
    }
}
