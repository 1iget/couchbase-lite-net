﻿// 
// QueryEnumerator.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using Couchbase.Lite.DB;
using Couchbase.Lite.Logging;
using LiteCore;
using LiteCore.Interop;

namespace Couchbase.Lite.Querying
{
    internal abstract unsafe class QueryEnumerable<T> : IEnumerable<T>
    {
        #region Variables

        // private const string Tag = nameof(QueryEnumerable<T>);

        protected readonly Database _db;
        protected readonly string _encodedParameters;
        protected readonly C4QueryOptions _options;
        protected readonly C4Query* _query;

        #endregion

        #region Constructors

        protected QueryEnumerable(Database db, C4Query* query, C4QueryOptions options, string encodedParameters)
        {
            _db = db;
            _query = query;
            _options = options;
            _encodedParameters = encodedParameters;
        }

        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region IEnumerable<T>

        public abstract IEnumerator<T> GetEnumerator();

        #endregion
    }

    internal sealed unsafe class QueryRowEnumerable : QueryEnumerable<QueryRow>
    {
        #region Constructors

        internal QueryRowEnumerable(Database db, C4Query* query, C4QueryOptions options, string encodedParameters)
            : base(db, query, options, encodedParameters)
        {

        }

        #endregion

        #region Overrides

        public override IEnumerator<QueryRow> GetEnumerator()
        {
            return new QueryRowEnumerator(_db, _query, _options, _encodedParameters);
        }

        #endregion
    }

    internal sealed unsafe class LinqQueryEnumerable<T> : QueryEnumerable<T>
    {
        #region Constructors

        internal LinqQueryEnumerable(Database db, C4Query* query, C4QueryOptions options, string encodedParameters)
            : base(db, query, options, encodedParameters)
        {

        }

        #endregion

        #region Overrides

        public override IEnumerator<T> GetEnumerator()
        {
            return new LinqQueryEnumerator<T>(_db, _query, _options, _encodedParameters);
        }

        #endregion
    }

    internal abstract unsafe class QueryEnumerator<T> : InteropObject, IEnumerator<T>
    {
        #region Constants

        private const string Tag = nameof(QueryEnumerator<T>);

        #endregion

        #region Variables

        protected Database _db;
        protected C4Query* _query;
        private long _native;

        #endregion

        #region Properties

        public T Current { get; protected set; }

        object IEnumerator.Current
        {
            get {
                return Current;
            }
        }

        private C4QueryEnumerator* Native
        {
            get {
                return (C4QueryEnumerator*)_native;
            }
            set {
                _native = (long)value;
            }
        }

        #endregion

        #region Constructors

        protected QueryEnumerator(Database db, C4Query* query, C4QueryOptions options, string encodedParameters)
        {
            _db = db;
            _query = query;
            Native = (C4QueryEnumerator*)LiteCoreBridge.Check(err =>
            {
                var localOpts = options;
                return LiteCore.Interop.Native.c4query_run(query, &localOpts, encodedParameters, err);
            });
        }

        #endregion

        #region Protected Methods

        protected abstract void SetCurrent(C4QueryEnumerator* enumerator);

        #endregion

        #region Overrides

        protected override void Dispose(bool finalizing)
        {
            var native = (C4QueryEnumerator*)Interlocked.Exchange(ref _native, 0);
            if(native != null) {
                LiteCore.Interop.Native.c4queryenum_close(native);
                LiteCore.Interop.Native.c4queryenum_free(native);
            }
        }

        #endregion

        #region IEnumerator

        public bool MoveNext()
        {
            C4Error err;
            if(LiteCore.Interop.Native.c4queryenum_next(Native, &err)) {
                (Current as IDisposable)?.Dispose();
                SetCurrent(Native);
                return true;
            } else if(err.code != 0) {
                Log.To.Query.E(Tag, $"QueryEnumerator error: {err.domain}/{err.code}");
            }

            return false;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        #endregion
    }

    internal sealed unsafe class QueryRowEnumerator : QueryEnumerator<QueryRow>
    {
        #region Constructors

        public QueryRowEnumerator(Database db, C4Query* query, C4QueryOptions options, string encodedParameters)
            : base(db, query, options, encodedParameters)
        {

        }

        #endregion

        #region Overrides

        protected override void SetCurrent(C4QueryEnumerator* enumerator)
        {
            Current = enumerator->fullTextTermCount > 0 ? new FullTextQueryRow(_db, _query, enumerator) : new QueryRow(_db, enumerator);
        }

        #endregion
    }

    internal sealed unsafe class LinqQueryEnumerator<T> : QueryEnumerator<T>
    {
        #region Constructors

        public LinqQueryEnumerator(Database db, C4Query* query, C4QueryOptions options, string encodedParameters)
            : base(db, query, options, encodedParameters)
        {

        }

        #endregion

        #region Overrides
#warning fix DocumentMetadata type

        [SuppressMessage("ReSharper", "PossibleNullReferenceException", Justification = "Current will always be IDocumentModel")]
        protected override void SetCurrent(C4QueryEnumerator* enumerator)
        {
            var doc = default(C4Document*);
            _db.ActionQueue.DispatchSync(() => doc = (C4Document*)LiteCoreBridge.Check(err => Native.c4doc_getBySequence(_db.c4db, enumerator->docSequence, err)));
            try {
                FLValue* value = NativeRaw.FLValue_FromTrustedData((FLSlice)doc->selectedRev.body);
                Current = _db.JsonSerializer.Deserialize<T>(value);
                var idm = Current as IDocumentModel;
                idm.Metadata = new DocumentMetadata(doc->docID.CreateString(), null, doc->flags.HasFlag(C4DocumentFlags.Deleted), doc->sequence);
            } finally {
                Native.c4doc_free(doc);
            }
        }

        #endregion
    }
}
