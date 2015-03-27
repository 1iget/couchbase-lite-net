﻿//
//  DatabaseMethods.cs
//
//  Author:
//  	Jim Borden  <jim.borden@couchbase.com>
//
//  Copyright (c) 2015 Couchbase, Inc All rights reserved.
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
using System.Linq;
using System.Net;

using Couchbase.Lite.Internal;
using System.Diagnostics;
using Couchbase.Lite.Replicator;
using Couchbase.Lite.Util;
using System.Text;

namespace Couchbase.Lite.PeerToPeer
{
    internal static class DatabaseMethods
    {
        private const string TAG = "DatabaseMethods";
        private const double MIN_HEARTBEAT = 5000.0; //NOTE: iOS uses seconds but .NET uses milliseconds

        public static ICouchbaseResponseState GetConfiguration(HttpListenerContext context)
        {
            // http://wiki.apache.org/couchdb/HTTP_database_API#Database_Information
            return PerformLogicWithDatabase(context, true, db =>
            {
                int numDocs = db.DocumentCount;
                long updateSequence = db.LastSequenceNumber;
                if (numDocs < 0 || updateSequence < 0) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.DbError };
                }

                var response = new CouchbaseLiteResponse(context);
                response.Body = new Body(new Dictionary<string, object> {
                    { "db_name", db.Name },
                    { "doc_count", numDocs },
                    { "update_seq", updateSequence },
                    { "committed_update_seq", updateSequence },
                    { "purge_seq", 0 }, //TODO: Implement
                    { "disk_size", db.TotalDataSize },
                    { "start_time", db.StartTime * 1000 }
                });

                return response;
            }).AsDefaultState();
        }

        public static ICouchbaseResponseState DeleteConfiguration(HttpListenerContext context) 
        {
            return PerformLogicWithDatabase(context, false, db =>
            {
                if(context.Request.QueryString["rev"] != null) {
                    // CouchDB checks for this; probably meant to be a document deletion
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadId };
                }

                try {
                    db.Delete();
                } catch (CouchbaseLiteException) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.InternalServerError };
                }

                return new CouchbaseLiteResponse(context);
            }).AsDefaultState();
        }

        public static ICouchbaseResponseState UpdateConfiguration(HttpListenerContext context)
        {
            string[] components = context.Request.Url.AbsolutePath.Split(new[]{ '/' }, StringSplitOptions.RemoveEmptyEntries);
            string dbName = components[0];
            Database db = Manager.SharedInstance.GetDatabaseWithoutOpening(dbName, false);
            if (db != null && db.Exists()) {
                return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.PreconditionFailed }.AsDefaultState();
            }

            try {
                db.Open();
            } catch(CouchbaseLiteException) {
                return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.Exception }.AsDefaultState();
            }

            return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.Created }.AsDefaultState();
        }

        public static ICouchbaseResponseState GetAllDocuments(HttpListenerContext context)
        {
            return PerformLogicWithDatabase(context, true, db =>
            {
                var options = GetQueryOptions(context);
                if(options == null) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadParam };
                }

                return DoAllDocs(context, db, options);
            }).AsDefaultState();
        }

        public static ICouchbaseResponseState GetAllSpecifiedDocuments(HttpListenerContext context)
        {
            // http://wiki.apache.org/couchdb/HTTP_Bulk_Document_API
            return PerformLogicWithDatabase(context, true, db =>
            {
                var options = GetQueryOptions(context);
                if(options == null) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadParam };
                }
                    
                var body = GetPostBody(context);
                if(body == null) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadJson };
                }

                if(!body.ContainsKey("rows")) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadParam };
                }

                var keys = body["rows"].AsList<object>();
                options.SetKeys(keys);
                return DoAllDocs(context, db, options);
            }).AsDefaultState();
        }

        public static ICouchbaseResponseState ProcessDocumentChangeOperations(HttpListenerContext context)
        {
            // http://wiki.apache.org/couchdb/HTTP_Bulk_Document_API
            return PerformLogicWithDatabase(context, true, db =>
            {
                var postBody = GetPostBody(context);
                if(postBody == null) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadJson };
                }
                    
                if(!postBody.ContainsKey("docs")) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadParam };
                }
                var docs = postBody["docs"].AsList<IDictionary<string, object>>();

                bool allOrNothing;
                postBody.TryGetValue<bool>("all_or_nothing", out allOrNothing);

                bool newEdits;
                postBody.TryGetValue<bool>("new_edits", out newEdits);

                var response = new CouchbaseLiteResponse(context);
                StatusCode status = StatusCode.Ok;
                bool success = db.RunInTransaction(() => {
                    List<IDictionary<string, object>> results = new List<IDictionary<string, object>>(docs.Count);
                    foreach(var doc in docs) {
                        string docId = doc.GetCast<string>("_id");
                        RevisionInternal rev = null;
                        Body body = new Body(doc);

                        if(!newEdits) {
                            if(!RevisionInternal.IsValid(body)) {
                                status = StatusCode.BadParam;
                            } else {
                                rev = new RevisionInternal(body);
                                var history = Database.ParseCouchDBRevisionHistory(doc);
                                try {
                                    db.ForceInsert(rev, history, null);
                                } catch(CouchbaseLiteException e) {
                                    status = e.Code;
                                }
                            } 
                        } else {
                            status = UpdateDocument(context, db, docId, body, false, allOrNothing, out rev);
                        }

                        IDictionary<string, object> result = null;
                        if((int)status < 300) {
                            Debug.Assert(rev != null && rev.GetRevId() != null);
                            if(newEdits) {
                                result = new Dictionary<string, object>
                                {
                                    { "id", rev.GetDocId() },
                                    { "rev", rev.GetRevId() },
                                    { "status", (int)status }
                                };
                            }
                        } else if((int)status >= 500) {
                            return false; // abort the whole thing if something goes badly wrong
                        } else if(allOrNothing) {
                            return false; // all_or_nothing backs out if there's any error
                        } else {
                            var info = Status.ToHttpStatus(status);
                            result = new Dictionary<string, object>
                            {
                                { "id", docId },
                                { "error", info.Item2 },
                                { "status", info.Item1 }
                            };
                        }

                        if(result != null) {
                            results.Add(result);
                        }
                    }

                    response.Body = new Body(results.Cast<object>().ToList());
                    return true;
                });

                if(!success) {
                    response.InternalStatus = status;
                }

                return response;
            }).AsDefaultState();
        }

        public static ICouchbaseResponseState GetChanges(HttpListenerContext context)
        {
            // http://wiki.apache.org/couchdb/HTTP_database_API#Changes

            DBMonitorCouchbaseResponseState responseState = new DBMonitorCouchbaseResponseState();

            var responseObject = PerformLogicWithDatabase(context, true, db =>
            {
                var response = new CouchbaseLiteResponse(context);
                responseState.ChangesMode = ParseChangesOptions(context);
                if (responseState.ChangesMode < ChangesFeedMode.Continuous) {
                    if(CacheWithEtag(db.LastSequenceNumber.ToString(), context, response)) {
                        response.InternalStatus = StatusCode.NotModified;
                        return response;
                    }
                }

                var query = context.Request.QueryString;
                var options = new ChangesOptions();
                responseState.ChangesIncludeDocs = query.Get<bool>("include_docs", bool.TryParse, false);
                options.SetIncludeDocs(responseState.ChangesIncludeDocs);
                responseState.ChangesIncludeConflicts = query.Get("style") == "all_docs";
                options.SetIncludeConflicts(responseState.ChangesIncludeConflicts);
                options.SetContentOptions(GetContentOptions(context));
                options.SetSortBySequence(!options.IsIncludeConflicts());
                options.SetLimit(query.Get<int>("limit", int.TryParse, options.GetLimit()));
                int since = query.Get<int>("since", int.TryParse, 0);

                string filterName = query.Get("filter");
                if(filterName != null) {
                    Status status = new Status();
                    responseState.ChangesFilter = db.GetFilter(filterName, status);
                    if(responseState.ChangesFilter == null) {
                        return new CouchbaseLiteResponse(context) { InternalStatus = status.GetCode() };
                    }

                    Log.D(TAG, "Filter params={0}", query);
                }


                RevisionList changes = db.ChangesSince(since, options, responseState.ChangesFilter);
                if((responseState.ChangesMode >= ChangesFeedMode.Continuous) || 
                    (responseState.ChangesMode == ChangesFeedMode.LongPoll && changes.Count == 0)) {
                    // Response is going to stay open (continuous, or hanging GET):
                    if(responseState.ChangesMode == ChangesFeedMode.EventSource) {
                        response["Content-Type"] = "text/event-stream; charset=utf-8";
                    }

                    if(responseState.ChangesMode >= ChangesFeedMode.Continuous) {
                        response.WriteHeaders();
                        response.Chunked = true;
                        foreach(var rev in changes) {
                            SendContinuousLine(ChangesDictForRev(rev, responseState), responseState);
                        }
                    }

                    responseState.SubscribeToDatabase(db);
                    string heartbeatParam = query.Get("heartbeat");
                    if(heartbeatParam != null) {
                        double heartbeat;
                        if(!double.TryParse(heartbeatParam, out heartbeat) || heartbeat <= 0) {
                            responseState.IsAsync = false;
                            return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadParam };
                        }

                        heartbeat = Math.Min(heartbeat, MIN_HEARTBEAT);
                        string heartbeatResponse = responseState.ChangesMode == ChangesFeedMode.EventSource ? "\n\n" : "\r\n";
                        responseState.StartHeartbeat(heartbeatResponse, heartbeat);
                    }

                    return new CouchbaseLiteResponse(context);
                } else {
                    if(responseState.ChangesIncludeConflicts) {
                        
                    } else {
                        response.Body = new Body(ResponseBodyForChanges(changes, since, responseState));
                    }

                    return response;
                }
            });

            responseState.Response = responseObject;
            return responseState;
        }

        private static CouchbaseLiteResponse PerformLogicWithDatabase(HttpListenerContext context, bool open, 
            Func<Database, CouchbaseLiteResponse> action) 
        {
            string[] components = context.Request.Url.AbsolutePath.Split(new[]{ '/' }, StringSplitOptions.RemoveEmptyEntries);
            string dbName = components[0];
            Database db = Manager.SharedInstance.GetDatabaseWithoutOpening(dbName, false);
            if (db == null || !db.Exists()) {
                return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.NotFound };
            }

            if (open) {
                bool opened = false;
                try {
                    opened = db.Open();
                } catch (CouchbaseLiteException) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.Exception };
                }

                if (!opened) {
                    return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.DbError };
                }
            }

            return action(db);
        }

        private static QueryOptions GetQueryOptions(HttpListenerContext context)
        {
            var options = new QueryOptions();
            var queryStr = context.Request.QueryString;
            options.SetSkip(queryStr.Get<int>("skip", int.TryParse, options.GetSkip()));
            options.SetLimit(queryStr.Get<int>("limit", int.TryParse, options.GetLimit()));
            options.SetGroupLevel(queryStr.Get<int>("group_level", int.TryParse, options.GetGroupLevel()));
            options.SetDescending(queryStr.Get<bool>("descending", bool.TryParse, options.IsDescending()));
            options.SetIncludeDocs(queryStr.Get<bool>("include_docs", bool.TryParse, options.IsIncludeDocs()));
            if (queryStr.Get<bool>("include_deleted", bool.TryParse, false)) {
                options.SetAllDocsMode(AllDocsMode.IncludeDeleted);
            } else if (queryStr.Get<bool>("include_conflicts", bool.TryParse, false)) { //non-standard
                options.SetAllDocsMode(AllDocsMode.ShowConflicts); 
            } else if(queryStr.Get<bool>("only_conflicts", bool.TryParse, false)) { //non-standard
                options.SetAllDocsMode(AllDocsMode.OnlyConflicts);
            }

            options.SetUpdateSeq(queryStr.Get<bool>("update_seq", bool.TryParse, false));
            options.SetInclusiveEnd(queryStr.Get<bool>("inclusive_end", bool.TryParse, false));
            //TODO: InclusiveStart
            //TODO: PrefixMatchLevel
            options.SetReduceSpecified(queryStr.Get("reduce") != null);
            options.SetReduce(queryStr.Get<bool>("reduce", bool.TryParse, false));
            options.SetGroup(queryStr.Get<bool>("group", bool.TryParse, false));
            options.SetContentOptions(GetContentOptions(context));

            // Stale options (ok or update_after):
            string stale = queryStr.Get("stale");
            if(stale != null) {
                if (stale.Equals("ok")) {
                    options.SetStale(IndexUpdateMode.Never);
                } else if (stale.Equals("update_after")) {
                    options.SetStale(IndexUpdateMode.After);
                }
            }

            IList<object> keys = null;
            try {
                keys = queryStr.JsonQuery("keys").AsList<object>();
                if (keys == null) {
                    var key = queryStr.JsonQuery("key");
                    if (key != null) {
                        keys = new List<object> { key };
                    }
                }
            } catch(CouchbaseLiteException) {
                return null;
            }

            if (keys != null) {
                options.SetKeys(keys);
            } else {
                try {
                    options.SetStartKey(queryStr.JsonQuery("start_key"));
                    options.SetEndKey(queryStr.JsonQuery("end_key"));
                    options.SetStartKeyDocId(queryStr.JsonQuery("startkey_docid") as string);
                    options.SetEndKeyDocId(queryStr.JsonQuery("endkey_docid") as string);
                } catch(CouchbaseLiteException) {
                    return null;
                }
            }

            //TODO:  Full text and bbox

            return options;
        }

        private static DocumentContentOptions GetContentOptions(HttpListenerContext context)
        {
            var queryStr = context.Request.QueryString;
            DocumentContentOptions options = DocumentContentOptions.None;
            if (queryStr.Get<bool>("attachments", bool.TryParse, false)) {
                options |= DocumentContentOptions.IncludeAttachments;
            }

            if (queryStr.Get<bool>("local_seq", bool.TryParse, false)) {
                options |= DocumentContentOptions.IncludeLocalSeq;
            }

            if (queryStr.Get<bool>("conflicts", bool.TryParse, false)) {
                options |= DocumentContentOptions.IncludeConflicts;
            }

            if (queryStr.Get<bool>("revs", bool.TryParse, false)) {
                options |= DocumentContentOptions.IncludeRevs;
            }

            if (queryStr.Get<bool>("revs_info", bool.TryParse, false)) {
                options |= DocumentContentOptions.IncludeRevsInfo;
            }

            return options;
        }

        private static CouchbaseLiteResponse DoAllDocs(HttpListenerContext context, Database db, QueryOptions options)
        {
            var result = db.GetAllDocs(options);
            if (!result.ContainsKey("rows")) {
                return new CouchbaseLiteResponse(context) { InternalStatus = StatusCode.BadJson };
            }

            var documentProps = from row in (List<QueryRow>)result["rows"] select row.Document.Properties;
            result["rows"] = documentProps;
            var response = new CouchbaseLiteResponse(context);
            response.Body = new Body(result);
            return response;
        }

        private static IDictionary<string, object> GetPostBody(HttpListenerContext context) 
        {
            if(context.Request.HasEntityBody) {
                try {
                    var body = Manager.GetObjectMapper().ReadValue<IDictionary<string, object>>(context.Request.InputStream);
                    return body;
                } catch(CouchbaseLiteException) {
                    return null;
                }
            }

            return new Dictionary<string, object>();
        }

        private static StatusCode UpdateDocument(HttpListenerContext context, Database db, string docId, Body body, bool deleting, 
            bool allowConflict, out RevisionInternal outRev)
        {
            outRev = null;
            if (body != null && !body.IsValidJSON()) {
                return StatusCode.BadJson;
            }

            string prevRevId;
            if (!deleting) {
                var properties = body.GetProperties();
                deleting = properties.GetCast<bool>("_deleted");
                if (docId == null) {
                    // POST's doc ID may come from the _id field of the JSON body.
                    docId = properties.GetCast<string>("_id");
                    if (docId == null && deleting) {
                        return StatusCode.BadId;
                    }
                }

                // PUT's revision ID comes from the JSON body.
                prevRevId = properties.GetCast<string>("_rev");
            } else {
                // DELETE's revision ID comes from the ?rev= query param
                prevRevId = context.Request.QueryString.Get("rev");
            }

            // A backup source of revision ID is an If-Match header:
            if (prevRevId == null) {
                prevRevId = IfMatch(context.Request);
            }

            if (docId == null && deleting) {
                return StatusCode.BadId;
            }

            RevisionInternal rev = new RevisionInternal(docId, null, deleting);
            rev.SetBody(body);

            StatusCode status = StatusCode.Ok;
            try {
                if (docId.StartsWith("_local")) {
                    outRev = db.PutLocalRevision(rev, prevRevId); //TODO: Doesn't match iOS
                } else {
                    Status retStatus = new Status();
                    outRev = db.PutRevision(rev, prevRevId, allowConflict, retStatus);
                    status = retStatus.GetCode();
                }
            } catch(CouchbaseLiteException e) {
                status = e.Code;
            }

            return status;
        }

        private static string IfMatch(HttpListenerRequest request)
        {
            string ifMatch = request.Headers.Get("If-Match");
            if (ifMatch == null) {
                return null;
            }

            // Value of If-Match is an ETag, so have to trim the quotes around it:
            if (ifMatch.Length > 2 && ifMatch.StartsWith("\"") && ifMatch.EndsWith("\"")) {
                return ifMatch.Trim('"');
            }

            return null;
        }

        private static ChangesFeedMode ParseChangesOptions(HttpListenerContext context)
        {
            var query = context.Request.QueryString;
            ChangesFeedMode retVal = ChangesFeedMode.Normal;
            string feed = query.Get("feed");
            if (feed == null) {
                return retVal;
            }

            if (feed.Equals("longpoll")) {
                retVal = ChangesFeedMode.LongPoll;
            } else if (feed.Equals("continuous")) {
                retVal = ChangesFeedMode.Continuous;
            } else if (feed.Equals("eventsource")) {
                retVal = ChangesFeedMode.EventSource;
            }

            return retVal;
        }

        private static bool CacheWithEtag(string etag, HttpListenerContext context, CouchbaseLiteResponse response)
        {
            etag = String.Format("\"{0}\"", etag);
            response["Etag"] = etag;
            return etag.Equals(context.Request.Headers.Get("If-None-Match"));
        }

        private static IDictionary<string, object> ResponseBodyForChanges(RevisionList changes, long since, int limit)
        {
            throw new NotImplementedException();
        }

        public static IDictionary<string, object> ResponseBodyForChanges(RevisionList changes, long since, DBMonitorCouchbaseResponseState responseState)
        {
            List<IDictionary<string, object>> results = new List<IDictionary<string, object>>();
            foreach (var change in changes) {
                results.Add(DatabaseMethods.ChangesDictForRev(change, responseState));
            }

            if (changes.Count > 0) {
                since = changes.Last().GetSequence();
            }

            return new Dictionary<string, object> {
                { "results", results },
                { "last_seq", since }
            };
        }

        // Send a JSON object followed by a newline without closing the connection.
        // Used by the continuous mode of _changes and _active_tasks.
        public static void SendContinuousLine(IDictionary<string, object> changesDict, DBMonitorCouchbaseResponseState responseState)
        {
            var json = Manager.GetObjectMapper().WriteValueAsBytes(changesDict).ToList();
            if (responseState.ChangesMode == ChangesFeedMode.EventSource) {
                // https://developer.mozilla.org/en-US/docs/Server-sent_events/Using_server-sent_events#Event_stream_format
                json.InsertRange(0, Encoding.UTF8.GetBytes("data: "));
                json.AddRange(Encoding.UTF8.GetBytes("\n\n"));
            } else {
                json.AddRange(Encoding.UTF8.GetBytes("\n"));
            }

            responseState.Response.WriteData(json, false);
        }

        public static IDictionary<string, object> ChangesDictForRev(RevisionInternal rev, DBMonitorCouchbaseResponseState responseState)
        {
            return new Dictionary<string, object> {
                { "seq", rev.GetSequence() },
                { "id", rev.GetDocId() },
                { "changes", new List<object> { 
                        new Dictionary<string, object> { 
                            { "rev", rev.GetRevId() } 
                        } 
                    } 
                },
                { "deleted", rev.IsDeleted() ? (object)true : null },
                { "doc", responseState.ChangesIncludeDocs ? rev.GetProperties() : null }
            };
        }
    }


}

