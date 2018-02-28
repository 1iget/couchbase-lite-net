﻿// 
//  Database.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Couchbase.Lite.DI;
using Couchbase.Lite.Internal.Doc;
using Couchbase.Lite.Internal.Query;
using Couchbase.Lite.Internal.Serialization;
using Couchbase.Lite.Logging;
using Couchbase.Lite.Query;
using Couchbase.Lite.Support;
using Couchbase.Lite.Sync;
using Couchbase.Lite.Util;

using JetBrains.Annotations;

using LiteCore;
using LiteCore.Interop;
using LiteCore.Util;

using Newtonsoft.Json;

namespace Couchbase.Lite
{

    /// <summary>
    /// Specifies the way that the library should behave when it encounters a situation
    /// when the database has been altered since the last read (e.g. a local operation read
    /// a document, modified it, and while it was being modified a replication committed a
    /// change to the document, and then the local document was saved after that)
    /// </summary>
    public enum ConcurrencyControl
    {
        /// <summary>
        /// Disregard the version that was received out of band and
        /// force this version to be current
        /// </summary>
        LastWriteWins,

        /// <summary>
        /// Throw an exception to indicate the situation so that the latest
        /// data can be read again from the local database
        /// </summary>
        FailOnConflict
    }

    /// <summary>
    /// A container for storing and maintaining Couchbase Lite <see cref="Document"/>s
    /// </summary>
    public sealed unsafe class Database : IDisposable
    {
        #region Constants

        private static readonly DatabaseObserverCallback _DbObserverCallback;
        private static readonly DocumentObserverCallback _DocObserverCallback;

        private static readonly C4DatabaseConfig DBConfig = new C4DatabaseConfig {
            flags = C4DatabaseFlags.Create | C4DatabaseFlags.AutoCompact | C4DatabaseFlags.SharedKeys,
            storageEngine = "SQLite",
            versioning = C4DocumentVersioning.RevisionTrees
        };

        private const string DBExtension = "cblite2";

        private const string Tag = nameof(Database);

        #endregion

        #region Variables

        [NotNull]
        private readonly Dictionary<string, DocumentObserver> _docObs = new Dictionary<string, DocumentObserver>();

        [NotNull]
        private readonly FilteredEvent<string, DocumentChangedEventArgs> _documentChanged =
            new FilteredEvent<string, DocumentChangedEventArgs>();

        [NotNull]
        private readonly Event<DatabaseChangedEventArgs> _databaseChanged = 
            new Event<DatabaseChangedEventArgs>();

        [NotNull]
        private readonly SharedStringCache _sharedStrings;
        
        [NotNull]
        private readonly HashSet<Document> _unsavedDocuments = new HashSet<Document>();

        [NotNull]
        private readonly TaskFactory _callbackFactory = new TaskFactory(new QueueTaskScheduler());

        #if false
        private IJsonSerializer _jsonSerializer;
        #endif

        private DatabaseObserver _obs;
        private C4Database* _c4db;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the configuration that were used to create the database
        /// </summary>
        [NotNull]
        public DatabaseConfiguration Config { get; }

        /// <summary>
        /// Gets the total number of documents in the database
        /// </summary>
        public ulong Count => ThreadSafety.DoLocked(() => Native.c4db_getDocumentCount(_c4db));

        /// <summary>
        /// Bracket operator for retrieving <see cref="DocumentFragment"/> objects
        /// </summary>
        /// <param name="id">The ID of the <see cref="DocumentFragment"/> to retrieve</param>
        /// <returns>The instantiated <see cref="DocumentFragment"/></returns>
        [NotNull]
        public DocumentFragment this[string id] => new DocumentFragment(GetDocument(id));

        /// <summary>
        /// Gets the name of the database
        /// </summary>
        [NotNull]
        public string Name { get; }

        /// <summary>
        /// Gets the path on disk where the database exists
        /// </summary>
        [CanBeNull]
        public string Path
        {
            get {
                return ThreadSafety.DoLocked(() => _c4db != null ? Native.c4db_getPath(c4db) : null);
            }
        }

        [NotNull]
        [ItemNotNull]
        internal ICollection<XQuery> ActiveLiveQueries { get; } = new HashSet<XQuery>();

        [NotNull]
        [ItemNotNull]
        internal ICollection<Replicator> ActiveReplications { get; } = new HashSet<Replicator>();

        internal C4BlobStore* BlobStore
        {
            get {
                C4BlobStore* retVal = null;
                ThreadSafety.DoLocked(() =>
                {
                    CheckOpen();
                    retVal = (C4BlobStore*) LiteCoreBridge.Check(err => Native.c4db_getBlobStore(c4db, err));
                });

                return retVal;
            }
        }

