using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.IO;
using Sharpen;
using Couchbase.Lite.Util;
using Couchbase.Lite.Storage;
using Couchbase.Lite.Internal;
using System.Threading.Tasks;
using System.Text;

namespace Couchbase.Lite 
{

    public partial class Database 
    {
    
    #region Constructors

        /// <summary>Constructor</summary>
        internal Database(String path, Manager manager)
        {
            System.Diagnostics.Debug.Assert((path.StartsWith("/", StringComparison.InvariantCultureIgnoreCase)));

            //path must be absolute
            Path = path;
            Name = FileDirUtils.GetDatabaseNameFromPath(path);
            Manager = manager;
            DocumentCache = new LruCache<string, Document>(MaxDocCacheSize);
        }


    #endregion

    #region Static Members
        //Properties
        public static CompileFilterDelegate FilterCompiler { get; set; }

        // "_local/*" is not a valid document ID. Local docs have their own API and shouldn't get here.
        internal static String GenerateDocumentId()
        {
            return Misc.TDCreateUUID();
        }

        static readonly ICollection<String> KnownSpecialKeys;

        static Database()
        {
            // Length that constitutes a 'big' attachment
            KnownSpecialKeys = new List<String>();
            KnownSpecialKeys.Add("_id");
            KnownSpecialKeys.Add("_rev");
            KnownSpecialKeys.Add("_attachments");
            KnownSpecialKeys.Add("_deleted");
            KnownSpecialKeys.Add("_revisions");
            KnownSpecialKeys.Add("_revs_info");
            KnownSpecialKeys.Add("_conflicts");
            KnownSpecialKeys.Add("_deleted_conflicts");
        }

    #endregion
    
    #region Instance Members
        //Properties

        /// <summary>
        /// Gets the %Database% name.
        /// </summary>
        public String Name { get; internal set; }

        /// <summary>
        /// Gets the %Manager% that owns this %Database%.
        /// </summary>
        public Manager Manager { get; private set; }

