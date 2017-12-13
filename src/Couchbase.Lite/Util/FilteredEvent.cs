﻿// 
//  FilteredEvent.cs
// 
//  Author:
//   Jim Borden  <jim.borden@couchbase.com>
// 
//  Copyright (c) 2017 Couchbase, Inc All rights reserved.
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
// 
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
// 

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace Couchbase.Lite.Util
{
    internal abstract class CouchbaseEventHandler
    {
        #region Variables

        public readonly Guid Id = Guid.NewGuid();

        #endregion

        #region Overrides

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CouchbaseEventHandler other)) {
                return false;
            }

            return Id == other.Id;
        }

        #endregion
    }

    internal class CouchbaseEventHandler<TEventType> : CouchbaseEventHandler where TEventType : EventArgs
    {
        #region Variables

        private readonly EventHandler<TEventType> _handler;
        private readonly TaskFactory _taskFactory;

        #endregion

        #region Constructors

        public CouchbaseEventHandler(EventHandler<TEventType> handler, TaskScheduler scheduler)
        {
            _taskFactory = new TaskFactory(scheduler ?? TaskScheduler.Current ?? TaskScheduler.Default);
            _handler = handler;
        }

        #endregion

        #region Public Methods

        public bool Equals(EventHandler<TEventType> handler)
        {
            return handler == _handler;
        }

        public void Fire(object sender, TEventType args)
        {
            _taskFactory.StartNew(() => _handler?.Invoke(sender, args));
        }

        #endregion

        #region Overrides

        public override int GetHashCode()
        {
            return _handler?.GetHashCode() ?? 0;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CouchbaseEventHandler<TEventType> other)) {
                return false;
            }

            return _handler == other._handler;
        }

        #endregion
    }

    internal sealed class CouchbaseEventHandler<TFilterType, TEventType> : CouchbaseEventHandler<TEventType> where TEventType : EventArgs
    {
        #region Variables

        public readonly TFilterType Filter;

        #endregion

        #region Constructors

        public CouchbaseEventHandler(EventHandler<TEventType> handler, TFilterType filter, TaskScheduler scheduler)
            : base(handler, scheduler)
        {
            Filter = filter;
        }

        #endregion
    }

    internal class Event<TEventType> where TEventType : EventArgs
    {
        #region Variables

        [NotNull]private readonly List<CouchbaseEventHandler<TEventType>> _events = new List<CouchbaseEventHandler<TEventType>>();
        [NotNull]private readonly object _locker = new object();

        #endregion

        #region Public Methods

        public int Add(CouchbaseEventHandler<TEventType> handler)
        {
            lock (_locker) {
                _events.Add(handler);
                return _events.Count - 1;
            }
        }

        public int Remove(ListenerToken id)
        {
           

            lock (_locker) {
                _events.Remove(id.EventHandler as CouchbaseEventHandler<TEventType>);
                return _events.Count;
            }
        }

        public int Remove(EventHandler<TEventType> handler)
        {
            lock (_locker) {
                var index = _events.FindIndex(x => x.Equals(handler));
                if (index != -1) {
                    _events.RemoveAt(index);
                }

                return _events.Count;
            }
        }

        #endregion

        #region Internal Methods

        internal void Fire(object sender, TEventType args)
        {
            lock (_locker) {
                foreach (var ev in _events) {
                    ev.Fire(sender, args);
                }
            }
        }

        #endregion
    }

    internal sealed class FilteredEvent<TFilterType, TEventType> where TEventType : EventArgs
    {
        #region Variables

        [NotNull]private readonly ConcurrentDictionary<TFilterType, HashSet<CouchbaseEventHandler<TEventType>>> _eventMap =
            new ConcurrentDictionary<TFilterType, HashSet<CouchbaseEventHandler<TEventType>>>();

        [NotNull]private readonly object _locker = new object();

        #endregion

        #region Public Methods

        public int Add(CouchbaseEventHandler<TFilterType, TEventType> handler)
        {
            var collection = _eventMap.GetOrAdd(handler.Filter, new HashSet<CouchbaseEventHandler<TEventType>>());
            lock (_locker) {
                collection.Add(handler);
                return collection.Count - 1;
            }
        }

        public int Remove(TFilterType key, EventHandler<TEventType> method)
        {
            var collection = _eventMap.GetOrAdd(key, new HashSet<CouchbaseEventHandler<TEventType>>());
            lock (_locker) {
                collection.RemoveWhere(x => x.Equals(method));
                return collection.Count;
            }
        }

        public int Remove(ListenerToken token)
        {
            if (!(token.EventHandler is CouchbaseEventHandler<TFilterType, TEventType> handler)) {
                return -1;
            }

            var collection = _eventMap.GetOrAdd(handler.Filter, new HashSet<CouchbaseEventHandler<TEventType>>());
            lock (_locker) {
                collection.Remove(handler);
                return collection.Count;
            }
        }

        #endregion

        #region Internal Methods

        internal void Fire(TFilterType key, object sender, TEventType args)
        {
            var collection = _eventMap.GetOrAdd(key, new HashSet<CouchbaseEventHandler<TEventType>>());
            lock (_locker) {
                foreach (var method in collection) {
                    method.Fire(sender, args);
                }
            }
        }

        #endregion
    }
}