        internal C4Database* c4db
        {
            get {
                C4Database* retVal = null;
                ThreadSafety.DoLocked(() => retVal = _c4db);
                return retVal;
            }
        }

        [NotNull]
        internal IDictionary<Uri, Replicator> Replications { get; } = new Dictionary<Uri, Replicator>();

        internal FLEncoder* SharedEncoder
        {
            get {
                FLEncoder* encoder = null;
                ThreadSafety.DoLocked(() => encoder = Native.c4db_getSharedFleeceEncoder(_c4db));
                return encoder;
            }
        }

        [NotNull]
        internal SharedStringCache SharedStrings => ThreadSafety.DoLocked(() => _sharedStrings);

        [NotNull]
        internal ThreadSafety ThreadSafety { get; } = new ThreadSafety();
        
        private bool InTransaction => ThreadSafety.DoLocked(() => _c4db != null && Native.c4db_isInTransaction(_c4db));

        #endregion

        #region Constructors

        static Database()
        {
            _DbObserverCallback = DbObserverCallback;
            _DocObserverCallback = DocObserverCallback;
            FLSliceExtensions.RegisterFLEncodeExtension(FLValueConverter.FLEncode);
        }

        /// <summary>
        /// Creates a database given a name and some configuration
        /// </summary>
        /// <param name="name">The name of the database</param>
        /// <param name="configuration">The configuration to open it with</param>
        public Database(string name, DatabaseConfiguration configuration = null)
        {
            Name = CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(name), name);
            Config = configuration?.Freeze() ?? new DatabaseConfiguration(true);
            Open();
            FLSharedKeys* keys = null;
            ThreadSafety.DoLocked(() => keys = Native.c4db_getFLSharedKeys(_c4db));
            _sharedStrings = new SharedStringCache(keys);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="other">The database to copy from</param>
        internal Database([NotNull]Database other)
            : this(other.Name, other.Config)
        {
            
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~Database()
        {
            try {
                Dispose(false);
            } catch (Exception e) {
                Log.To.Database.E(Tag, "Error during finalizer, swallowing!", e);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Copies a database from the given path to be used as the database with
        /// the given name and configuration
        /// </summary>
        /// <param name="path">The path (of the .cblite2 folder) to copy</param>
        /// <param name="name">The name of the database to be used when opening</param>
        /// <param name="config">The config to use when copying (for specifying directory, etc)</param>
        [ContractAnnotation("name:null => halt; path:null => halt")]
        public static void Copy(string path, string name, [CanBeNull]DatabaseConfiguration config)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(path), path);
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(name), name);

            var destPath = DatabasePath(name, config?.Directory);
			LiteCoreBridge.Check(err =>
			{
				var nativeConfig = DBConfig;

                #if COUCHBASE_ENTERPRISE_FUTURE
				if (config?.EncryptionKey != null) {
					var key = config.EncryptionKey;
					var i = 0;
					nativeConfig.encryptionKey.algorithm = C4EncryptionAlgorithm.AES128;
					foreach (var b in key.KeyData) {
						nativeConfig.encryptionKey.bytes[i++] = b;
					}
				}
                #endif

				return Native.c4db_copy(path, destPath, &nativeConfig, err);
			});

		}

        /// <summary>
        /// Deletes the contents of a database with the given name in the
        /// given directory
        /// </summary>
        /// <param name="name">The name of the database to delete</param>
        /// <param name="directory">The directory to search in</param>
        [ContractAnnotation("name:null => halt")]
        public static void Delete(string name, [CanBeNull]string directory)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(name), name);