        /// <summary>
        /// Gets the number of %Documents% in the %Database%.
        /// </summary>
        // TODO: Convert this to a standard method call.
        public Int32 DocumentCount 
        {
            get 
            {
                var sql = "SELECT COUNT(DISTINCT doc_id) FROM revs WHERE current=1 AND deleted=0";
                Cursor cursor = null;
                int result = 0;
                try
                {
                    cursor = StorageEngine.RawQuery(sql, null);
                    if (cursor.MoveToNext())
                    {
                        result = cursor.GetInt(0);
                    }
                }
                catch (SQLException e)
                {
                    Log.E(Database.Tag, "Error getting document count", e);
                }
                finally
                {
                    if (cursor != null)
                    {
                        cursor.Close();
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Gets the latest sequence number used by the %Database%.  Every new %Revision% is assigned a new sequence 
        /// number, so this property increases monotonically as changes are made to the %Database%. This can be used to 
        /// check whether the %Database% has changed between two points in time.
        /// </summary>
        public Int64 LastSequenceNumber 
        {
            get
            {
                var sql = "SELECT MAX(sequence) FROM revs";
                Cursor cursor = null;
                long result = 0;
                try
                {
                    cursor = StorageEngine.RawQuery(sql, null);
                    if (cursor.MoveToNext())
                    {
                        result = cursor.GetLong(0);
                    }
                }
                catch (SQLException e)
                {
                    Log.E(Database.Tag, "Error getting last sequence", e);
                }
                finally
                {
                    if (cursor != null)
                    {
                        cursor.Close();
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Gets all the running %Replications% for this %Database%.  This includes all continuous %Replications% and 
        /// any non-continuous %Replications% that has been started and are still running.
        /// </summary>
        /// <value>All replications.</value>
        public IEnumerable<Replication> AllReplications { get { return ActiveReplicators; } }

        //Methods

        /// <summary>
        /// Compacts the %Database% file by purging non-current %Revisions% and deleting unused %Attachments%.
        /// </summary>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"/>
        public void Compact()
        {
            // Can't delete any rows because that would lose revision tree history.
            // But we can remove the JSON of non-current revisions, which is most of the space.
            try
            {
                Log.V(Database.Tag, "Deleting JSON of old revisions...");
                ContentValues args = new ContentValues();
                args.Put("json", (string)null);
                StorageEngine.Update("revs", args, "current=0", null);
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error compacting", e);
                return;
            }

            Log.V(Database.Tag, "Deleting old attachments...");
            var result = GarbageCollectAttachments();
            Log.V(Database.Tag, "Vacuuming SQLite sqliteDb..." + result.ToString());

            try
            {
                StorageEngine.ExecSQL("VACUUM");
            }
            catch (SQLException e)
            {
                Log.E(Couchbase.Lite.Database.Tag, "Error vacuuming sqliteDb", e);
                return;
            }
            return;
        }

        /// <summary>
        /// Deletes the %Database%.
        /// </summary>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"/>
        public void Delete()
        {
            if (open)
            {
                if (!Close())
                {
                    return;
                }
            }
            else
            {
                if (!Exists())
                {
                    return;
                }
            }
            var file = new FilePath(Path);
            var attachmentsFile = new FilePath(AttachmentStorePath);

            var deleteStatus = file.Delete();
            if (!deleteStatus)
                Log.V(Database.Tag, String.Format("Error deleting the SQLite database file at {0}", file.GetAbsolutePath()));

            //recursively delete attachments path
            var deletedAttachmentsPath = true;
            try {
                Directory.Delete (attachmentsFile.GetPath (), true);
            } catch (Exception ex) {
                Log.V(Database.Tag, "Error deleting the attachments directory.", ex);
                deletedAttachmentsPath = false;
            }

            if (!(deleteStatus && deletedAttachmentsPath))
                throw new CouchbaseLiteException("Could not the SQLite file and/or the attachments folder. See log for additional details.");
        }

        /// <summary>
        /// Gets or creates the %Document% with the given id.
        /// </summary>
        /// <returns>The %Document%.</returns>
        /// <param name="id">Identifier.</param>
        public Document GetDocument(String id) 
        { 
            if (String.IsNullOrWhiteSpace (id)) {
                return null;
            }

            var doc = DocumentCache.Get(id);
            if (doc == null)
            {
                doc = new Document(this, id);
                if (doc == null)
                {
                    return null;
                }
                DocumentCache.Put(id, doc);
            }

            return doc;
        }

        /// <summary>
        /// Gets the %Document% with the given id, or null if it does not exist.
        /// </summary>
        /// <returns>The existing document.</returns>
        /// <param name="id">Identifier.</param>
        public Document GetExistingDocument(String id) 
        { 
            if (String.IsNullOrWhiteSpace (id)) {
                return null;
            }
            var revisionInternal = GetDocumentWithIDAndRev(id, null, EnumSet.NoneOf<TDContentOptions>());
            return revisionInternal == null ? null : GetDocument (id);
        }

        /// <summary>
        /// Creates a %Document% with a unique id.
        /// </summary>
        /// <returns>The document.</returns>
        public Document CreateDocument()
        { 
            return GetDocument(Misc.TDCreateUUID());
        }

        /// <summary>
        /// Gets the local document with the given id, or null if it does not exist.
        /// </summary>
        /// <returns>The existing local document.</returns>
        /// <param name="id">Identifier.</param>
        public IDictionary<String, Object> GetExistingLocalDocument(String id) 
        {
            var revInt = GetLocalDocument(MakeLocalDocumentId(id), null);
            if (revInt == null)
            {
                return null;
            }
            return revInt.GetProperties();
        }

        /// <summary>
        /// Sets the contents of the local %Document% with the given id.  If <param name="properties"/> is null, the 
        /// %Document% is deleted.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="properties">Properties.</param>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        public void PutLocalDocument(String id, Dictionary<String, Object> properties) 
        { 
            // TODO: the iOS implementation wraps this in a transaction, this should do the same.
            id = MakeLocalDocumentId(id);
            var prevRev = GetLocalDocument(id, null);

            if (prevRev == null && properties == null)
            {
                return;
            }

            var deleted = false || properties == null;
            var rev = new RevisionInternal(id, null, deleted, this);

            if (properties != null)
            {
                rev.SetProperties(properties);
            }

            var success = false;

            if (prevRev == null)
            {
                success = PutLocalRevision(rev, null) != null;
            }
            else
            {
                success = PutLocalRevision(rev, prevRev.GetRevId()) != null;
            }

            if (!success) 
            {
                throw new CouchbaseLiteException("Unable to put local revision with id " + id);
            }
        }

        /// <summary>
        /// Deletes the local %Document% with the given id.
        /// </summary>
        /// <returns><c>true</c>, if local %Document% was deleted, <c>false</c> otherwise.</returns>
        /// <param name="id">Identifier.</param>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        public Boolean DeleteLocalDocument(String id) 
        {
            id = MakeLocalDocumentId(id);

            var prevRev = GetLocalDocument(id, null);
            if (prevRev == null)
            {
                return false;
            }

            DeleteLocalDocument(id, prevRev.GetRevId());
            return true;
        } 

        /// <summary>
        /// Creates a %Query% that matches all %Documents% in the %Database%.
        /// </summary>
        /// <returns>All documents query.</returns>
        public Query CreateAllDocumentsQuery() 
        {
            return new Query(this, (View)null);
        }

        /// <summary>
        /// Gets or creates the %View% with the given name.  New %View%s won't be added to the %Database% until a 
        /// map function is assigned.
        /// </summary>
        /// <returns>The view.</returns>
        /// <param name="name">Name.</param>
        public View GetView(String name) 
        {
            View view = null;

            if (views != null)
            {
                view = views.Get(name);
            }

            if (view != null)
            {
                return view;
            }

            return RegisterView(new View(this, name));
        }

        /// <summary>
        /// Gets the %View% with the given name, or null if it does not exist.
        /// </summary>
        /// <returns>The existing view.</returns>
        /// <param name="name">Name.</param>
        public View GetExistingView(String name) 
        {
            View view = null;
            if (views != null)
            {
                view = views.Get(name);
            }

            if (view != null)
            {
                return view;
            }

            view = new View(this, name);

            if (view.Id == 0)
            {
                return null;
            }

            return RegisterView(view);
        }

        /// <summary>
        /// Gets the validation.
        /// </summary>
        /// <returns>The validation delegate for the given name, or null if it does not exist.</returns>
        /// <param name="name">Name.</param>
        public ValidateDelegate GetValidation(String name) 
        {
            ValidateDelegate result = null;
            if (Validations != null)
            {
                result = Validations.Get(name);
            }
            return result;
        }

        /// <summary>
        /// Sets the validation.
        /// </summary>
        /// <remarks>
        /// Sets the validation delegate for the given name.  If delegate is null, 
        /// the validation with the given name is deleted.  Before any change 
        /// to the <see cref="Couchbase.Lite.Database"/> is committed, including incoming changes from a pull 
        /// <see cref="Couchbase.Lite.Replication"/>, all of its validation delegates are called and given 
        /// a chance to reject it.
        /// </remarks>
        /// <param name="name">Name.</param>
        /// <param name="validationDelegate">Validation delegate.</param>
        public void SetValidation(String name, ValidateDelegate validationDelegate)
        {
            if (Validations == null)
                Validations = new Dictionary<string, ValidateDelegate>();

            if (validationDelegate != null)
                Validations[name] = validationDelegate;
            else
                Validations.Remove(name);
        }

        /// <summary>
        /// Returns the %FilterDelegate$ for the given name, or null if it does not exist.
        /// </summary>
        /// <returns>The filter.</returns>
        /// <param name="name">Name.</param>
        public FilterDelegate GetFilter(String name) 
        { 
            FilterDelegate result = null;
            if (Filters != null)
            {
                result = Filters.Get(name);
            }
            if (result == null)
            {
                var filterCompiler = FilterCompiler;
                if (filterCompiler == null)
                {
                    return null;
                }

                var outLanguageList = new AList<string>();
                var sourceCode = GetDesignDocFunction(name, "filters", outLanguageList);

                if (sourceCode == null)
                {
                    return null;
                }

                var language = outLanguageList[0];

                var filter = filterCompiler(sourceCode, language);
                if (filter == null)
                {
                    Log.W(Database.Tag, string.Format("Filter {0} failed to compile", name));
                    return null;
                }

                SetFilter(name, filter);
                return filter;
            }
            return result;
        }

        /// <summary>
        /// Sets the %FilterDelegate% for the given name.  If <param name="filterDelegate"/> is null, the filter 
        /// with the given name is deleted. Before a %Revision% is replicated via a 
        /// push %Replication%, its filter delegate is called and given a chance to exclude it from the %Replication%.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="filterDelegate">Filter delegate.</param>
        public void SetFilter(String name, FilterDelegate filterDelegate) 
        { 
            if (Filters == null)
            {
                Filters = new Dictionary<String, FilterDelegate>();
            }
            if (filterDelegate != null)
            {
                Filters.Put(name, filterDelegate);
            }
            else
            {
                Collections.Remove(Filters, name);
            }
        }

        /// <summary>
        /// Runs the <see cref="Couchbase.Lite.RunAsyncDelegate"/>  asynchronously.
        /// </summary>
        /// <param name="runAsyncDelegate">Run async delegate.</param>
        public void RunAsync(RunAsyncDelegate runAsyncDelegate) 
        {
            Manager.RunAsync(runAsyncDelegate, this)
                .ContinueWith(task=>{
                if (task.Status != TaskStatus.RanToCompletion)
                    throw new CouchbaseLiteException(Tag, task.Exception);
                }, TaskScheduler.Current);
        }

        /// <summary>Runs the block within a transaction.</summary>
        /// <remarks>
        /// Runs the block within a transaction. If the block returns NO, the transaction is rolled back.
        /// Use this when performing bulk write operations like multiple inserts/updates;
        /// it saves the overhead of multiple SQLite commits, greatly improving performance.
        /// Does not commit the transaction if the code throws an Exception.
        /// TODO: the iOS version has a retry loop, so there should be one here too
        /// </remarks>
        /// <param name="transactionDelegate"></param>
        public Boolean RunInTransaction(RunInTransactionDelegate transactionDelegate)
        {
            var shouldCommit = true;

            BeginTransaction();

            try
            {
                shouldCommit = transactionDelegate();
            }
            catch (Exception e)
            {
                shouldCommit = false;
                Log.E(Database.Tag, e.ToString(), e);
                throw new RuntimeException(e);
            }
            finally
            {
                EndTransaction(shouldCommit);
            }

            return shouldCommit;
        }

        public Replication GetPushReplication(Uri url) 
        { 
            return Manager.ReplicationWithDatabase(this, url, true, true, false);
        }

        public Replication GetPullReplication(Uri url) 
        {
            return Manager.ReplicationWithDatabase(this, url, false, true, false);
        }

        public event EventHandler<DatabaseChangeEventArgs> Changed;

    #endregion
       
    #region Constants
        internal const String Tag = "Database";

        internal const String TagSql = "CBLSQL";
       
        const Int32 BigAttachmentLength = 16384;

        const Int32 MaxDocCacheSize = 50;

        internal readonly String Schema = string.Empty + "CREATE TABLE docs ( " + "        doc_id INTEGER PRIMARY KEY, "
                                      + "        docid TEXT UNIQUE NOT NULL); " + "    CREATE INDEX docs_docid ON docs(docid); "
                                      + "    CREATE TABLE revs ( " + "        sequence INTEGER PRIMARY KEY AUTOINCREMENT, "
                                      + "        doc_id INTEGER NOT NULL REFERENCES docs(doc_id) ON DELETE CASCADE, "
                                      + "        revid TEXT NOT NULL, " + "        parent INTEGER REFERENCES revs(sequence) ON DELETE SET NULL, "
                                      + "        current BOOLEAN, " + "        deleted BOOLEAN DEFAULT 0, " + "        json BLOB); "
                                      + "    CREATE INDEX revs_by_id ON revs(revid, doc_id); " + "    CREATE INDEX revs_current ON revs(doc_id, current); "
                                      + "    CREATE INDEX revs_parent ON revs(parent); " + "    CREATE TABLE localdocs ( "
                                      + "        docid TEXT UNIQUE NOT NULL, " + "        revid TEXT NOT NULL, " + "        json BLOB); "
                                      + "    CREATE INDEX localdocs_by_docid ON localdocs(docid); " + "    CREATE TABLE views ( "
                                      + "        view_id INTEGER PRIMARY KEY, " + "        name TEXT UNIQUE NOT NULL,"
                                      + "        version TEXT, " + "        lastsequence INTEGER DEFAULT 0); " + "    CREATE INDEX views_by_name ON views(name); "
                                      + "    CREATE TABLE maps ( " + "        view_id INTEGER NOT NULL REFERENCES views(view_id) ON DELETE CASCADE, "
                                      + "        sequence INTEGER NOT NULL REFERENCES revs(sequence) ON DELETE CASCADE, "
                                      + "        key TEXT NOT NULL COLLATE JSON, " + "        value TEXT); " + "    CREATE INDEX maps_keys on maps(view_id, key COLLATE JSON); "
                                      + "    CREATE TABLE attachments ( " + "        sequence INTEGER NOT NULL REFERENCES revs(sequence) ON DELETE CASCADE, "
                                      + "        filename TEXT NOT NULL, " + "        key BLOB NOT NULL, " + "        type TEXT, "
                                      + "        length INTEGER NOT NULL, " + "        revpos INTEGER DEFAULT 0); " +
                                      "    CREATE INDEX attachments_by_sequence on attachments(sequence, filename); "
                                      + "    CREATE TABLE replicators ( " + "        remote TEXT NOT NULL, " + "        push BOOLEAN, "
                                      + "        last_sequence TEXT, " + "        UNIQUE (remote, push)); " + "    PRAGMA user_version = 3";


    #endregion

    #region Non-Public Instance Members

        private Boolean open;
        private IDictionary<String, ValidateDelegate> Validations;
        private IDictionary<String, BlobStoreWriter> _pendingAttachmentsByDigest;
        private IDictionary<String, View> views;
        private Int32 transactionLevel;

        internal String Path { get; private set; }

        internal IList<Replication> ActiveReplicators { get; set; }

        internal SQLiteStorageEngine StorageEngine { get; set; }

        internal LruCache<String, Document> DocumentCache { get; set; }

        private IDictionary<String, FilterDelegate> Filters { get; set; }

        internal RevisionList GetAllRevisionsOfDocumentID (string id, bool onlyCurrent)
        {
            var docNumericId = GetDocNumericID(id);
            if (docNumericId < 0)
            {
                return null;
            }
            else
            {
                if (docNumericId == 0)
                {
                    return new RevisionList();
                }
                else
                {
                    return GetAllRevisionsOfDocumentID(id, docNumericId, onlyCurrent);
                }
            }
        }

        private RevisionList GetAllRevisionsOfDocumentID(string docId, long docNumericID, bool onlyCurrent)
        {
            var sql = onlyCurrent 
                      ? "SELECT sequence, revid, deleted FROM revs " + "WHERE doc_id=? AND current ORDER BY sequence DESC"
                      : "SELECT sequence, revid, deleted FROM revs " + "WHERE doc_id=? ORDER BY sequence DESC";

            var args = new [] { Convert.ToString (docNumericID) };
            var cursor = StorageEngine.RawQuery(sql, args);

            RevisionList result;
            try
            {
                cursor.MoveToNext();
                result = new RevisionList();
                while (!cursor.IsAfterLast())
                {
                    var rev = new RevisionInternal(docId, cursor.GetString(1), (cursor.GetInt(2) > 0), this);
                    rev.SetSequence(cursor.GetLong(0));
                    result.AddItem(rev);
                    cursor.MoveToNext();
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting all revisions of document", e);
                return null;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        private String GetDesignDocFunction(string fnName, string key, ICollection<string> outLanguageList)
        {
            var path = fnName.Split("/");
            if (path.Length != 2)
            {
                return null;
            }

            var docId = string.Format("_design/%s", path[0]);
            var rev = GetDocumentWithIDAndRev(docId, null, EnumSet.NoneOf<TDContentOptions>());
            if (rev == null)
            {
                return null;
            }

            var outLanguage = (string)rev.GetPropertyForKey("language");
            if (outLanguage != null)
            {
                outLanguageList.AddItem(outLanguage);
            }
            else
            {
                outLanguageList.AddItem("javascript");
            }

            var container = (IDictionary<String, Object>)rev.GetPropertyForKey(key);
            return (string)container.Get(path[1]);
        }

        internal Boolean Exists()
        {
            return new FilePath(Path).Exists();
        }

        internal static string MakeLocalDocumentId(string documentId)
        {
            return string.Format("_local/{0}", documentId);
        }

        private RevisionInternal PutLocalRevision(RevisionInternal revision, string prevRevID)
        {
            var docID = revision.GetDocId();
            if (!docID.StartsWith ("_local/", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new CouchbaseLiteException(StatusCode.BadRequest);
            }

            if (!revision.IsDeleted())
            {
                // PUT:
                string newRevID;
                var json = EncodeDocumentJSON(revision);

                if (prevRevID != null)
                {
                    int generation = RevisionInternal.GenerationFromRevID(prevRevID);
                    if (generation == 0)
                    {
                        throw new CouchbaseLiteException(StatusCode.BadRequest);
                    }
                    newRevID = Extensions.ToString(++generation) + "-local";

                    var values = new ContentValues();
                    values.Put("revid", newRevID);
                    values.Put("json", json);

                    var whereArgs = new [] { docID, prevRevID };
                    try
                    {
                        var rowsUpdated = StorageEngine.Update("localdocs", values, "docid=? AND revid=?", whereArgs);
                        if (rowsUpdated == 0)
                        {
                            throw new CouchbaseLiteException(StatusCode.Conflict);
                        }
                    }
                    catch (SQLException e)
                    {
                        throw new CouchbaseLiteException(e, StatusCode.InternalServerError);
                    }
                }
                else
                {
                    newRevID = "1-local";

                    var values = new ContentValues();
                    values.Put("docid", docID);
                    values.Put("revid", newRevID);
                    values.Put("json", json);

                    try
                    {
                        StorageEngine.InsertWithOnConflict("localdocs", null, values, SQLiteStorageEngine.ConflictIgnore);
                    }
                    catch (SQLException e)
                    {
                        throw new CouchbaseLiteException(e, StatusCode.InternalServerError);
                    }
                }
                return revision.CopyWithDocID(docID, newRevID);
            }
            else
            {
                // DELETE:
                DeleteLocalDocument(docID, prevRevID);
                return revision;
            }
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void DeleteLocalDocument(string docID, string revID)
        {
            if (docID == null)
            {
                throw new CouchbaseLiteException(StatusCode.BadRequest);
            }

            if (revID == null)
            {
                // Didn't specify a revision to delete: 404 or a 409, depending
                if (GetLocalDocument(docID, null) != null)
                {
                    throw new CouchbaseLiteException(StatusCode.Conflict);
                }
                else
                {
                    throw new CouchbaseLiteException(StatusCode.NotFound);
                }
            }

            var whereArgs = new [] { docID, revID };
            try
            {
                int rowsDeleted = StorageEngine.Delete("localdocs", "docid=? AND revid=?", whereArgs);
                if (rowsDeleted == 0)
                {
                    if (GetLocalDocument(docID, null) != null)
                    {
                        throw new CouchbaseLiteException(StatusCode.Conflict);
                    }
                    else
                    {
                        throw new CouchbaseLiteException(StatusCode.NotFound);
                    }
                }
            }
            catch (SQLException e)
            {
                throw new CouchbaseLiteException(e, StatusCode.InternalServerError);
            }
        }

        internal RevisionInternal GetLocalDocument(string docID, string revID)
        {
            // docID already should contain "_local/" prefix
            RevisionInternal result = null;
            Cursor cursor = null;
            try
            {
                var args = new [] { docID };
                cursor = StorageEngine.RawQuery("SELECT revid, json FROM localdocs WHERE docid=?", args); // TODO: Convert to ADO params.

                if (cursor.MoveToNext())
                {
                    var gotRevID = cursor.GetString(0);
                    if (revID != null && (!revID.Equals(gotRevID)))
                    {
                        return null;
                    }

                    var json = cursor.GetBlob(1);
                    IDictionary<string, object> properties = null;
                    try
                    {
                        properties = Manager.GetObjectMapper().ReadValue<IDictionary<String, Object>>(json);
                        properties.Put("_id", docID);
                        properties.Put("_rev", gotRevID);
                        result = new RevisionInternal(docID, gotRevID, false, this);
                        result.SetProperties(properties);
                    }
                    catch (Exception e)
                    {
                        Log.W(Database.Tag, "Error parsing local doc JSON", e);
                        return null;
                    }
                }
                return result;
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting local document", e);
                return null;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
        }

        /// <summary>Inserts an already-existing revision replicated from a remote sqliteDb.</summary>
        /// <remarks>
        /// Inserts an already-existing revision replicated from a remote sqliteDb.
        /// It must already have a revision ID. This may create a conflict! The revision's history must be given; ancestor revision IDs that don't already exist locally will create phantom revisions with no content.
        /// </remarks>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void ForceInsert(RevisionInternal rev, IList<string> revHistory, Uri source)
        {
            var docId = rev.GetDocId();
            var revId = rev.GetRevId();
            if (!IsValidDocumentId(docId) || (revId == null))
            {
                throw new CouchbaseLiteException(StatusCode.BadRequest);
            }
            int historyCount = 0;
            if (revHistory != null)
            {
                historyCount = revHistory.Count;
            }
            if (historyCount == 0)
            {
                revHistory = new AList<string>();
                revHistory.AddItem(revId);
                historyCount = 1;
            }
            else
            {
                if (!revHistory[0].Equals(rev.GetRevId()))
                {
                    throw new CouchbaseLiteException(StatusCode.BadRequest);
                }
            }
            bool success = false;
            BeginTransaction();
            try
            {
                // First look up all locally-known revisions of this document:
                long docNumericID = GetOrInsertDocNumericID(docId);
                RevisionList localRevs = GetAllRevisionsOfDocumentID(docId, docNumericID, false);
                if (localRevs == null)
                {
                    throw new CouchbaseLiteException(StatusCode.InternalServerError);
                }
                // Walk through the remote history in chronological order, matching each revision ID to
                // a local revision. When the list diverges, start creating blank local revisions to fill
                // in the local history:
                long sequence = 0;
                long localParentSequence = 0;
                for (int i = revHistory.Count - 1; i >= 0; --i)
                {
                    revId = revHistory[i];
                    RevisionInternal localRev = localRevs.RevWithDocIdAndRevId(docId, revId);
                    if (localRev != null)
                    {
                        // This revision is known locally. Remember its sequence as the parent of the next one:
                        sequence = localRev.GetSequence();
                        System.Diagnostics.Debug.Assert((sequence > 0));
                        localParentSequence = sequence;
                    }
                    else
                    {
                        // This revision isn't known, so add it:
                        RevisionInternal newRev;
                        IEnumerable<Byte> data = null;
                        bool current = false;
                        if (i == 0)
                        {
                            // Hey, this is the leaf revision we're inserting:
                            newRev = rev;
                            if (!rev.IsDeleted())
                            {
                                data = EncodeDocumentJSON(rev);
                                if (data == null)
                                {
                                    throw new CouchbaseLiteException(StatusCode.BadRequest);
                                }
                            }
                            current = true;
                        }
                        else
                        {
                            // It's an intermediate parent, so insert a stub:
                            newRev = new RevisionInternal(docId, revId, false, this);
                        }
                        // Insert it:
                        sequence = InsertRevision(newRev, docNumericID, sequence, current, data);
                        if (sequence <= 0)
                        {
                            throw new CouchbaseLiteException(StatusCode.InternalServerError);
                        }
                        if (i == 0)
                        {
                            // Write any changed attachments for the new revision. As the parent sequence use
                            // the latest local revision (this is to copy attachments from):
                            IDictionary<string, AttachmentInternal> attachments = GetAttachmentsFromRevision(
                                rev);
                            if (attachments != null)
                            {
                                ProcessAttachmentsForRevision(attachments, rev, localParentSequence);
                                StubOutAttachmentsInRevision(attachments, rev);
                            }
                        }
                    }
                }
                // Mark the latest local rev as no longer current:
                if (localParentSequence > 0 && localParentSequence != sequence)
                {
                    ContentValues args = new ContentValues();
                    args.Put("current", 0);
                    string[] whereArgs = new string[] { System.Convert.ToString(localParentSequence) };
                    try
                    {
                        StorageEngine.Update("revs", args, "sequence=?", whereArgs); // TODO: Convert to ADO Params.
                    }
                    catch (SQLException)
                    {
                        throw new CouchbaseLiteException(StatusCode.InternalServerError);
                    }
                }
                success = true;
            }
            catch (SQLException)
            {
                throw new CouchbaseLiteException(StatusCode.InternalServerError);
            }
            finally
            {
                EndTransaction(success);
            }
            // Notify and return:
            const bool inConflict = false; // NOTE.ZJG: Not sure that is right, but it's what came over via sharpen.
            // TODO: can we be in conflict here?
            RevisionInternal winningRev = null;
            // TODO: what should we do here?
            NotifyChange(rev, winningRev, source, inConflict);
        }

        private Int64 GetOrInsertDocNumericID(String docId)
        {
            var docNumericId = GetDocNumericID(docId);
            if (docNumericId == 0)
            {
                docNumericId = InsertDocumentID(docId);
            }
            return docNumericId;
        }

        /// <summary>Deletes obsolete attachments from the sqliteDb and blob store.</summary>
        private Status GarbageCollectAttachments()
        {
            // First delete attachment rows for already-cleared revisions:
            // OPT: Could start after last sequence# we GC'd up to
            try
            {
                StorageEngine.ExecSQL("DELETE FROM attachments WHERE sequence IN " + "(SELECT sequence from revs WHERE json IS null)"
                );
            }
            catch (SQLException e)
            {
                Log.E(Couchbase.Lite.Database.Tag, "Error deleting attachments", e);
            }
            // Now collect all remaining attachment IDs and tell the store to delete all but these:
            Cursor cursor = null;
            try
            {
                cursor = StorageEngine.RawQuery("SELECT DISTINCT key FROM attachments", null);
                cursor.MoveToNext();

                var allKeys = new AList<BlobKey>();
                while (!cursor.IsAfterLast())
                {
                    var key = new BlobKey(cursor.GetBlob(0));
                    allKeys.AddItem(key);
                    cursor.MoveToNext();
                }

                var numDeleted = Attachments.DeleteBlobsExceptWithKeys(allKeys);
                if (numDeleted < 0)
                {
                    return new Status(StatusCode.InternalServerError);
                }

                Log.V(Database.Tag, "Deleted " + numDeleted + " attachments");

                return new Status(StatusCode.Ok);
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error finding attachment keys in use", e);
                return new Status(StatusCode.InternalServerError);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
        }

        internal Boolean SetLastSequence(String lastSequence, Uri url, Boolean push)
        {
            var values = new ContentValues();
            values.Put("remote", url.ToString());
            values.Put("push", push);
            values.Put("last_sequence", lastSequence);
            var newId = StorageEngine.InsertWithOnConflict("replicators", null, values, SQLiteStorageEngine.ConflictReplace);
            return (newId == -1);
        }


        internal String LastSequenceWithRemoteURL(Uri url, Boolean push)
        {
            Cursor cursor = null;
            string result = null;
            try
            {
                var args = new [] { url.ToString(), (push ? 1 : 0).ToString() };
                cursor = StorageEngine.RawQuery("SELECT last_sequence FROM replicators WHERE remote=? AND push=?"
                    , args);
                if (cursor.MoveToNext())
                {
                    result = cursor.GetString(0);
                }
            }
            catch (SQLException e)
            {
                Log.E(Couchbase.Lite.Database.Tag, "Error getting last sequence", e);
                return null;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        private IDictionary<String, BlobStoreWriter> PendingAttachmentsByDigest
        {
            get {
                return _pendingAttachmentsByDigest ?? (_pendingAttachmentsByDigest = new Dictionary<String, BlobStoreWriter>());
            }
            set {
                _pendingAttachmentsByDigest = value;
            }
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal IEnumerable<QueryRow> QueryViewNamed(String viewName, QueryOptions options, IList<Int64> outLastSequence)
        {
            var before = Runtime.CurrentTimeMillis();
            var lastSequence = 0L;
            IEnumerable<QueryRow> rows;

            if (!String.IsNullOrEmpty (viewName)) {
                var view = GetView (viewName);
                if (view == null)
                    throw new CouchbaseLiteException (StatusCode.NotFound);

                lastSequence = view.LastSequenceIndexed;
                if (options.GetStale () == IndexUpdateMode.Never || lastSequence <= 0) {
                    view.UpdateIndex ();
                    lastSequence = view.LastSequenceIndexed;
                } else {
                    if (options.GetStale () == IndexUpdateMode.After 
                        && lastSequence < GetLastSequenceNumber())
                        // NOTE: The exception is handled inside the thread.
                        // TODO: Consider using the async keyword instead.
                        Manager.RunAsync(()=>{
                            try
                            {
                                view.UpdateIndex();
                            }
                            catch (CouchbaseLiteException e)
                            {
                                Log.E(Database.Tag, "Error updating view index on background thread", e);
                            }
                        });

                }
                rows = view.QueryWithOptions (options);
            } else {
                // nil view means query _all_docs
                // note: this is a little kludgy, but we have to pull out the "rows" field from the
                // result dictionary because that's what we want.  should be refactored, but
                // it's a little tricky, so postponing.
                var allDocsResult = GetAllDocs (options);
                rows = (IList<QueryRow>)allDocsResult.Get ("rows");
                lastSequence = GetLastSequenceNumber ();
            }
            outLastSequence.AddItem(lastSequence);
            var delta = Runtime.CurrentTimeMillis() - before;
            Log.D(Database.Tag, String.Format("Query view {0} completed in {1} milliseconds", viewName, delta));
            return rows;
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal IDictionary<String, Object> GetAllDocs(QueryOptions options)
        {
            var result = new Dictionary<String, Object>();
            var rows = new AList<QueryRow>();
            if (options == null)
                options = new QueryOptions();

            var updateSeq = 0L;
            if (options.IsUpdateSeq())
            {
                updateSeq = GetLastSequenceNumber();
            }

            // TODO: needs to be atomic with the following SELECT
            var sql = new StringBuilder("SELECT revs.doc_id, docid, revid, sequence");
            if (options.IsIncludeDocs())
            {
                sql.Append(", json");
            }
            if (options.IsIncludeDeletedDocs())
            {
                sql.Append(", deleted");
            }
            sql.Append(" FROM revs, docs WHERE");

            if (options.GetKeys() != null)
            {
                if (options.GetKeys().Count() == 0)
                {
                    return result;
                }
                var commaSeperatedIds = JoinQuotedObjects(options.GetKeys());
                sql.Append(String.Format(" revs.doc_id IN (SELECT doc_id FROM docs WHERE docid IN ({0})) AND", commaSeperatedIds));
            }
            sql.Append(" docs.doc_id = revs.doc_id AND current=1"); // TODO: Convert to ADO params.

            if (!options.IsIncludeDeletedDocs())
            {
                sql.Append(" AND deleted=0"); // TODO: Convert to ADO params.
            }

            var args = new AList<String>();
            var minKey = options.GetStartKey();
            var maxKey = options.GetEndKey();
            var inclusiveMin = true;
            var inclusiveMax = options.IsInclusiveEnd();

            if (options.IsDescending())
            {
                minKey = maxKey;
                maxKey = options.GetStartKey();
                inclusiveMin = inclusiveMax;
                inclusiveMax = true;
            }
            if (minKey != null)
            {
                System.Diagnostics.Debug.Assert((minKey is String));
                sql.Append((inclusiveMin ? " AND docid >= ?" : " AND docid > ?")); // TODO: Convert to ADO params.
                args.AddItem((string)minKey);
            }
            if (maxKey != null)
            {
                System.Diagnostics.Debug.Assert((maxKey is string));
                sql.Append((inclusiveMax ? " AND docid <= ?" : " AND docid < ?")); // TODO: Convert to ADO params.
                args.AddItem((string)maxKey);
            }
            sql.Append(String.Format(" ORDER BY docid {0}, {1} revid DESC LIMIT ? OFFSET ?", 
                options.IsDescending() ? "DESC" : "ASC", 
                options.IsIncludeDeletedDocs() ? "deleted ASC," : String.Empty)); // TODO: Convert to ADO params.
            args.AddItem(options.GetLimit().ToString());
            args.AddItem(options.GetSkip().ToString());
            Cursor cursor = null;
            var lastDocID = 0L;
            var totalRows = 0;
            var docs = new Dictionary<String, QueryRow>();
            try
            {
                cursor = StorageEngine.RawQuery(
                    sql.ToString(),
                    Collections.ToArray(args, new string[args.Count])
                );

                cursor.MoveToNext();

                while (!cursor.IsAfterLast())
                {
                    totalRows++;
                    var docNumericID = cursor.GetLong(0);
                    if (docNumericID == lastDocID)
                    {
                        cursor.MoveToNext();
                        continue;
                    }

                    lastDocID = docNumericID;
                    var docId = cursor.GetString(1);
                    var revId = cursor.GetString(2);
                    var sequenceNumber = cursor.GetLong(3);
                    var deleted = options.IsIncludeDeletedDocs() && cursor.GetInt(GetDeletedColumnIndex(options)) > 0;
                    IDictionary<string, object> docContents = null;
                    if (options.IsIncludeDocs())
                    {
                        var json = cursor.GetBlob(4);
                        docContents = DocumentPropertiesFromJSON(json, docId, revId, deleted, sequenceNumber, options.GetContentOptions());
                    }
                    var value = new Dictionary<string, object>();
                    value.Put("rev", revId);
                    if (options.IsIncludeDeletedDocs())
                    {
                        value.Put("deleted", deleted);
                    }
                    var change = new QueryRow(docId, sequenceNumber, docId, value, docContents);
                    change.Database = this;

                    if (options.GetKeys() != null)
                    {
                        docs.Put(docId, change);
                    }
                    else
                    {
                        rows.AddItem(change);
                    }
                    cursor.MoveToNext();
                }
                if (options.GetKeys() != null)
                {
                    foreach (var docIdObject in options.GetKeys())
                    {
                        if (docIdObject is string)
                        {
                            var docId = (string)docIdObject;
                            var change = docs.Get(docId);
                            if (change == null)
                            {
                                var value = new Dictionary<string, object>();
                                var docNumericID = GetDocNumericID(docId);
                                if (docNumericID > 0)
                                {
                                    bool deleted;
                                    var outIsDeleted = new AList<bool>();
                                    var outIsConflict = new AList<bool>();
                                    var revId = WinningRevIDOfDoc(docNumericID, outIsDeleted, outIsConflict);
                                    if (outIsDeleted.Count > 0)
                                    {
                                        deleted = true;
                                    }
                                    if (revId != null)
                                    {
                                        value.Put("rev", revId);
                                        value.Put("deleted", true);
                                    }
                                }
                                change = new QueryRow((value != null ? docId : null), 0, docId, value, null);
                                change.Database = this;
                            }
                            rows.AddItem(change);
                        }
                    }
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting all docs", e);
                throw new CouchbaseLiteException("Error getting all docs", e, new Status(StatusCode.InternalServerError));
            }
            finally
            {
                if (cursor != null)
                    cursor.Close();
            }
            result.Put("rows", rows);
            result.Put("total_rows", totalRows);
            result.Put("offset", options.GetSkip());
            if (updateSeq != 0)
            {
                result.Put("update_seq", updateSeq);
            }
            return result;
        }

        /// <summary>Returns the rev ID of the 'winning' revision of this document, and whether it's deleted.</summary>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal String WinningRevIDOfDoc(Int64 docNumericId, IList<Boolean> outIsDeleted, IList<Boolean> outIsConflict)
        {
            Cursor cursor = null;
            var args = new [] { Convert.ToString(docNumericId) };
            String revId = null;
            var sql = "SELECT revid, deleted FROM revs" + " WHERE doc_id=? and current=1" 
                      + " ORDER BY deleted asc, revid desc LIMIT 2"; // TODO: Convert to ADO params.

            try
            {
                cursor = StorageEngine.RawQuery(sql, args);
                cursor.MoveToNext();

                if (!cursor.IsAfterLast())
                {
                    revId = cursor.GetString(0);
                    var deleted = cursor.GetInt(1) > 0;
                    if (deleted)
                    {
                        outIsDeleted.AddItem(true);
                    }

                    // The document is in conflict if there are two+ result rows that are not deletions.
                    var hasNextResult = cursor.MoveToNext();
                    var isNextDeleted = cursor.GetInt(1) > 0;
                    var isInConflict = !deleted && hasNextResult && isNextDeleted;

                    if (isInConflict)
                    {
                        outIsConflict.AddItem(true);
                    }
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error", e);
                throw new CouchbaseLiteException("Error", e, new Status(StatusCode.InternalServerError));
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return revId;
        }

        internal IDictionary<String, Object> DocumentPropertiesFromJSON(IEnumerable<Byte> json, String docId, String revId, Boolean deleted, Int64 sequence, EnumSet<TDContentOptions> contentOptions)
        {
            var rev = new RevisionInternal(docId, revId, deleted, this);
            rev.SetSequence(sequence);

            IDictionary<String, Object> extra = ExtraPropertiesForRevision(rev, contentOptions);
            if (json == null)
            {
                return extra;
            }

            IDictionary<String, Object> docProperties = null;
            try
            {
                docProperties = Manager.GetObjectMapper().ReadValue<IDictionary<string, object>>(json);
                docProperties.PutAll(extra);
            }
            catch (Exception e)
            {
                Log.E(Database.Tag, "Error serializing properties to JSON", e);
            }
            return docProperties;
        }


        /// <summary>Hack because cursor interface does not support cursor.getColumnIndex("deleted") yet.
        ///     </summary>
        internal Int32 GetDeletedColumnIndex(QueryOptions options)
        {
            System.Diagnostics.Debug.Assert(options != null);

            return options.IsIncludeDocs() ? 5 : 4;
        }

        internal static String JoinQuotedObjects(IEnumerable<Object> objects)
        {
            var strings = new AList<String>();
            foreach (var obj in objects)
            {
                strings.AddItem(obj != null ? obj.ToString() : null);
            }
            return JoinQuoted(strings);
        }

        internal static String JoinQuoted(IList<String> strings)
        {
            if (strings.Count == 0)
            {
                return String.Empty;
            }

            var result = "'";
            var first = true;

            foreach (string str in strings)
            {
                if (first)
                    first = false;
                else
                    result = result + "','";

                result = result + Quote(str);
            }

            result = result + "'";

            return result;
        }

        internal static string Quote(string str)
        {
            return str.Replace("'", "''");
        }

        internal View RegisterView(View view)
        {
            if (view == null)
            {
                return null;
            }
            if (views == null)
            {
                views = new Dictionary<string, View>();
            }
            views.Put(view.Name, view);
            return view;
        }

        internal View MakeAnonymousView()
        {
            for (var i = 0; true; ++i)
            {
                var name = String.Format("anon%d", i);
                var existing = GetExistingView(name);
                if (existing == null)
                {
                    // this name has not been used yet, so let's use it
                    return GetView(name);
                }
            }
        }

        internal Status DeleteViewNamed(String name)
        {
            var result = new Status(StatusCode.InternalServerError);
            try
            {
                var whereArgs = new [] { name };
                var rowsAffected = StorageEngine.Delete("views", "name=?", whereArgs);

                if (rowsAffected > 0)
                {
                    result.SetCode(StatusCode.Ok);
                }
                else
                {
                    result.SetCode(StatusCode.NotFound);
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error deleting view", e);
            }
            return result;
        }

        internal RevisionInternal GetParentRevision(RevisionInternal rev)
        {
            // First get the parent's sequence:
            var seq = rev.GetSequence();
            if (seq > 0)
            {
                seq = LongForQuery("SELECT parent FROM revs WHERE sequence=?", new string[] { Convert.ToString(seq) }); // TODO: Convert to ADO parameters
            }
            else
            {
                var docNumericID = GetDocNumericID(rev.GetDocId());
                if (docNumericID <= 0)
                {
                    return null;
                }
                var args = new [] { Convert.ToString(docNumericID), rev.GetRevId() };
                seq = LongForQuery("SELECT parent FROM revs WHERE doc_id=? and revid=?", args); // TODO: Convert to ADO parameters
            }
            if (seq == 0)
            {
                return null;
            }

            // Now get its revID and deletion status:
            RevisionInternal result = null;
            var args_1 = new [] { Convert.ToString(seq) };
            var queryString = "SELECT revid, deleted FROM revs WHERE sequence=?"; // TODO: Convert to ADO parameters

            Cursor cursor = null;
            try
            {
                cursor = StorageEngine.RawQuery(queryString, args_1);
                if (cursor.MoveToNext())
                {
                    string revId = cursor.GetString(0);
                    bool deleted = (cursor.GetInt(1) > 0);
                    result = new RevisionInternal(rev.GetDocId(), revId, deleted, this);
                    result.SetSequence(seq);
                }
            }
            finally
            {
                cursor.Close();
            }
            return result;
        }

        /// <summary>The latest sequence number used.</summary>
        /// <remarks>
        /// The latest sequence number used.  Every new revision is assigned a new sequence number,
        /// so this property increases monotonically as changes are made to the database. It can be
        /// used to check whether the database has changed between two points in time.
        /// </remarks>
        internal Int64 GetLastSequenceNumber()
        {
            string sql = "SELECT MAX(sequence) FROM revs";
            Cursor cursor = null;
            long result = 0;
            try
            {
                cursor = StorageEngine.RawQuery(sql, null);
                if (cursor.MoveToNext())
                {
                    result = cursor.GetLong(0);
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting last sequence", e);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }
               
        /// <exception cref="Couchbase.Lite.Storage.SQLException"></exception>
        internal Int64 LongForQuery(string sqlQuery, IEnumerable<string> args)
        {
            Cursor cursor = null;
            var result = 0L;
            try
            {
                cursor = StorageEngine.RawQuery(sqlQuery, args);
                if (cursor.MoveToNext())
                {
                    result = cursor.GetLong(0);
                }
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        /// <summary>Purges specific revisions, which deletes them completely from the local database _without_ adding a "tombstone" revision.
        ///     </summary>
        /// <remarks>
        /// Purges specific revisions, which deletes them completely from the local database _without_ adding a "tombstone" revision. It's as though they were never there.
        /// This operation is described here: http://wiki.apache.org/couchdb/Purge_Documents
        /// </remarks>
        /// <param name="docsToRevs">A dictionary mapping document IDs to arrays of revision IDs.
        ///     </param>
        /// <resultOn>success will point to an NSDictionary with the same form as docsToRev, containing the doc/revision IDs that were actually removed.
        ///     </resultOn>
        internal IDictionary<String, Object> PurgeRevisions(IDictionary<String, IList<String>> docsToRevs)
        {
            var result = new Dictionary<String, Object>();
            RunInTransaction(() => PurgeRevisionsTask(this, docsToRevs, result));
            // no such document, skip it
            // Delete all revisions if magic "*" revision ID is given:
            // Iterate over all the revisions of the doc, in reverse sequence order.
            // Keep track of all the sequences to delete, i.e. the given revs and ancestors,
            // but not any non-given leaf revs or their ancestors.
            // Purge it and maybe its parent:
            // Keep it and its parent:
            // Now delete the sequences to be purged.
            return result;
        }

        internal void RemoveDocumentFromCache(Document document)
        {
            DocumentCache.Remove(document.Id);
        }

        internal BlobStoreWriter AttachmentWriter { get; set; }

        private  BlobStore Attachments { get; set; }

        internal String PrivateUUID ()
        {
            string result = null;
            Cursor cursor = null;
            try
            {
                cursor = StorageEngine.RawQuery("SELECT value FROM info WHERE key='privateUUID'", null);
                if (cursor.MoveToNext())
                {
                    result = cursor.GetString(0);
                }
            }
            catch (SQLException e)
            {
                Log.E(Tag, "Error querying privateUUID", e);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        internal String PublicUUID()
        {
            string result = null;
            Cursor cursor = null;
            try
            {
                cursor = StorageEngine.RawQuery("SELECT value FROM info WHERE key='publicUUID'", null);
                if (cursor.MoveToNext())
                {
                    result = cursor.GetString(0);
                }
            }
            catch (SQLException e)
            {
                Log.E(Tag, "Error querying privateUUID", e);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        internal BlobStore GetAttachments()
        {
            return Attachments;
        }

        internal BlobStoreWriter GetAttachmentWriter()
        {
            return new BlobStoreWriter(GetAttachments());
        }

        internal Boolean ReplaceUUIDs()
        {
            var query = "UPDATE INFO SET value='" + Misc.TDCreateUUID() + "' where key = 'privateUUID';";

            try
            {
                StorageEngine.ExecSQL(query);
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error updating UUIDs", e);
                return false;
            }

            query = "UPDATE INFO SET value='" + Misc.TDCreateUUID() + "' where key = 'publicUUID';";

            try
            {
                StorageEngine.ExecSQL(query);
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error updating UUIDs", e);
                return false;
            }

            return true;
        }

        internal Attachment GetAttachmentForSequence (long sequence, string filename)
        {
            System.Diagnostics.Debug.Assert((sequence > 0));
            System.Diagnostics.Debug.Assert((filename != null));

            Cursor cursor = null;
            string[] args = new string[] { System.Convert.ToString(sequence), filename };
            try
            {
                cursor = StorageEngine.RawQuery("SELECT key, type FROM attachments WHERE sequence=? AND filename=?", args);

                if (!cursor.MoveToNext())
                {
                    throw new CouchbaseLiteException(StatusCode.NotFound);
                }
                byte[] keyData = cursor.GetBlob(0);
                //TODO add checks on key here? (ios version)
                BlobKey key = new BlobKey(keyData);
                InputStream contentStream = Attachments.BlobStreamForKey(key);
                if (contentStream == null)
                {
                    Log.E(Couchbase.Lite.Database.Tag, "Failed to load attachment");
                    throw new CouchbaseLiteException(StatusCode.InternalServerError);
                }
                else
                {
                    Attachment result = new Attachment(contentStream, cursor.GetString(1));
                    result.Compressed = Attachments.IsGZipped(key);
                    return result;
                }
            }
            catch (SQLException)
            {
                throw new CouchbaseLiteException(StatusCode.InternalServerError);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
        }

        internal void RememberAttachmentWriter (BlobStoreWriter writer)
        {
            var digest = writer.MD5DigestString();
            PendingAttachmentsByDigest[digest] = writer;
        }

        internal Int64 GetDocNumericID(string docId)
        {
            Cursor cursor = null;
            string[] args = new string[] { docId };
            long result = -1;
            try
            {
                cursor = StorageEngine.RawQuery("SELECT doc_id FROM docs WHERE docid=?", args);
                if (cursor.MoveToNext())
                {
                    result = cursor.GetLong(0);
                }
                else
                {
                    result = 0;
                }
            }
            catch (Exception e)
            {
                Log.E(Database.Tag, "Error getting doc numeric id", e);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        /// <summary>Begins a database transaction.</summary>
        /// <remarks>
        /// Begins a database transaction. Transactions can nest.
        /// Every beginTransaction() must be balanced by a later endTransaction()
        /// </remarks>
        internal Boolean BeginTransaction()
        {
            // TODO: Implement Database.BeginTransaction.
            throw new NotImplementedException();
//            try
//            {
//                StorageEngine.BeginTransaction();
//                ++transactionLevel;
//                Log.I(Couchbase.Lite.Database.TagSql, Sharpen.Thread.CurrentThread().GetName(
//                ) + " Begin transaction (level " + Sharpen.Extensions.ToString(transactionLevel)
//                      + ")");
//            }
//            catch (SQLException e)
//            {
//                Log.E(Couchbase.Lite.Database.Tag, Sharpen.Thread.CurrentThread().GetName() +
//                      " Error calling beginTransaction()", e);
//                return false;
//            }
//            return true;
        }

        /// <summary>Commits or aborts (rolls back) a transaction.</summary>
        /// <param name="commit">If true, commits; if false, aborts and rolls back, undoing all changes made since the matching -beginTransaction call, *including* any committed nested transactions.
        ///     </param>
        internal Boolean EndTransaction(bool commit)
        {
            // TODO: Implement Database.BeginTransaction.
            throw new NotImplementedException();
//            System.Diagnostics.Debug.Assert((transactionLevel > 0));
//            if (commit)
//            {
//                Log.I(Couchbase.Lite.Database.TagSql, Sharpen.Thread.CurrentThread().GetName(
//                ) + " Committing transaction (level " + Sharpen.Extensions.ToString(transactionLevel
//                                                                                       ) + ")");
//                StorageEngine.SetTransactionSuccessful();
//                StorageEngine.EndTransaction();
//            }
//            else
//            {
//                Log.I(TagSql, Sharpen.Thread.CurrentThread().GetName() + " CANCEL transaction (level "
//                      + Sharpen.Extensions.ToString(transactionLevel) + ")");
//                try
//                {
//                    StorageEngine.EndTransaction();
//                }
//                catch (SQLException e)
//                {
//                    Log.E(Couchbase.Lite.Database.Tag, Sharpen.Thread.CurrentThread().GetName() +
//                          " Error calling endTransaction()", e);
//                    return false;
//                }
//            }
//            --transactionLevel;
//            return true;
        }

        internal static Boolean PurgeRevisionsTask(Database enclosingDatabase, IDictionary<String, IList<String>> docsToRevs, IDictionary<String, Object> result)
        {
            foreach (string docID in docsToRevs.Keys)
            {
                long docNumericID = enclosingDatabase.GetDocNumericID(docID);
                if (docNumericID == -1)
                {
                    continue;
                }
                IList<string> revsPurged = null;
                var revIDs = docsToRevs [docID];
                if (revIDs == null)
                {
                    return false;
                }
                else
                {
                    if (revIDs.Count == 0)
                    {
                        revsPurged = new AList<string>();
                    }
                    else
                    {
                        if (revIDs.Contains("*"))
                        {
                            try
                            {
                                string[] args = new string[] { System.Convert.ToString(docNumericID) };
                                enclosingDatabase.StorageEngine.ExecSQL("DELETE FROM revs WHERE doc_id=?", args);
                            }
                            catch (SQLException e)
                            {
                                Log.E(Database.Tag, "Error deleting revisions", e);
                                return false;
                            }
                            revsPurged = new AList<string>();
                            revsPurged.AddItem("*");
                        }
                        else
                        {
                            Cursor cursor = null;
                            try
                            {
                                string[] args = new string[] { System.Convert.ToString(docNumericID) };
                                string queryString = "SELECT revid, sequence, parent FROM revs WHERE doc_id=? ORDER BY sequence DESC";
                                cursor = enclosingDatabase.StorageEngine.RawQuery(queryString, args);
                                if (!cursor.MoveToNext())
                                {
                                    Log.W(Database.Tag, "No results for query: " + queryString);
                                    return false;
                                }
                                var seqsToPurge = new HashSet<long>();
                                var seqsToKeep = new HashSet<long>();
                                var revsToPurge = new HashSet<string>();
                                while (!cursor.IsAfterLast())
                                {
                                    string revID = cursor.GetString(0);
                                    long sequence = cursor.GetLong(1);
                                    long parent = cursor.GetLong(2);
                                    if (seqsToPurge.Contains(sequence) || revIDs.Contains(revID) && !seqsToKeep.Contains
                                            (sequence))
                                    {
                                        seqsToPurge.AddItem(sequence);
                                        revsToPurge.AddItem(revID);
                                        if (parent > 0)
                                        {
                                            seqsToPurge.AddItem(parent);
                                        }
                                    }
                                    else
                                    {
                                        seqsToPurge.Remove(sequence);
                                        revsToPurge.Remove(revID);
                                        seqsToKeep.AddItem(parent);
                                    }
                                    cursor.MoveToNext();
                                }
                                seqsToPurge.RemoveAll(seqsToKeep);
                                Log.I(Database.Tag, String.Format("Purging doc '{0}' revs ({1}); asked for ({2})", docID, revsToPurge, revIDs));
                                if (seqsToPurge.Count > 0)
                                {
                                    string seqsToPurgeList = String.Join(",", seqsToPurge);
                                    string sql = string.Format("DELETE FROM revs WHERE sequence in ({0})", seqsToPurgeList);
                                    try
                                    {
                                        enclosingDatabase.StorageEngine.ExecSQL(sql);
                                    }
                                    catch (SQLException e)
                                    {
                                        Log.E(Database.Tag, "Error deleting revisions via: " + sql, e);
                                        return false;
                                    }
                                }
                                Collections.AddAll(revsPurged, revsToPurge);
                            }
                            catch (SQLException e)
                            {
                                Log.E(Database.Tag, "Error getting revisions", e);
                                return false;
                            }
                            finally
                            {
                                if (cursor != null)
                                {
                                    cursor.Close();
                                }
                            }
                        }
                    }
                }
                result.Put(docID, revsPurged);
            }
            return true;
        }

        internal RevisionInternal GetDocumentWithIDAndRev(String id, String rev, EnumSet<TDContentOptions> contentOptions)
        {
            RevisionInternal result = null;
            string sql;
            Cursor cursor = null;
            try
            {
                cursor = null;
                string cols = "revid, deleted, sequence";
                if (!contentOptions.Contains(TDContentOptions.TDNoBody))
                {
                    cols += ", json";
                }
                if (rev != null)
                {
                    sql = "SELECT " + cols + " FROM revs, docs WHERE docs.docid=? AND revs.doc_id=docs.doc_id AND revid=? LIMIT 1";
                    string[] args = new string[] { id, rev };
                    cursor = StorageEngine.RawQuery(sql, args);
                }
                else
                {
                    sql = "SELECT " + cols + " FROM revs, docs WHERE docs.docid=? AND revs.doc_id=docs.doc_id and current=1 and deleted=0 ORDER BY revid DESC LIMIT 1";
                    string[] args = new string[] { id };
                    cursor = StorageEngine.RawQuery(sql, args);
                }
                if (cursor.MoveToNext())
                {
                    if (rev == null)
                    {
                        rev = cursor.GetString(0);
                    }
                    bool deleted = (cursor.GetInt(1) > 0);
                    result = new RevisionInternal(id, rev, deleted, this);
                    result.SetSequence(cursor.GetLong(2));
                    if (!contentOptions.Equals(EnumSet.Of(TDContentOptions.TDNoBody)))
                    {
                        byte[] json = null;
                        if (!contentOptions.Contains(TDContentOptions.TDNoBody))
                        {
                            json = cursor.GetBlob(3);
                        }
                        ExpandStoredJSONIntoRevisionWithAttachments(json, result, contentOptions);
                    }
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting document with id and rev", e);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        /// <summary>Inserts the _id, _rev and _attachments properties into the JSON data and stores it in rev.
        ///     </summary>
        /// <remarks>
        /// Inserts the _id, _rev and _attachments properties into the JSON data and stores it in rev.
        /// Rev must already have its revID and sequence properties set.
        /// </remarks>
        internal void ExpandStoredJSONIntoRevisionWithAttachments(IEnumerable<Byte> json, RevisionInternal rev, EnumSet<TDContentOptions> contentOptions)
        {
            var extra = ExtraPropertiesForRevision(rev, contentOptions);

            if (json != null)
            {
                rev.SetJson(AppendDictToJSON(json, extra));
            }
            else
            {
                rev.SetProperties(extra);
            }
        }

        /// <summary>Inserts the _id, _rev and _attachments properties into the JSON data and stores it in rev.
        ///     </summary>
        /// <remarks>
        /// Inserts the _id, _rev and _attachments properties into the JSON data and stores it in rev.
        /// Rev must already have its revID and sequence properties set.
        /// </remarks>
        internal IDictionary<String, Object> ExtraPropertiesForRevision(RevisionInternal rev, EnumSet<TDContentOptions> contentOptions)
        {
            string docId = rev.GetDocId();
            string revId = rev.GetRevId();
            long sequenceNumber = rev.GetSequence();
            System.Diagnostics.Debug.Assert((revId != null));
            System.Diagnostics.Debug.Assert((sequenceNumber > 0));
            // Get attachment metadata, and optionally the contents:
            IDictionary<string, object> attachmentsDict = GetAttachmentsDictForSequenceWithContent
                                                          (sequenceNumber, contentOptions);
            // Get more optional stuff to put in the properties:
            //OPT: This probably ends up making redundant SQL queries if multiple options are enabled.
            var localSeq = 0L;
            if (contentOptions.Contains(TDContentOptions.TDIncludeLocalSeq))
            {
                localSeq = sequenceNumber;
            }
            IDictionary<string, object> revHistory = null;
            if (contentOptions.Contains(TDContentOptions.TDIncludeRevs))
            {
                revHistory = GetRevisionHistoryDict(rev);
            }
            IList<object> revsInfo = null;
            if (contentOptions.Contains(TDContentOptions.TDIncludeRevsInfo))
            {
                revsInfo = new AList<object>();
                IList<RevisionInternal> revHistoryFull = GetRevisionHistory(rev);
                foreach (RevisionInternal historicalRev in revHistoryFull)
                {
                    IDictionary<string, object> revHistoryItem = new Dictionary<string, object>();
                    string status = "available";
                    if (historicalRev.IsDeleted())
                    {
                        status = "deleted";
                    }
                    // TODO: Detect missing revisions, set status="missing"
                    revHistoryItem.Put("rev", historicalRev.GetRevId());
                    revHistoryItem.Put("status", status);
                    revsInfo.AddItem(revHistoryItem);
                }
            }
            IList<string> conflicts = null;
            if (contentOptions.Contains(TDContentOptions.TDIncludeConflicts))
            {
                RevisionList revs = GetAllRevisionsOfDocumentID(docId, true);
                if (revs.Count > 1)
                {
                    conflicts = new AList<string>();
                    foreach (RevisionInternal historicalRev in revs)
                    {
                        if (!historicalRev.Equals(rev))
                        {
                            conflicts.AddItem(historicalRev.GetRevId());
                        }
                    }
                }
            }

            var result = new Dictionary<string, object>();
            result["_id"] = docId;
            result["_rev"] = revId;

            if (rev.IsDeleted())
            {
                result["_deleted"] = true;
            }
            if (attachmentsDict != null)
            {
                result["_attachments"] = attachmentsDict;
            }
            if (localSeq != null)
            {
                result["_local_seq"] = localSeq;
            }
            if (revHistory != null)
            {
                result["_revisions"] = revHistory;
            }
            if (revsInfo != null)
            {
                result["_revs_info"] = revsInfo;
            }
            if (conflicts != null)
            {
                result["_conflicts"] = conflicts;
            }
            return result;
        }

        /// <summary>Returns an array of TDRevs in reverse chronological order, starting with the given revision.
        ///     </summary>
        /// <remarks>Returns an array of TDRevs in reverse chronological order, starting with the given revision.
        ///     </remarks>
        internal IList<RevisionInternal> GetRevisionHistory(RevisionInternal rev)
        {
            string docId = rev.GetDocId();
            string revId = rev.GetRevId();

            System.Diagnostics.Debug.Assert(((docId != null) && (revId != null)));

            long docNumericId = GetDocNumericID(docId);
            if (docNumericId < 0)
            {
                return null;
            }
            else
            {
                if (docNumericId == 0)
                {
                    return new AList<RevisionInternal>();
                }
            }

            Cursor cursor = null;
            IList<RevisionInternal> result;
            var args = new [] { Convert.ToString(docNumericId) };
            var sql = "SELECT sequence, parent, revid, deleted FROM revs WHERE doc_id=? ORDER BY sequence DESC";

            try
            {
                cursor = StorageEngine.RawQuery(sql, args);
                cursor.MoveToNext();

                long lastSequence = 0;
                result = new AList<RevisionInternal>();

                while (!cursor.IsAfterLast())
                {
                    long sequence = cursor.GetLong(0);
                    bool matches = false;
                    if (lastSequence == 0)
                    {
                        matches = revId.Equals(cursor.GetString(2));
                    }
                    else
                    {
                        matches = (sequence == lastSequence);
                    }
                    if (matches)
                    {
                        revId = cursor.GetString(2);
                        var deleted = (cursor.GetInt(3) > 0);

                        RevisionInternal aRev = new RevisionInternal(docId, revId, deleted, this);
                        aRev.SetSequence(cursor.GetLong(0));
                        result.AddItem(aRev);

                        lastSequence = cursor.GetLong(1);
                        if (lastSequence == 0)
                        {
                            break;
                        }
                    }
                    cursor.MoveToNext();
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting revision history", e);
                return null;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return result;
        }

        /// <summary>Returns the revision history as a _revisions dictionary, as returned by the REST API's ?revs=true option.
        ///     </summary>
        internal IDictionary<String, Object> GetRevisionHistoryDict(RevisionInternal rev)
        {
            return MakeRevisionHistoryDict(GetRevisionHistory(rev));
        }

        private static IDictionary<string, object> MakeRevisionHistoryDict(IList<RevisionInternal> history)
        {
            if (history == null)
                return null;

            // Try to extract descending numeric prefixes:
            var suffixes = new AList<string>();
            var start = -1;
            var lastRevNo = -1;

            foreach (var rev in history)
            {
                var revNo = ParseRevIDNumber(rev.GetRevId());
                var suffix = ParseRevIDSuffix(rev.GetRevId());
                if (revNo > 0 && suffix.Length > 0)
                {
                    if (start < 0)
                    {
                        start = revNo;
                    }
                    else
                    {
                        if (revNo != lastRevNo - 1)
                        {
                            start = -1;
                            break;
                        }
                    }
                    lastRevNo = revNo;
                    suffixes.AddItem(suffix);
                }
                else
                {
                    start = -1;
                    break;
                }
            }

            var result = new Dictionary<String, Object>();
            if (start == -1)
            {
                // we failed to build sequence, just stuff all the revs in list
                suffixes = new AList<string>();
                foreach (RevisionInternal rev_1 in history)
                {
                    suffixes.AddItem(rev_1.GetRevId());
                }
            }
            else
            {
                result["start"] = start;
            }

            result["ids"] = suffixes;
            return result;
        }

        /// <summary>Parses the _revisions dict from a document into an array of revision ID strings.</summary>
        internal static IList<String> ParseCouchDBRevisionHistory(IDictionary<String, Object> docProperties)
        {
            var revisions = (IDictionary<String, Object>)docProperties.Get("_revisions");
            if (revisions == null)
            {
                return null;
            }
            var revIDs = (IList<string>)revisions.Get("ids");
            int start = (int)revisions.Get("start");
            for (var i = 0; i < revIDs.Count; i++)
            {
                var revID = revIDs[i];
                revIDs.Set(i, Extensions.ToString(start--) + "-" + revID);
            }
            return revIDs;
        }

        // Splits a revision ID into its generation number and opaque suffix string
        private static int ParseRevIDNumber(string rev)
        {
            var result = -1;
            var dashPos = rev.IndexOf("-", StringComparison.InvariantCultureIgnoreCase);

            if (dashPos >= 0)
            {
                try
                {
                    result = System.Convert.ToInt32(Sharpen.Runtime.Substring(rev, 0, dashPos));
                }
                catch (FormatException)
                {
                }
            }
            // ignore, let it return -1
            return result;
        }

        // Splits a revision ID into its generation number and opaque suffix string
        private static string ParseRevIDSuffix(string rev)
        {
            var result = String.Empty;
            int dashPos = rev.IndexOf("-", StringComparison.InvariantCultureIgnoreCase);
            if (dashPos >= 0)
            {
                result = Runtime.Substring(rev, dashPos + 1);
            }
            return result;
        }

        /// <summary>Constructs an "_attachments" dictionary for a revision, to be inserted in its JSON body.</summary>
        internal IDictionary<String, Object> GetAttachmentsDictForSequenceWithContent(long sequence, EnumSet<TDContentOptions> contentOptions)
        {
            System.Diagnostics.Debug.Assert((sequence > 0));

            Cursor cursor = null;
            var args = new [] { Convert.ToString(sequence) };

            try
            {
                cursor = StorageEngine.RawQuery("SELECT filename, key, type, length, revpos FROM attachments WHERE sequence=?", args); // TODO: Convert to ADO parameters.
                if (!cursor.MoveToNext())
                {
                    return null;
                }
                IDictionary<string, object> result = new Dictionary<string, object>();
                while (!cursor.IsAfterLast())
                {
                    var dataSuppressed = false;
                    var length = cursor.GetInt(3);
                    var keyData = cursor.GetBlob(1);
                    var key = new BlobKey(keyData);
                    var digestString = "sha1-" + Convert.ToBase64String(keyData);
                    var dataBase64 = String.Empty;
                    if (contentOptions.Contains(TDContentOptions.TDIncludeAttachments))
                    {
                        if (contentOptions.Contains(TDContentOptions.TDBigAttachmentsFollow) && 
                            length >= Database.BigAttachmentLength)
                        {
                            dataSuppressed = true;
                        }
                        else
                        {
                            byte[] data = Attachments.BlobForKey(key);
                            if (data != null)
                            {
                                dataBase64 = Convert.ToBase64String(data);
                            }
                            else
                            {
                                // <-- very expensive
                                Log.W(Database.Tag, "Error loading attachment");
                            }
                        }
                    }
                    var attachment = new Dictionary<string, object>();
                    if (dataBase64 == null || dataSuppressed)
                    {
                        attachment.Put("stub", true);
                    }
                    if (dataBase64 != null)
                    {
                        attachment.Put("data", dataBase64);
                    }
                    if (dataSuppressed) {
                        attachment.Put ("follows", true);
                    }
                    attachment.Put("digest", digestString);

                    var contentType = cursor.GetString(2);
                    attachment.Put("content_type", contentType);
                    attachment.Put("length", length);
                    attachment.Put("revpos", cursor.GetInt(4));

                    var filename = cursor.GetString(0);
                    result.Put(filename, attachment);

                    cursor.MoveToNext();
                }
                return result;
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error getting attachments for sequence", e);
                return null;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
        }

        /// <summary>Splices the contents of an NSDictionary into JSON data (that already represents a dict), without parsing the JSON.</summary>
        internal IEnumerable<Byte> AppendDictToJSON(IEnumerable<Byte> json, IDictionary<String, Object> dict)
        {
            if (dict.Count == 0)
                return json;

            Byte[] extraJSON;
            try
            {
                extraJSON = Manager.GetObjectMapper().WriteValueAsBytes(dict).ToArray();
            }
            catch (Exception e)
            {
                Log.E(Database.Tag, "Error convert extra JSON to bytes", e);
                return null;
            }

            var jsonArray = json.ToArray ();
            int jsonLength = jsonArray.Length;
            int extraLength = extraJSON.Length;

            if (jsonLength == 2)
            {
                // Original JSON was empty
                return extraJSON;
            }

            var newJson = new byte[jsonLength + extraLength - 1];
            Array.Copy(jsonArray.ToArray(), 0, newJson, 0, jsonLength - 1);

            // Copy json w/o trailing '}'
            newJson[jsonLength - 1] = (byte)(',');

            // Add a ','
            Array.Copy(extraJSON, 1, newJson, jsonLength, extraLength - 1);

            return newJson;
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal RevisionInternal PutRevision(RevisionInternal rev, String prevRevId, Status resultStatus)
        {
            return PutRevision(rev, prevRevId, false, resultStatus);
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal RevisionInternal PutRevision(RevisionInternal rev, String prevRevId, Boolean allowConflict)
        {
            Status ignoredStatus = new Status();
            return PutRevision(rev, prevRevId, allowConflict, ignoredStatus);
        }

        /// <summary>Stores a new (or initial) revision of a document.</summary>
        /// <remarks>
        /// Stores a new (or initial) revision of a document.
        /// This is what's invoked by a PUT or POST. As with those, the previous revision ID must be supplied when necessary and the call will fail if it doesn't match.
        /// </remarks>
        /// <param name="rev">The revision to add. If the docID is null, a new UUID will be assigned. Its revID must be null. It must have a JSON body.
        ///     </param>
        /// <param name="prevRevId">The ID of the revision to replace (same as the "?rev=" parameter to a PUT), or null if this is a new document.
        ///     </param>
        /// <param name="allowConflict">If false, an error status 409 will be returned if the insertion would create a conflict, i.e. if the previous revision already has a child.
        ///     </param>
        /// <param name="resultStatus">On return, an HTTP status code indicating success or failure.
        ///     </param>
        /// <returns>A new RevisionInternal with the docID, revID and sequence filled in (but no body).
        ///     </returns>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal RevisionInternal PutRevision(RevisionInternal oldRev, String prevRevId, Boolean allowConflict, Status resultStatus)
        {
            // prevRevId is the rev ID being replaced, or nil if an insert
            var docId = oldRev.GetDocId();
            var deleted = oldRev.IsDeleted();

            if ((oldRev == null) || ((prevRevId != null) && (docId == null)) || (deleted && (docId == null)) || ((docId != null) && !IsValidDocumentId(docId)))
            {
                throw new CouchbaseLiteException(StatusCode.BadRequest);
            }
            BeginTransaction();
            Cursor cursor = null;
            var inConflict = false;
            RevisionInternal winningRev = null;
            RevisionInternal newRev = null;

            // PART I: In which are performed lookups and validations prior to the insert...
            var docNumericID = (docId != null) ? GetDocNumericID(docId) : 0;
            var parentSequence = 0L;
            try
            {
                if (prevRevId != null)
                {
                    // Replacing: make sure given prevRevID is current & find its sequence number:
                    if (docNumericID <= 0)
                    {
                        throw new CouchbaseLiteException(StatusCode.NotFound);
                    }
                    var args = new [] { Convert.ToString(docNumericID), prevRevId };
                    var additionalWhereClause = String.Empty;
                    if (!allowConflict)
                    {
                        additionalWhereClause = "AND current=1";
                    }
                    cursor = StorageEngine.RawQuery(
                                                "SELECT sequence FROM revs WHERE doc_id=? AND revid=? "
                                               + additionalWhereClause + " LIMIT 1", args);
                    if (cursor.MoveToNext())
                    {
                        parentSequence = cursor.GetLong(0);
                    }
                    if (parentSequence == 0)
                    {
                        // Not found: either a 404 or a 409, depending on whether there is any current revision
                        if (!allowConflict && ExistsDocumentWithIDAndRev(docId, null))
                        {
                            throw new CouchbaseLiteException(StatusCode.Conflict);
                        }
                        else
                        {
                            throw new CouchbaseLiteException(StatusCode.NotFound);
                        }
                    }

                    var validate = Validations;
                    if (validate != null)
                    {
                        // Fetch the previous revision and validate the new one against it:
                        var prevRev = new RevisionInternal(docId, prevRevId, false, this);
                        ValidateRevision(oldRev, prevRev);
                    }
                    // Make replaced rev non-current:
                    var updateContent = new ContentValues();
                    updateContent.Put("current", 0);

                    StorageEngine.Update("revs", updateContent, "sequence=" + parentSequence, null);
                }
                else
                {
                    // Inserting first revision.
                    if (deleted && (docId != null))
                    {
                        // Didn't specify a revision to delete: 404 or a 409, depending
                        if (ExistsDocumentWithIDAndRev(docId, null))
                        {
                            throw new CouchbaseLiteException(StatusCode.Conflict);
                        }
                        else
                        {
                            throw new CouchbaseLiteException(StatusCode.NotFound);
                        }
                    }
                    // Validate:
                    ValidateRevision(oldRev, null);
                    if (docId != null)
                    {
                        // Inserting first revision, with docID given (PUT):
                        if (docNumericID <= 0)
                        {
                            // Doc doesn't exist at all; create it:
                            docNumericID = InsertDocumentID(docId);
                            if (docNumericID <= 0)
                            {
                                return null;
                            }
                        }
                        else
                        {
                            // Doc exists; check whether current winning revision is deleted:
                            string[] args = new string[] { System.Convert.ToString(docNumericID) };
                            cursor = StorageEngine.RawQuery("SELECT sequence, deleted FROM revs WHERE doc_id=? and current=1 ORDER BY revid DESC LIMIT 1"
                                                       , args);
                            if (cursor.MoveToNext())
                            {
                                bool wasAlreadyDeleted = (cursor.GetInt(1) > 0);
                                if (wasAlreadyDeleted)
                                {
                                    // Make the deleted revision no longer current:
                                    ContentValues updateContent = new ContentValues();
                                    updateContent.Put("current", 0);
                                    StorageEngine.Update("revs", updateContent, "sequence=" + cursor.GetLong(0), null);
                                }
                                else
                                {
                                    if (!allowConflict)
                                    {
                                        string msg = string.Format("docId (%s) already exists, current not " + "deleted, so conflict.  Did you forget to pass in a previous "
                                                                   + "revision ID in the properties being saved?", docId);
                                        throw new CouchbaseLiteException(msg, StatusCode.Conflict);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Inserting first revision, with no docID given (POST): generate a unique docID:
                        docId = Database.GenerateDocumentId();
                        docNumericID = InsertDocumentID(docId);
                        if (docNumericID <= 0)
                        {
                            return null;
                        }
                    }
                }
                // PART II: In which insertion occurs...
                // Get the attachments:
                var attachments = GetAttachmentsFromRevision(oldRev);
                // Bump the revID and update the JSON:
                var newRevId = GenerateNextRevisionID(prevRevId);
                IEnumerable<byte> data = null;
                if (!oldRev.IsDeleted())
                {
                    data = EncodeDocumentJSON(oldRev);
                    if (data == null)
                    {
                        // bad or missing json
                        throw new CouchbaseLiteException(StatusCode.BadRequest);
                    }
                }
                newRev = oldRev.CopyWithDocID(docId, newRevId);
                StubOutAttachmentsInRevision(attachments, oldRev);
                // Now insert the rev itself:
                long newSequence = InsertRevision(oldRev, docNumericID, parentSequence, true, data);
                if (newSequence == 0)
                {
                    return null;
                }
                // Store any attachments:
                if (attachments != null)
                {
                    ProcessAttachmentsForRevision(attachments, oldRev, parentSequence);
                }
                // Success!
                if (deleted)
                {
                    resultStatus.SetCode(StatusCode.Ok);
                }
                else
                {
                    resultStatus.SetCode(StatusCode.Created);
                }
            }
            catch (SQLException e1)
            {
                Log.E(Couchbase.Lite.Database.Tag, "Error putting revision", e1);
                return null;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
                EndTransaction(resultStatus.IsSuccessful());
            }
            // EPILOGUE: A change notification is sent...
            NotifyChange(newRev, winningRev, null, inConflict);
            return oldRev;
        }

        internal void NotifyChange(RevisionInternal rev, RevisionInternal winningRev, Uri source, bool inConflict)
        {
            // TODO: it is currently sending one change at a time rather than batching them up
            const bool isExternalFixMe = false;
            // TODO: fix this to have a real value
            var change = new DocumentChange(rev, winningRev, inConflict, source);

            var changes = new AList<DocumentChange>() { change };
            changes.AddItem(change);

            var args = new DatabaseChangeEventArgs { 
                                    Changes = changes,
                                    IsExternal = isExternalFixMe,
                                    Source = this
                                } ;

            var changeEvent = Changed;
            if (changeEvent != null)
                changeEvent(this, args);

            // TODO: this is expensive, it should be using a WeakHashMap
            // TODO: instead of loading from the DB.  iOS code below.
            var document = GetDocument(change.DocumentId);
            document.RevisionAdded(change);
        }

        /// <summary>
        /// Given a newly-added revision, adds the necessary attachment rows to the sqliteDb and
        /// stores inline attachments into the blob store.
        /// </summary>
        /// <remarks>
        /// Given a newly-added revision, adds the necessary attachment rows to the sqliteDb and
        /// stores inline attachments into the blob store.
        /// </remarks>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void ProcessAttachmentsForRevision(IDictionary<string, AttachmentInternal> attachments, RevisionInternal rev, long parentSequence)
        {
            System.Diagnostics.Debug.Assert((rev != null));
            var newSequence = rev.GetSequence();
            System.Diagnostics.Debug.Assert((newSequence > parentSequence));
            var generation = rev.GetGeneration();
            System.Diagnostics.Debug.Assert((generation > 0));

            // If there are no attachments in the new rev, there's nothing to do:
            IDictionary<string, object> revAttachments = null;
            var properties = rev.GetProperties ();
            if (properties != null)
            {
                revAttachments = (IDictionary<string, object>)properties.Get("_attachments");
            }

            if (revAttachments == null || revAttachments.Count == 0 || rev.IsDeleted())
            {
                return;
            }
            foreach (string name in revAttachments.Keys)
            {
                var attachment = attachments.Get(name);
                if (attachment != null)
                {
                    // Determine the revpos, i.e. generation # this was added in. Usually this is
                    // implicit, but a rev being pulled in replication will have it set already.
                    if (attachment.GetRevpos() == 0)
                    {
                        attachment.SetRevpos(generation);
                    }
                    else
                    {
                        if (attachment.GetRevpos() > generation)
                        {
                            Log.W(Couchbase.Lite.Database.Tag, string.Format("Attachment %s %s has unexpected revpos %s, setting to %s"
                                                                             , rev, name, attachment.GetRevpos(), generation));
                            attachment.SetRevpos(generation);
                        }
                    }
                    // Finally insert the attachment:
                    InsertAttachmentForSequence(attachment, newSequence);
                }
                else
                {
                    // It's just a stub, so copy the previous revision's attachment entry:
                    //? Should I enforce that the type and digest (if any) match?
                    CopyAttachmentNamedFromSequenceToSequence(name, parentSequence, newSequence);
                }
            }
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void CopyAttachmentNamedFromSequenceToSequence(string name, long fromSeq, long toSeq)
        {
            System.Diagnostics.Debug.Assert((name != null));
            System.Diagnostics.Debug.Assert((toSeq > 0));

            if (fromSeq < 0)
            {
                throw new CouchbaseLiteException(StatusCode.NotFound);
            }

            Cursor cursor = null;
            var args = new [] { Convert.ToString(toSeq), name, Convert.ToString(fromSeq), name };

            try
            {
                StorageEngine.ExecSQL("INSERT INTO attachments (sequence, filename, key, type, length, revpos) "
                                      + "SELECT ?, ?, key, type, length, revpos FROM attachments " + "WHERE sequence=? AND filename=?", args); // FIX: Convert to ADO parameters;
                cursor = StorageEngine.RawQuery("SELECT changes()", null);
                cursor.MoveToNext();

                int rowsUpdated = cursor.GetInt(0);
                if (rowsUpdated == 0)
                {
                    // Oops. This means a glitch in our attachment-management or pull code,
                    // or else a bug in the upstream server.
                    Log.W(Database.Tag, "Can't find inherited attachment " + name 
                          + " from seq# " + Convert.ToString(fromSeq) + " to copy to " + Convert.ToString(toSeq));
                    throw new CouchbaseLiteException(StatusCode.NotFound);
                }
                else
                {
                    return;
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error copying attachment", e);
                throw new CouchbaseLiteException(StatusCode.InternalServerError);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void InsertAttachmentForSequence(AttachmentInternal attachment, long sequence)
        {
            InsertAttachmentForSequenceWithNameAndType(sequence, attachment.GetName(), attachment.GetContentType(), attachment.GetRevpos(), attachment.GetBlobKey());
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void InsertAttachmentForSequenceWithNameAndType(InputStream contentStream, long sequence, string name, string contentType, int revpos)
        {
            System.Diagnostics.Debug.Assert((sequence > 0));
            System.Diagnostics.Debug.Assert((name != null));

            var key = new BlobKey();
            if (!Attachments.StoreBlobStream(contentStream, key))
            {
                throw new CouchbaseLiteException(StatusCode.InternalServerError);
            }
            InsertAttachmentForSequenceWithNameAndType(sequence, name, contentType, revpos, key);
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void InsertAttachmentForSequenceWithNameAndType(long sequence, string name, string contentType, int revpos, BlobKey key)
        {
            try
            {
                var args = new ContentValues();
                args.Put("sequence", sequence);
                args.Put("filename", name);
                if (key != null)
                {
                    args.Put("key", key.GetBytes());
                    args.Put("length", Attachments.GetSizeOfBlob(key));
                }
                args.Put("type", contentType);
                args.Put("revpos", revpos);
                StorageEngine.Insert("attachments", null, args);
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error inserting attachment", e);
                throw new CouchbaseLiteException(StatusCode.InternalServerError);
            }
        }

        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void InstallAttachment(AttachmentInternal attachment, IDictionary<String, Object> attachInfo)
        {
            var digest = (string)attachInfo.Get("digest");
            if (digest == null)
            {
                throw new CouchbaseLiteException(StatusCode.BadAttachment);
            }

            if (PendingAttachmentsByDigest != null && PendingAttachmentsByDigest.ContainsKey(digest))
            {
                var writer = PendingAttachmentsByDigest.Get(digest);
                try
                {
                    var blobStoreWriter = writer;
                    blobStoreWriter.Install();
                    attachment.SetBlobKey(blobStoreWriter.GetBlobKey());
                    attachment.SetLength(blobStoreWriter.GetLength());
                }
                catch (Exception e)
                {
                    throw new CouchbaseLiteException(e, StatusCode.StatusAttachmentError);
                }
            }
        }

        internal Int64 InsertRevision(RevisionInternal rev, long docNumericID, long parentSequence, Boolean current, IEnumerable<byte> data)
        {
            long rowId = 0;
            try
            {
                ContentValues args = new ContentValues();
                args.Put("doc_id", docNumericID);
                args.Put("revid", rev.GetRevId());
                if (parentSequence != 0)
                {
                    args.Put("parent", parentSequence);
                }
                args.Put("current", current);
                args.Put("deleted", rev.IsDeleted());
                args.Put("json", data.ToArray());
                rowId = StorageEngine.Insert("revs", null, args);
                rev.SetSequence(rowId);
            }
            catch (Exception e)
            {
                Log.E(Database.Tag, "Error inserting revision", e);
            }
            return rowId;
        }

        internal void StubOutAttachmentsInRevision(IDictionary<String, AttachmentInternal> attachments, RevisionInternal rev)
        {
            var properties = rev.GetProperties();
            var attachmentsFromProps = (IDictionary<String, Object>)properties.Get("_attachments");
            if (attachmentsFromProps != null)
            {
                foreach (string attachmentKey in attachmentsFromProps.Keys)
                {
                    var attachmentFromProps = (IDictionary<string, object>)attachmentsFromProps.Get(attachmentKey);
                    if (attachmentFromProps.Get("follows") != null || attachmentFromProps.Get("data")
                        != null)
                    {
                        Collections.Remove(attachmentFromProps, "follows");
                        Collections.Remove(attachmentFromProps, "data");

                        attachmentFromProps.Put("stub", true);
                        if (attachmentFromProps.Get("revpos") == null)
                        {
                            attachmentFromProps.Put("revpos", rev.GetGeneration());
                        }
                        var attachmentObject = attachments.Get(attachmentKey);
                        if (attachmentObject != null)
                        {
                            attachmentFromProps.Put("length", attachmentObject.GetLength());
                            if (attachmentObject.GetBlobKey() != null)
                            {
                                // case with Large Attachment
                                attachmentFromProps.Put("digest", attachmentObject.GetBlobKey().Base64Digest());
                            }
                        }
                        attachmentFromProps.Put(attachmentKey, attachmentFromProps);
                    }
                }
            }
        }

        /// <summary>INSERTION:</summary>
        internal IEnumerable<Byte> EncodeDocumentJSON(RevisionInternal rev)
        {
            var origProps = rev.GetProperties();
            if (origProps == null)
            {
                return null;
            }
            // Don't allow any "_"-prefixed keys. Known ones we'll ignore, unknown ones are an error.
            IDictionary<string, object> properties = new Dictionary<string, object>(origProps
                                                                                    .Count);
            foreach (string key in origProps.Keys)
            {
                if (key.StartsWith("_", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!KnownSpecialKeys.Contains(key))
                    {
                        Log.E(Tag, "Database: Invalid top-level key '" + key + "' in document to be inserted");
                        return null;
                    }
                }
                else
                {
                    properties.Put(key, origProps.Get(key));
                }
            }
            IEnumerable<byte> json = null;
            try
            {
                json = Manager.GetObjectMapper().WriteValueAsBytes(properties);
            }
            catch (Exception e)
            {
                Log.E(Database.Tag, "Error serializing " + rev + " to JSON", e);
            }
            return json;
        }

        /// <summary>
        /// Given a revision, read its _attachments dictionary (if any), convert each attachment to a
        /// AttachmentInternal object, and return a dictionary mapping names-&gt;CBL_Attachments.
        /// </summary>
        /// <remarks>
        /// Given a revision, read its _attachments dictionary (if any), convert each attachment to a
        /// AttachmentInternal object, and return a dictionary mapping names-&gt;CBL_Attachments.
        /// </remarks>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal IDictionary<String, AttachmentInternal> GetAttachmentsFromRevision(RevisionInternal rev)
        {
            IDictionary<string, object> revAttachments = (IDictionary<string, object>)rev.GetPropertyForKey
                                                         ("_attachments");
            if (revAttachments == null || revAttachments.Count == 0 || rev.IsDeleted())
            {
                return new Dictionary<string, AttachmentInternal>();
            }
            IDictionary<string, AttachmentInternal> attachments = new Dictionary<string, AttachmentInternal
            >();
            foreach (string name in revAttachments.Keys)
            {
                IDictionary<string, object> attachInfo = (IDictionary<string, object>)revAttachments
                    .Get(name);
                string contentType = (string)attachInfo.Get("content_type");
                AttachmentInternal attachment = new AttachmentInternal(name, contentType);
                string newContentBase64 = (string)attachInfo.Get("data");
                if (newContentBase64 != null)
                {
                    // If there's inline attachment data, decode and store it:
                    byte[] newContents;
                    try
                    {
                        newContents = Convert.FromBase64String(newContentBase64);
                    }
                    catch (IOException e)
                    {
                        throw new CouchbaseLiteException(e, StatusCode.BadEncoding);
                    }
                    attachment.SetLength(newContents.Length);
                    BlobKey outBlobKey = new BlobKey();
                    bool storedBlob = GetAttachments().StoreBlob(newContents, outBlobKey);
                    attachment.SetBlobKey(outBlobKey);
                    if (!storedBlob)
                    {
                        throw new CouchbaseLiteException(StatusCode.StatusAttachmentError);
                    }
                }
                else
                {
                    if (((bool)attachInfo.Get("follows")) == true)
                    {
                        // "follows" means the uploader provided the attachment in a separate MIME part.
                        // This means it's already been registered in _pendingAttachmentsByDigest;
                        // I just need to look it up by its "digest" property and install it into the store:
                        InstallAttachment(attachment, attachInfo);
                    }
                    else
                    {
                        // This item is just a stub; validate and skip it
                        if (((bool)attachInfo.Get("stub")) == false)
                        {
                            throw new CouchbaseLiteException("Expected this attachment to be a stub", StatusCode.
                                                             BadAttachment);
                        }
                        int revPos = ((int)attachInfo.Get("revpos"));
                        if (revPos <= 0)
                        {
                            throw new CouchbaseLiteException("Invalid revpos: " + revPos, StatusCode.BadAttachment
                                                            );
                        }
                        continue;
                    }
                }
                // Handle encoded attachment:
                string encodingStr = (string)attachInfo.Get("encoding");
                if (encodingStr != null && encodingStr.Length > 0)
                {
                    if (Sharpen.Runtime.EqualsIgnoreCase(encodingStr, "gzip"))
                    {
                        attachment.SetEncoding(AttachmentInternal.AttachmentEncoding.AttachmentEncodingGZIP
                                              );
                    }
                    else
                    {
                        throw new CouchbaseLiteException("Unnkown encoding: " + encodingStr, StatusCode.BadEncoding
                                                        );
                    }
                    attachment.SetEncodedLength(attachment.GetLength());
                    attachment.SetLength((long)attachInfo.Get("length"));
                }
                if (attachInfo.ContainsKey("revpos"))
                {
                    attachment.SetRevpos((int)attachInfo.Get("revpos"));
                }
                else
                {
                    attachment.SetRevpos(1);
                }
                attachments.Put(name, attachment);
            }
            return attachments;
        }

        internal String GenerateNextRevisionID(string revisionId)
        {
            // Revision IDs have a generation count, a hyphen, and a UUID.
            int generation = 0;
            if (revisionId != null)
            {
                generation = RevisionInternal.GenerationFromRevID(revisionId);
                if (generation == 0)
                {
                    return null;
                }
            }
            string digest = Misc.TDCreateUUID();
            // TODO: Generate canonical digest of body
            return Sharpen.Extensions.ToString(generation + 1) + "-" + digest;
        }


        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal RevisionInternal LoadRevisionBody(RevisionInternal rev, EnumSet<TDContentOptions> contentOptions)
        {
            if (rev.GetBody() != null)
            {
                return rev;
            }
            System.Diagnostics.Debug.Assert(((rev.GetDocId() != null) && (rev.GetRevId() != null)));
            Cursor cursor = null;
            Status result = new Status(StatusCode.NotFound);
            try
            {
                var sql = "SELECT sequence, json FROM revs, docs WHERE revid=? AND docs.docid=? AND revs.doc_id=docs.doc_id LIMIT 1"; // FIX: Replace with ADO parameters.
                var args = new [] { rev.GetRevId(), rev.GetDocId() };

                cursor = StorageEngine.RawQuery(sql, args);
                if (cursor.MoveToNext())
                {
                    result.SetCode(StatusCode.Ok);
                    rev.SetSequence(cursor.GetLong(0));
                    ExpandStoredJSONIntoRevisionWithAttachments(cursor.GetBlob(1), rev, contentOptions);
                }
            }
            catch (SQLException e)
            {
                Log.E(Couchbase.Lite.Database.Tag, "Error loading revision body", e);
                throw new CouchbaseLiteException(StatusCode.InternalServerError);
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return rev;
        }

        internal Int64 InsertDocumentID(String docId)
        {
            long rowId = -1;
            try
            {
                ContentValues args = new ContentValues();
                args.Put("docid", docId);
                rowId = StorageEngine.Insert("docs", null, args);
            }
            catch (Exception e)
            {
                Log.E(Database.Tag, "Error inserting document id", e);
            }
            return rowId;
        }

        internal Boolean ExistsDocumentWithIDAndRev(String docId, String revId)
        {
            return GetDocumentWithIDAndRev(docId, revId, EnumSet.Of(TDContentOptions.TDNoBody)) != null;
        }

        internal bool FindMissingRevisions(RevisionList touchRevs)
        {
            if (touchRevs.Count == 0)
            {
                return true;
            }
            var quotedDocIds = JoinQuoted(touchRevs.GetAllDocIds());
            var quotedRevIds = JoinQuoted(touchRevs.GetAllRevIds());
            var sql = "SELECT docid, revid FROM revs, docs " + "WHERE docid IN (" + quotedDocIds
                         + ") AND revid in (" + quotedRevIds + ")" + " AND revs.doc_id == docs.doc_id";
            Cursor cursor = null;
            try
            {
                cursor = StorageEngine.RawQuery(sql, null);
                cursor.MoveToNext();
                while (!cursor.IsAfterLast())
                {
                    var rev = touchRevs.RevWithDocIdAndRevId(cursor.GetString(0), cursor.GetString(1));
                    if (rev != null)
                    {
                        touchRevs.Remove(rev);
                    }
                    cursor.MoveToNext();
                }
            }
            catch (SQLException e)
            {
                Log.E(Database.Tag, "Error finding missing revisions", e);
                return false;
            }
            finally
            {
                if (cursor != null)
                {
                    cursor.Close();
                }
            }
            return true;
        }

        /// <summary>DOCUMENT & REV IDS:</summary>
        internal Boolean IsValidDocumentId(string id)
        {
            // http://wiki.apache.org/couchdb/HTTP_Document_API#Documents
            if (String.IsNullOrEmpty (id)) {
                return false;
            }

            return id [0] != '_' || id.StartsWith ("_design/", StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>VALIDATION</summary>
        /// <exception cref="Couchbase.Lite.CouchbaseLiteException"></exception>
        internal void ValidateRevision(RevisionInternal newRev, RevisionInternal oldRev)
        {
            if (Validations == null || Validations.Count == 0)
            {
                return;
            }

            var context = new ValidationContext(this, oldRev);

            foreach (string validationName in Validations.Keys)
            {
                var validation = GetValidation(validationName);
                if (validation == null) continue;
                throw new NotImplementedException();
//                if (validation(/*newRev*/null, context))
//                {
//                    throw new CouchbaseLiteException(context.ErrorType.GetCode());
//                }
            }
        }

        internal Boolean Initialize(String statements)
        {
            try
            {
                foreach (string statement in statements.Split(";"))
                {
                    StorageEngine.ExecSQL(statement);
                }
            }
            catch (SQLException)
            {
                Close();
                return false;
            }
            return true;
        }

        internal String AttachmentStorePath {
            get {
                var attachmentStorePath = Path;
                int lastDotPosition = attachmentStorePath.LastIndexOf(".", StringComparison.InvariantCultureIgnoreCase);
                if (lastDotPosition > 0)
                {
                    attachmentStorePath = Runtime.Substring(attachmentStorePath, 0, lastDotPosition);
                }
                attachmentStorePath = attachmentStorePath + FilePath.separator + "attachments";
                return attachmentStorePath;
            }
        }

        internal Boolean Open()
        {
            if (open)
            {
                return true;
            }
            // Create the storage engine.
            StorageEngine = SQLiteStorageEngineFactory.CreateStorageEngine();
            // Try to open the storage engine and stop if we fail.
            if (StorageEngine == null || !StorageEngine.Open(Path))
            {
                return false;
            }
            // Stuff we need to initialize every time the sqliteDb opens:
            if (!Initialize("PRAGMA foreign_keys = ON;"))
            {
                Log.E(Database.Tag, "Error turning on foreign keys");
                return false;
            }
            // Check the user_version number we last stored in the sqliteDb:
            var dbVersion = StorageEngine.GetVersion();
            // Incompatible version changes increment the hundreds' place:
            if (dbVersion >= 100)
            {
                Log.W(Database.Tag, "Database: Database version (" + dbVersion + ") is newer than I know how to work with");
                StorageEngine.Close();
                return false;
            }
            if (dbVersion < 1)
            {
                // First-time initialization:
                // (Note: Declaring revs.sequence as AUTOINCREMENT means the values will always be
                // monotonically increasing, never reused. See <http://www.sqlite.org/autoinc.html>)
                if (!Initialize(Schema))
                {
                    StorageEngine.Close();
                    return false;
                }
                dbVersion = 3;
            }
            if (dbVersion < 2)
            {
                // Version 2: added attachments.revpos
                var upgradeSql = "ALTER TABLE attachments ADD COLUMN revpos INTEGER DEFAULT 0; PRAGMA user_version = 2";
                if (!Initialize(upgradeSql))
                {
                    StorageEngine.Close();
                    return false;
                }
                dbVersion = 2;
            }
            if (dbVersion < 3)
            {
                var upgradeSql = "CREATE TABLE localdocs ( " + "docid TEXT UNIQUE NOT NULL, " 
                                    + "revid TEXT NOT NULL, " + "json BLOB); " + "CREATE INDEX localdocs_by_docid ON localdocs(docid); "
                                    + "PRAGMA user_version = 3";
                if (!Initialize(upgradeSql))
                {
                    StorageEngine.Close();
                    return false;
                }
                dbVersion = 3;
            }
            if (dbVersion < 4)
            {
                var upgradeSql = "CREATE TABLE info ( " + "key TEXT PRIMARY KEY, " + "value TEXT); "
                                    + "INSERT INTO INFO (key, value) VALUES ('privateUUID', '" + Misc.TDCreateUUID(
                                       ) + "'); " + "INSERT INTO INFO (key, value) VALUES ('publicUUID',  '" + Misc.TDCreateUUID
                                    () + "'); " + "PRAGMA user_version = 4";
                if (!Initialize(upgradeSql))
                {
                    StorageEngine.Close();
                    return false;
                }
            }
            try
            {
                Attachments = new BlobStore(AttachmentStorePath);
            }
            catch (ArgumentException e)
            {
                Log.E(Database.Tag, "Could not initialize attachment store", e);
                StorageEngine.Close();
                return false;
            }
            open = true;
            return true;
        }

        internal Boolean Close()
        {
            if (!open)
            {
                return false;
            }
            if (views != null)
            {
                foreach (View view in views.Values)
                {
                    view.DatabaseClosing();
                }
            }
            views = null;
            if (ActiveReplicators != null)
            {
                foreach (Replication replicator in ActiveReplicators)
                {
                    replicator.DatabaseClosing();
                }
                ActiveReplicators = null;
            }
            if (StorageEngine != null && StorageEngine.IsOpen())
            {
                StorageEngine.Close();
            }
            open = false;
            transactionLevel = 0;
            return true;
        }


    #endregion
    
    #region Delegates
        public delegate Boolean RunInTransactionDelegate();

        public delegate void ValidateDelegate(Revision newRevision, IValidationContext context);

        public delegate Boolean FilterDelegate(SavedRevision revision, Dictionary<String, Object> filterParams);

        public delegate FilterDelegate CompileFilterDelegate(String source, String language);

    #endregion
    
    #region EventArgs Subclasses
        public class DatabaseChangeEventArgs : EventArgs {

            //Properties
            public Database Source { get; internal set; }

            public Boolean IsExternal { get; internal set; }

            public IEnumerable<DocumentChange> Changes { get; internal set; }

        }

    #endregion
    
    }

    #region Global Delegates

    public delegate Boolean ValidateChangeDelegate(String key, Object oldValue, Object newValue);
    public delegate void RunAsyncDelegate(Database database);

    #endregion
}

