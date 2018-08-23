﻿//
// ThreadSafeHashSet.cs
//
// Copyright (c) 2018 Couchbase, Inc All rights reserved.
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
using System.Threading;

namespace Couchbase.Lite.Shared.Util
{
    internal sealed class ThreadSafeHashSet<T> : ICollection<T>
    {
        private readonly HashSet<T> _internal = new HashSet<T>();
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        public int Count
        {
            get {
                _lock.EnterReadLock();
                return _internal.Count;
            }
        }

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            _lock.EnterWriteLock();
            _internal.Add(item);
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            _internal.Clear();
        }

        public bool Contains(T item)
        {
            _lock.EnterReadLock();
            return _internal.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _lock.EnterReadLock();
            _internal.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator() => new SnapshotEnumerator(this);

        public bool Remove(T item)
        {
            _lock.EnterWriteLock();
            return _internal.Remove(item);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class SnapshotEnumerator : IEnumerator<T>
        {
            private T[] _snapshot;
            private IEnumerator<T> _internal;

            public SnapshotEnumerator(ThreadSafeHashSet<T> parent)
            {
                _snapshot = new T[parent.Count];
                parent.CopyTo(_snapshot, 0);
                _internal = ((IEnumerable<T>)_snapshot).GetEnumerator();
            }

            public T Current => _internal.Current;

            object IEnumerator.Current => Current;

            public void Dispose() => _internal.Dispose();

            public bool MoveNext() => _internal.MoveNext();

            public void Reset() => _internal.Reset();
        }
    }
}