            var path = DatabasePath(name, directory);
            LiteCoreBridge.Check(err => Native.c4db_deleteAtPath(path, err) || err->code == 0);
        }

        /// <summary>
        /// Returns whether or not a database with the given name
        /// exists in the given directory
        /// </summary>
        /// <param name="name">The name of the database to search for</param>
        /// <param name="directory">The directory to search in</param>
        /// <returns><c>true</c> if the database exists in the directory, otherwise <c>false</c></returns>
        [ContractAnnotation("name:null => halt")]
        public static bool Exists(string name, [CanBeNull]string directory)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(name), name);

            return Directory.Exists(DatabasePath(name, directory));
        }

		/// <summary>
		/// Sets the log level for the given domains(s)
		/// </summary>
		/// <param name="domains">The domains(s) to change the log level for</param>
		/// <param name="level">The level to set the logging to</param>
		public static void SetLogLevel(LogDomain domains, LogLevel level)
		{
			if(domains.HasFlag(LogDomain.Couchbase)) {
				Log.To.Couchbase.Level = level;
			    Log.To.LiteCore.Level = level;
			}

			if(domains.HasFlag(LogDomain.Database)) {
				Log.To.Database.Level = level;
			}

			if(domains.HasFlag(LogDomain.Query)) {
				Log.To.Query.Level = level;
			}

			if(domains.HasFlag(LogDomain.Replicator)) {
				Log.To.Sync.Level = level;
			}

		    if (domains.HasFlag(LogDomain.Network)) {
		        Native.c4log_setLevel(Log.LogDomainBLIP, (C4LogLevel)level);
                Native.c4log_setLevel(Log.LogDomainWebSocket, (C4LogLevel)level);
		    }
		}

        internal static IReadOnlyDictionary<LogDomain, LogLevel> GetLogLevels(LogDomain domains)
        {
            var retVal = new Dictionary<LogDomain, LogLevel>();
            if(domains.HasFlag(LogDomain.Couchbase)) {
                retVal[LogDomain.Couchbase] = Log.To.Couchbase.Level;
            }

            if(domains.HasFlag(LogDomain.Database)) {
                retVal[LogDomain.Database] = Log.To.Database.Level;
            }

            if(domains.HasFlag(LogDomain.Query)) {
                retVal[LogDomain.Query] = Log.To.Query.Level;
            }

            if(domains.HasFlag(LogDomain.Replicator)) {
                retVal[LogDomain.Replicator] = Log.To.Sync.Level;
            }

            if (domains.HasFlag(LogDomain.Network)) {
                retVal[LogDomain.Network] = (LogLevel)Native.c4log_getLevel(Log.LogDomainBLIP);
            }

            return retVal;
        }
        
        /// <summary>
        /// Adds a change listener for the changes that occur in this database.  Signatures
        /// are the same as += style event handlers, but this signature allows the use of
        /// a custom task scheduler, if desired.
        /// </summary>
        /// <param name="scheduler">The scheduler to use when firing the change handler</param>
        /// <param name="handler">The handler to invoke</param>
        /// <returns>A <see cref="ListenerToken"/> that can be used to remove the handler later</returns>
        [ContractAnnotation("handler:null => halt; scheduler:null => notnull")]
        public ListenerToken AddChangeListener([CanBeNull]TaskScheduler scheduler,
            EventHandler<DatabaseChangedEventArgs> handler)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(handler), handler);

            return ThreadSafety.DoLocked(() =>
            {
                CheckOpen();

                var cbHandler = new CouchbaseEventHandler<DatabaseChangedEventArgs>(handler, scheduler);
                _databaseChanged.Add(cbHandler);

                return new ListenerToken(cbHandler, "db");
            });
        }

        /// <summary>
        /// Adds a change listener for the changes that occur in this database.  Signatures
        /// are the same as += style event handlers.
        /// </summary>
        /// <param name="handler">The handler to invoke</param>
        /// <returns>A <see cref="ListenerToken"/> that can be used to remove the handler later</returns>
        [ContractAnnotation("null => halt")]
        public ListenerToken AddChangeListener(EventHandler<DatabaseChangedEventArgs> handler)
        {
            return AddChangeListener(null, handler);
        }

        /// <summary>
        /// Adds a listener for changes on a certain document (by ID).
        /// </summary>
        /// <param name="id">The ID to add the listener for</param>
        /// <param name="scheduler">The scheduler to use when firing the event handler</param>
        /// <param name="handler">The logic to handle the event</param>
        /// <returns>A token that can be used to remove the listener later</returns>
        [ContractAnnotation("id:null => halt; handler:null => halt")]
        public ListenerToken AddDocumentChangeListener(string id, [CanBeNull]TaskScheduler scheduler,
            EventHandler<DocumentChangedEventArgs> handler)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(id), id);
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(handler), handler);

            return ThreadSafety.DoLocked(() =>
            {
                CheckOpen();

                var cbHandler =
                    new CouchbaseEventHandler<string, DocumentChangedEventArgs>(handler, id, scheduler);
                var count = _documentChanged.Add(cbHandler);
                if (count == 0) {
                    var docObs = new DocumentObserver(_c4db, id, _DocObserverCallback, this);
                    _docObs[id] = docObs;
                }
                
                return new ListenerToken(cbHandler, "doc");
            });
        }

        /// <summary>
        /// Adds a listener for changes on a certain document (by ID).
        /// </summary>
        /// <param name="id">The ID to add the listener for</param>
        /// <param name="handler">The logic to handle the event</param>
        /// <returns>A token that can be used to remove the listener later</returns>
        [ContractAnnotation("id:null => halt; handler:null => halt")]
        public ListenerToken AddDocumentChangeListener(string id, EventHandler<DocumentChangedEventArgs> handler)
        {
            return AddDocumentChangeListener(id, null, handler);
        }

        /// <summary>
        /// Closes the database
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Performs a manual compaction of this database, removing old irrelevant data
        /// and decreasing the size of the database file on disk
        /// </summary>
        public void Compact()
        {
            ThreadSafety.DoLockedBridge(err =>
            {
                CheckOpen();
                return Native.c4db_compact(_c4db, err);
            });
        }

        /// <summary>
        /// Creates an index of the given type on the given path with the given configuration
        /// </summary>
        /// <param name="name">The name to give to the index (must be unique, or previous
        /// index with the same name will be overwritten)</param>
        /// <param name="index">The index to creaate</param>
        [ContractAnnotation("name:null => halt; index:null => halt")]
        public void CreateIndex(string name, IIndex index)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(name), name);
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(index), index);

            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                var concreteIndex = Misc.TryCast<IIndex, QueryIndex>(index);
                var jsonObj = concreteIndex.ToJSON();
                var json = JsonConvert.SerializeObject(jsonObj);
                LiteCoreBridge.Check(err =>
                {
                    var internalOpts = concreteIndex.Options;

                    // For some reason a "using" statement here causes a compiler error
                    try {
                        return Native.c4db_createIndex(c4db, name, json, concreteIndex.IndexType, &internalOpts, err);
                    } finally {
                        internalOpts.Dispose();
                    }
                });
            });
        }

        /// <summary>
        /// Deletes the database
        /// </summary>
        public void Delete()
        {
            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                ThrowIfActiveItems();
                LiteCoreBridge.Check(err => Native.c4db_delete(_c4db, err));
                Native.c4db_free(_c4db);
                _c4db = null;
                _obs?.Dispose();
                _obs = null;
            });
        }

        /// <summary>
        /// Deletes the given <see cref="Document"/> from this database.  This call is equivalent to calling
        /// <see cref="Delete(Document, ConcurrencyControl)" /> with a second argument of
        /// <see cref="ConcurrencyControl.LastWriteWins"/>
        /// </summary>
        /// <param name="document">The document to save</param>
        /// <exception cref="InvalidOperationException">Thrown when trying to delete a document from a database
        /// other than the one it was previously added to</exception>
        [ContractAnnotation("null => halt")]
        public void Delete(Document document) => Delete(document, ConcurrencyControl.LastWriteWins);

        /// <summary>
        /// Deletes the given <see cref="Document"/> from this database
        /// </summary>
        /// <param name="document">The document to save</param>
        /// <param name="concurrencyControl">The rule to use when encountering a conflict in the database</param>
        /// <exception cref="InvalidOperationException">Thrown when trying to save a document into a database
        /// other than the one it was previously added to</exception>
        [ContractAnnotation("document:null => halt")]
        public void Delete(Document document, ConcurrencyControl concurrencyControl)
        {
            var doc = CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(document), document);
            Save(doc, concurrencyControl, true);
        }

        /// <summary>
        /// Deletes the index with the given name
        /// </summary>
        /// <param name="name">The name of the index to delete</param>
        [ContractAnnotation("null => halt")]
        public void DeleteIndex(string name)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(name), name);

            ThreadSafety.DoLockedBridge(err =>
            {
                CheckOpen();
                return Native.c4db_deleteIndex(c4db, name, err);
            });
        }

        /// <summary>
        /// Gets the <see cref="Document"/> with the specified ID
        /// </summary>
        /// <param name="id">The ID to use when creating or getting the document</param>
        /// <returns>The instantiated document, or <c>null</c> if it does not exist</returns>
        [CanBeNull]
        [ContractAnnotation("null => halt")]
        public Document GetDocument(string id)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(id), id);
            return ThreadSafety.DoLocked(() => GetDocumentInternal(id));
        }

        /// <summary>
        /// Gets a list of index names that are present in the database
        /// </summary>
        /// <returns>The list of created index names</returns>
        [NotNull]
        [ItemNotNull]
        public IList<string> GetIndexes()
        {
            object retVal = null;
            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                var result = new C4SliceResult();
                LiteCoreBridge.Check(err =>
                {
                    result = NativeRaw.c4db_getIndexes(c4db, err);
                    return result.buf != null;
                });

                var val = NativeRaw.FLValue_FromTrustedData(new FLSlice(result.buf, result.size));
                if (val == null) {
                    Native.c4slice_free(result);
                    throw new CouchbaseLiteException(C4ErrorCode.CorruptIndexData);
                }

                retVal = FLValueConverter.ToCouchbaseObject(val, this, true, typeof(string));
                Native.c4slice_free(result);
            });

            return retVal as IList<string> ?? new List<string>();
        }

        /// <summary>
        /// Runs the given batch of operations as an atomic unit
        /// </summary>
        /// <param name="action">The <see cref="Action"/> containing the operations. </param>
        [ContractAnnotation("null => halt")]
        public void InBatch(Action action)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(action), action);

            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                PerfTimer.StartEvent("InBatch_BeginTransaction");
                LiteCoreBridge.Check(err => Native.c4db_beginTransaction(_c4db, err));
                PerfTimer.StopEvent("InBatch_BeginTransaction");
                var success = true;
                try {
                    action();
                } catch (Exception e) {
                    Log.To.Database.W(Tag, "Exception during InBatch, rolling back...", e);
                    success = false;
                    throw;
                } finally {
                    PerfTimer.StartEvent("InBatch_EndTransaction");
                    LiteCoreBridge.Check(err => Native.c4db_endTransaction(_c4db, success, err));
                    PerfTimer.StopEvent("InBatch_EndTransaction");
                }
            });

            PostDatabaseChanged();
        }

        /// <summary>
        /// Purges the given <see cref="Document"/> from the database.  This leaves
        /// no trace behind and will not be replicated
        /// </summary>
        /// <param name="document">The document to purge</param>
        /// <returns>Whether or not the document was actually purged.</returns>
        /// <exception cref="InvalidOperationException">Thrown when trying to purge a document from a database
        /// other than the one it was previously added to</exception>
        [ContractAnnotation("null => halt")]
        public void Purge(Document document)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(document), document);

            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                VerifyDB(document);

                if (!document.Exists) {
                    throw new CouchbaseLiteException(C4ErrorCode.NotFound);
                }

                InBatch(() =>
                {
                    var result = Native.c4doc_purgeRevision(document.c4Doc.RawDoc, null, null);
                    if (result >= 0) {
                        LiteCoreBridge.Check(err => Native.c4doc_save(document.c4Doc.RawDoc, 0, err));
                    }
                });
            });
        }

        /// <summary>
        /// Removes a database changed listener by token
        /// </summary>
        /// <param name="token">The token received from <see cref="AddChangeListener(TaskScheduler, EventHandler{DatabaseChangedEventArgs})"/>
        /// and family</param>
        public void RemoveChangeListener(ListenerToken token)
        {
            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();

                if (token.Type == "db") {
                    _databaseChanged.Remove(token);
                } else {
                    _documentChanged.Remove(token);
                }
            });
        }

        /// <summary>
        /// Saves the given <see cref="MutableDocument"/> into this database.  This call is equivalent to calling
        /// <see cref="Save(MutableDocument, ConcurrencyControl)" /> with a second argument of
        /// <see cref="ConcurrencyControl.LastWriteWins"/>
        /// </summary>
        /// <param name="document">The document to save</param>
        /// <exception cref="InvalidOperationException">Thrown when trying to save a document into a database
        /// other than the one it was previously added to</exception>
        [ContractAnnotation("null => halt")]
        public void Save(MutableDocument document) => Save(document, ConcurrencyControl.LastWriteWins);

        /// <summary>
        /// Saves the given <see cref="MutableDocument"/> into this database
        /// </summary>
        /// <param name="document">The document to save</param>
        /// <param name="concurrencyControl">The rule to use when encountering a conflict in the database</param>
        /// <exception cref="InvalidOperationException">Thrown when trying to save a document into a database
        /// other than the one it was previously added to</exception>
        [ContractAnnotation("document:null => halt")]
        public void Save(MutableDocument document, ConcurrencyControl concurrencyControl)
        {
            var doc = CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(document), document);
            Save(doc, concurrencyControl, false);
        }

        #if CBL_LINQ
        public void Save(Couchbase.Lite.Linq.IDocumentModel model)
        {
            CBDebug.MustNotBeNull(Log.To.Database, Tag, nameof(model), model);

            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                MutableDocument md = (model.Document as MutableDocument) ?? model.Document?.ToMutable() ?? new MutableDocument();
                md.SetFromModel(model);

                try {
                    var retVal = Save(md, false);
                    model.Document = retVal;
                } finally {
                    md.Dispose();
                }
            });
        }
        #endif

        #if COUCHBASE_ENTERPRISE_FUTURE
		/// <summary>
		/// Sets the encryption key for the database.  If null, encryption is
		/// removed.
		/// </summary>
		/// <param name="key">The new key to encrypt the database with, or <c>null</c>
		/// to remove encryption</param>
		public void SetEncryptionKey([CanBeNull]EncryptionKey key)
		{
			ThreadSafety.DoLockedBridge(err =>
			{
				var newKey = new C4EncryptionKey
				{
					algorithm = key == null ? C4EncryptionAlgorithm.None : C4EncryptionAlgorithm.AES128
				};

			    if (key != null) {
			        var i = 0;
			        foreach (var b in key.KeyData) {
			            newKey.bytes[i++] = b;
			        }
			    }

			    return Native.c4db_rekey(c4db, &newKey, err);
			});
		}
    #endif

        #endregion

        #region Internal Methods
        
        internal void ResolveConflict([NotNull]string docID)
        {
            Debug.Assert(docID != null);

            ThreadSafety.DoLocked(() =>
            {
                var success = true;
                LiteCoreBridge.Check(err => Native.c4db_beginTransaction(_c4db, err));
                try {
                    var localDoc = new Document(this, docID);
                    if (!localDoc.Exists) {
                        throw new CouchbaseLiteException(C4ErrorCode.NotFound);
                    }

                    var remoteDoc = new Document(this, docID);
                    remoteDoc.SelectConflictingRevision();

                    // Resolve conflict:
                    Log.To.Database.I(Tag, "Resolving doc '{0}' (mine={1} and theirs={2})",
                        new SecureLogString(docID, LogMessageSensitivity.PotentiallyInsecure), localDoc.RevID,
                        remoteDoc.RevID);
                    var resolvedDoc = ResolveConflict(localDoc, remoteDoc);
                    SaveResolvedDocument(resolvedDoc, localDoc, remoteDoc);
                } catch (Exception) {
                    success = false;
                    throw;
                } finally {
                    LiteCoreBridge.Check(err => Native.c4db_endTransaction(_c4db, success, err));
                }
            });
        }

        #endregion

        #region Private Methods
        
        [NotNull]
        private static string DatabasePath(string name, string directory)
        {
            if (String.IsNullOrWhiteSpace(name)) {
                return directory;
            }

            var directoryToUse = String.IsNullOrWhiteSpace(directory)
                ? Service.GetRequiredInstance<IDefaultDirectoryResolver>().DefaultDirectory()
                : directory;
            return System.IO.Path.Combine(directoryToUse, $"{name}.{DBExtension}");
        }

        private static void DbObserverCallback(C4DatabaseObserver* db, object context)
        {
            var dbObj = (Database)context;
            dbObj?._callbackFactory.StartNew(() => {
              dbObj.PostDatabaseChanged();
            });
        }

        private static void DocObserverCallback(C4DocumentObserver* obs, string docID, ulong sequence, object context)
        {
            if (docID == null) {
                return;
            }

            var dbObj = (Database)context;
            dbObj?._callbackFactory.StartNew(() => {
                dbObj.PostDocChanged(docID);
            });
        }

        private void CheckOpen()
        {
            if(_c4db == null) {
                throw new InvalidOperationException("Attempt to perform an operation on a closed database");
            }
        }

        private void Dispose(bool disposing)
        {
            if (_c4db == null) {
                return;
            }

            if (disposing) {
                Misc.SafeSwap(ref _obs, null);
                if (_unsavedDocuments.Count > 0) {
                    Log.To.Database.W(Tag,
                        $"Closing database with {_unsavedDocuments.Count} such as {_unsavedDocuments.Any()}");
                }

                _unsavedDocuments.Clear();
            }

            Log.To.Database.I(Tag, $"Closing database at path {Native.c4db_getPath(_c4db)}");
            LiteCoreBridge.Check(err => Native.c4db_close(_c4db, err));
            Native.c4db_free(_c4db);
            _c4db = null;
        }

        [CanBeNull]
        private Document GetDocumentInternal([NotNull]string docID)
        {
            CheckOpen();
            var doc = new Document(this, docID);

            if (!doc.Exists || doc.IsDeleted) {
                doc.Dispose();
                Log.To.Database.V(Tag, "Requested existing document {0}, but it doesn't exist", 
                    new SecureLogString(docID, LogMessageSensitivity.PotentiallyInsecure));
                return null;
            }

            return doc;
        }

        private void Open()
        {
            if(_c4db != null) {
                return;
            }
            
            Directory.CreateDirectory(Config.Directory);
            var path = DatabasePath(Name, Config.Directory);
            var config = DBConfig;

            var encrypted = "";

            #if COUCHBASE_ENTERPRISE_FUTURE
            if(Config.EncryptionKey != null) {
                var key = Config.EncryptionKey;
                var i = 0;
                config.encryptionKey.algorithm = C4EncryptionAlgorithm.AES128;
                foreach(var b in key.KeyData) {
                    config.encryptionKey.bytes[i++] = b;
                }

                encrypted = "encrypted ";
            }
            #endif

            Log.To.Database.I(Tag, $"Opening {encrypted}database at {path}");
            var localConfig1 = config;
            ThreadSafety.DoLocked(() =>
            {
                _c4db = (C4Database*) LiteCoreBridge.Check(err =>
                {
                    var localConfig2 = localConfig1;
                    return Native.c4db_open(path, &localConfig2, err);
                });

                _obs = Native.c4dbobs_create(_c4db, _DbObserverCallback, this);
            });
        }

        private void PostDatabaseChanged()
        {
            var allChanges = new List<DatabaseChangedEventArgs>();
			ThreadSafety.DoLocked(() =>
			{
				if (_obs == null || _c4db == null || InTransaction) {
					return;
				}

				const uint maxChanges = 100u;
                var external = false;
				uint nChanges;
				var changes = new C4DatabaseChange[maxChanges];
			    var docIDs = new List<string>();
				do {
					// Read changes in batches of MaxChanges:
					bool newExternal;
					nChanges = Native.c4dbobs_getChanges(_obs.Observer, changes, maxChanges, &newExternal);
				    if (nChanges == 0 || external != newExternal || docIDs.Count > 1000) {
				        if (docIDs.Count > 0) {
                            // Only notify if there are actually changes to send
				            var args = new DatabaseChangedEventArgs(this, docIDs);
				            allChanges.Add(args);
				            docIDs = new List<string>();
				        }
				    }

				    external = newExternal;
				    for (var i = 0; i < nChanges; i++) {
				        docIDs.Add(changes[i].docID.CreateString());
				    }
				} while (nChanges > 0);
			});

            foreach (var args in allChanges) {
                _databaseChanged.Fire(this, args);
            }
        }

        private void PostDocChanged([NotNull]string documentID)
        {
            DocumentChangedEventArgs change = null;
            ThreadSafety.DoLocked(() =>
            {
                if (!_docObs.ContainsKey(documentID) || _c4db == null || Native.c4db_isInTransaction(_c4db)) {
                    return;
                }

                change = new DocumentChangedEventArgs(documentID, this);
            });

            _documentChanged.Fire(documentID, this, change);
        }

        [NotNull]
        private Document ResolveConflict([NotNull]Document localDoc, [NotNull]Document remoteDoc)
        {
            if (remoteDoc.IsDeleted) {
                return remoteDoc;
            }

            if (localDoc.IsDeleted) {
                return localDoc;
            }

            if (localDoc.Generation > remoteDoc.Generation) {
                return localDoc;
            }

            if (remoteDoc.Generation > localDoc.Generation) {
                return remoteDoc;
            }

            return String.CompareOrdinal(localDoc.RevID, remoteDoc.RevID) > 0 ? localDoc : remoteDoc;
        }
        
        private void Save([NotNull]Document document, ConcurrencyControl concurrencyControl, bool deletion)
        {
            if (deletion && document.RevID == null) {
                throw new CouchbaseLiteException(C4ErrorCode.InvalidParameter, "Cannot delete a document that has not yet been saved");
            }

            ThreadSafety.DoLocked(() =>
            {
                CheckOpen();
                VerifyDB(document);
                C4Document* curDoc = null;
                C4Document* newDoc = null;
                var committed = false;
                try {
                    LiteCoreBridge.Check(err => Native.c4db_beginTransaction(_c4db, err));
                    Save(document, &newDoc, null, deletion);
                    if (newDoc == null) {
                        // Handle conflict:
                        if (concurrencyControl == ConcurrencyControl.FailOnConflict) {
                            throw new CouchbaseLiteException(C4ErrorCode.Conflict);
                        }

                        C4Error err;
                        curDoc = Native.c4doc_get(_c4db, document.Id, true, &err);

                        // If deletion and the current oc has already been deleted
                        // or doesn't exist:
                        if (deletion) {
                            if (curDoc == null) {
                                if (err.code == (int) C4ErrorCode.NotFound) {
                                    return;
                                }

                                throw CouchbaseException.Create(err);
                            } else if (curDoc->flags.HasFlag(C4DocumentFlags.DocDeleted)) {
                                document.c4Doc = new C4DocumentWrapper(curDoc);
                                curDoc = null;
                                return;
                            }
                        }

                        // Save changes on the current branch:
                        if (curDoc == null) {
                            throw CouchbaseException.Create(err);
                        }

                        Save(document, &newDoc, curDoc, deletion);
                    }
                    
                    committed = true; // Weird, but if the next call fails I don't want to call it again in the catch block
                    LiteCoreBridge.Check(e => Native.c4db_endTransaction(_c4db, true, e));
                    document.c4Doc = new C4DocumentWrapper(newDoc);
                    newDoc = null;
                } catch (Exception) {
                    if (!committed) {
                        LiteCoreBridge.Check(e => Native.c4db_endTransaction(_c4db, false, e));
                    }

                    throw;
                } finally {
                    Native.c4doc_free(curDoc);
                    Native.c4doc_free(newDoc);
                }
            });
        }

        private void Save([NotNull]Document doc, C4Document** outDoc, C4Document* baseDoc, bool deletion)
        {
            var revFlags = (C4RevisionFlags) 0;
            if (deletion) {
                revFlags = C4RevisionFlags.Deleted;
            }

            byte[] body = null;
            if (!deletion && !doc.IsEmpty) {

                body = doc.Encode();
                var root = Native.FLValue_FromTrustedData(body);
                if (root == null) {
                    Log.To.Database.E(Tag, "Failed to encode document body properly.  Aborting save of document!");
                    return;
                }

                var rootDict = Native.FLValue_AsDict(root);
                if (rootDict == null) {
                    Log.To.Database.E(Tag, "Failed to encode document body properly.  Aborting save of document!");
                    return;
                }

                ThreadSafety.DoLocked(() =>
                {
                    if (Native.c4doc_dictContainsBlobs(rootDict, SharedStrings.SharedKeys)) {
                        revFlags |= C4RevisionFlags.HasAttachments;
                    }
                });
                
            } else if (doc.IsEmpty) {
                FLEncoder* encoder = SharedEncoder;
                Native.FLEncoder_BeginDict(encoder, 0);
                Native.FLEncoder_EndDict(encoder);
                body = Native.FLEncoder_Finish(encoder, null);
                Native.FLEncoder_Reset(encoder);
            }

            var rawDoc = baseDoc != null ? baseDoc :
                doc.c4Doc?.HasValue == true ? doc.c4Doc.RawDoc : null;
            if (rawDoc != null) {
                doc.ThreadSafety.DoLocked(() =>
                {
                    ThreadSafety.DoLocked(() =>
                    {
                        *outDoc = (C4Document*)NativeHandler.Create()
                            .AllowError((int)C4ErrorCode.Conflict, C4ErrorDomain.LiteCoreDomain).Execute(
                                err => Native.c4doc_update(rawDoc, body, revFlags, err));
                    });
                });
            } else {
                ThreadSafety.DoLocked(() =>
                {
                    *outDoc = (C4Document*)NativeHandler.Create()
                        .AllowError((int)C4ErrorCode.Conflict, C4ErrorDomain.LiteCoreDomain).Execute(
                            err => Native.c4doc_create(_c4db, doc.Id, body, revFlags, err));
                });
            }
        }

        // Must be called in transaction
        private void SaveResolvedDocument([NotNull]Document resolved, [NotNull]Document localDoc, [NotNull]Document remoteDoc)
        {
            if (!ReferenceEquals(resolved, localDoc)) {
                resolved.Database = this;
            }

            // The remote branch has to win, so that the doc revision history matches the server's
            var winningRevID = remoteDoc.RevID;
            var losingRevID = localDoc.RevID;

            byte[] mergedBody = null;
            if (!ReferenceEquals(resolved, remoteDoc)) {
                // Unless the remote revision is being used as-is, we need a new revision:
                mergedBody = resolved.Encode();
            }

            // Tell LiteCore to do the resolution:
            C4Document* rawDoc = localDoc.c4Doc != null ? localDoc.c4Doc.RawDoc : null;
            LiteCoreBridge.Check(
                err => Native.c4doc_resolveConflict(rawDoc, winningRevID, losingRevID, mergedBody, err));
            LiteCoreBridge.Check(err => Native.c4doc_save(rawDoc, 0, err));

            Log.To.Database.I(Tag, "Conflict resolved as doc '{0}' rev {1}", localDoc.Id, rawDoc->revID.CreateString());
        }

        private void ThrowIfActiveItems()
        {
            if (ActiveReplications.Any()) {
                throw new InvalidOperationException(
                    "Cannot close the database. Please stop all of the replicators before closing the database.");
            }

            if (ActiveLiveQueries.Any()) {
                throw new InvalidOperationException(
                    "Cannot close the database. Please remove all of the query listeners before closing the database");
            }
        }

        private void VerifyDB([NotNull]Document document)
        {
            if (document.Database == null) {
                document.Database = this;
            } else if (document.Database != this) {
                throw new CouchbaseLiteException(C4ErrorCode.InvalidParameter, "Cannot operate on a document from another database");
            }
        }

        #endregion

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Path?.GetHashCode() ?? 0;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (!(obj is Database other)) {
                return false;
            }

            return String.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override string ToString() => $"DB[{Path}]";

        #region IDisposable

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            ThreadSafety.DoLocked(() =>
            {
                ThrowIfActiveItems();
                Dispose(true);
            });
        }

        #endregion
    }
}

