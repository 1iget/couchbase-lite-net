//
// ReplicationTest.cs
//
// Author:
//     Zachary Gramana  <zack@xamarin.com>
//
// Copyright (c) 2014 Xamarin Inc
// Copyright (c) 2014 .NET Foundation
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
//
// Copyright (c) 2014 Couchbase, Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file
// except in compliance with the License. You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed under the
// License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND,
// either express or implied. See the License for the specific language governing permissions
// and limitations under the License.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

using Couchbase.Lite;
using Couchbase.Lite.Auth;
using Couchbase.Lite.Internal;
using Couchbase.Lite.Replicator;
using Couchbase.Lite.Support;

using Couchbase.Lite.Util;
using NUnit.Framework;

using Sharpen;
using System.Threading.Tasks;
using System.Net.Http;
using System.Configuration;
using System.Web;
using Couchbase.Lite.Tests;
using System.Web.UI;
using System.Text;
using System.Net.Mime;

namespace Couchbase.Lite.Replicator
{
	public class ReplicationTest : LiteTestCase
	{
        public const string Tag = "ReplicationTest";

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestPusher()
		{
			var replicationDoneSignal = new CountDownLatch(1);
			var remote = GetReplicationURL();
			var docIdTimestamp = Convert.ToString(Runtime.CurrentTimeMillis());

			// Create some documents:
			var documentProperties = new Dictionary<string, object>();
			var doc1Id = string.Format("doc1-{0}", docIdTimestamp);
			documentProperties["_id"] = doc1Id;
			documentProperties["foo"] = 1;
			documentProperties["bar"] = false;

			var body = new Body(documentProperties);
			var rev1 = new RevisionInternal(body, database);
			var status = new Status();
			rev1 = database.PutRevision(rev1, null, false, status);
            Assert.AreEqual(StatusCode.Created, status.GetCode());

			documentProperties.Put("_rev", rev1.GetRevId());
			documentProperties["UPDATED"] = true;
			var rev2 = database.PutRevision(new RevisionInternal(documentProperties, database), rev1.GetRevId(), false, status);
            Assert.AreEqual(StatusCode.Created, status.GetCode());

			documentProperties = new Dictionary<string, object>();
			var doc2Id = string.Format("doc2-{0}", docIdTimestamp);
			documentProperties["_id"] = doc2Id;
			documentProperties["baz"] = 666;
			documentProperties["fnord"] = true;

			database.PutRevision(new RevisionInternal(documentProperties, database), null, false, status);
            Assert.AreEqual(StatusCode.Created, status.GetCode());

			var continuous = false;
			var repl = database.CreatePushReplication(remote);
            repl.Continuous = continuous;

            //repl.CreateTarget = false; 

			// Check the replication's properties:
			Assert.AreEqual(database, repl.LocalDatabase);
			Assert.AreEqual(remote, repl.RemoteUrl);
			Assert.IsFalse(repl.IsPull);
            Assert.IsFalse(repl.Continuous);
            //Assert.IsTrue(repl.CreateTarget);
			Assert.IsNull(repl.Filter);
			Assert.IsNull(repl.FilterParams);
			// TODO: CAssertNil(r1.doc_ids);
			// TODO: CAssertNil(r1.headers);
			// Check that the replication hasn't started running:
			Assert.IsFalse(repl.IsRunning);
            Assert.AreEqual((int)repl.Status, (int)ReplicationStatus.Stopped);
			Assert.AreEqual(0, repl.CompletedChangesCount);
			Assert.AreEqual(0, repl.ChangesCount);
			Assert.IsNull(repl.LastError);
            RunReplication(repl);
			// make sure doc1 is there
			// TODO: make sure doc2 is there (refactoring needed)
            var replicationUrlTrailing = new Uri(string.Format("{0}/", remote));
			var pathToDoc = new Uri(replicationUrlTrailing, doc1Id);
			Log.D(Tag, "Send http request to " + pathToDoc);
			var httpRequestDoneSignal = new CountDownLatch(1);
            var getDocTask = Task.Factory.StartNew(()=>
                {
                    var httpclient = new HttpClient();
                    HttpResponseMessage response;
                    string responseString = null;
                    try
                    {
                        var responseTask = httpclient.GetAsync(pathToDoc.ToString());
                        responseTask.Wait(TimeSpan.FromSeconds(10));
                        response = responseTask.Result;
                        var statusLine = response.StatusCode;
                        NUnit.Framework.Assert.IsTrue(statusLine == HttpStatusCode.OK);
                        if (statusLine == HttpStatusCode.OK)
                        {
                            var responseStringTask = response.Content.ReadAsStringAsync();
                            responseStringTask.Wait(TimeSpan.FromSeconds(10));
                            responseString = responseStringTask.Result;
                            NUnit.Framework.Assert.IsTrue(responseString.Contains(doc1Id));
                            Log.D(ReplicationTest.Tag, "result: " + responseString);
                        }
                        else
                        {
                            var statusReason = response.ReasonPhrase;
                            response.Dispose();
                            throw new IOException(statusReason);
                        }
                    }
                    catch (ProtocolViolationException e)
                    {
                        NUnit.Framework.Assert.IsNull(e, "Got ClientProtocolException: " + e.Message);
                    }
                    catch (IOException e)
                    {
                        NUnit.Framework.Assert.IsNull(e, "Got IOException: " + e.Message);
                    }
                    httpRequestDoneSignal.CountDown();
                });
			//Closes the connection.
			Log.D(Tag, "Waiting for http request to finish");
			try
			{
                var result = httpRequestDoneSignal.Await(TimeSpan.FromSeconds(10));
                Assert.IsTrue(result, "Could not retrieve the new doc from the sync gateway.");
				Log.D(Tag, "http request finished");
			}
			catch (Exception e)
			{
				Sharpen.Runtime.PrintStackTrace(e);
			}
			Log.D(Tag, "testPusher() finished");
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestPusherDeletedDoc()
		{
			var remote = GetReplicationURL();
			var docIdTimestamp = Convert.ToString(Runtime.CurrentTimeMillis());
			// Create some documentsConvert
			var documentProperties = new Dictionary<string, object>();
			var doc1Id = string.Format("doc1-{0}", docIdTimestamp);
			documentProperties["_id"] = doc1Id;
			documentProperties["foo"] = 1;
			documentProperties["bar"] = false;
			var body = new Body(documentProperties);
			var rev1 = new RevisionInternal(body, database);
			var status = new Status();
			rev1 = database.PutRevision(rev1, null, false, status);
            Assert.AreEqual(StatusCode.Created, status.GetCode());

            documentProperties["_rev"] = rev1.GetRevId();
			documentProperties["UPDATED"] = true;
			documentProperties["_deleted"] = true;
			database.PutRevision(new RevisionInternal(documentProperties, database), rev1.GetRevId(), false, status);
            Assert.IsTrue((int)status.GetCode() >= 200 && (int)status.GetCode() < 300);

            var repl = database.CreatePushReplication(remote);
            ((Pusher)repl).CreateTarget = true;
			RunReplication(repl);
			// make sure doc1 is deleted
			var replicationUrlTrailing = new Uri(string.Format ("{0}/", remote));
			var pathToDoc = new Uri(replicationUrlTrailing, doc1Id);
			Log.D(Tag, "Send http request to " + pathToDoc);
			var httpRequestDoneSignal = new CountDownLatch(1);
			Task.Factory.StartNew(async ()=>
                {
                    var httpclient = new HttpClient();
                    try
                    {
						var getDocResponse = await httpclient.GetAsync(pathToDoc.ToString());
                        var statusLine = getDocResponse.StatusCode;
                        Log.D(ReplicationTest.Tag, "statusLine " + statusLine);
                        Assert.AreEqual(HttpStatusCode.NotFound, statusLine.GetStatusCode());                        
                    }
                    catch (ProtocolViolationException e)
                    {
                        Assert.IsNull(e, "Got ClientProtocolException: " + e.Message);
                    }
                    catch (IOException e)
                    {
                        Assert.IsNull(e, "Got IOException: " + e.Message);
                    }
                    finally
                    {
                        httpRequestDoneSignal.CountDown();
                    }
                });
			Log.D(Tag, "Waiting for http request to finish");
			try
			{
                httpRequestDoneSignal.Await(TimeSpan.FromSeconds(10));
				Log.D(Tag, "http request finished");
			}
			catch (Exception e)
			{
				Runtime.PrintStackTrace(e);
			}
			Log.D(Tag, "testPusherDeletedDoc() finished");
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestPuller()
		{
			var docIdTimestamp = System.Convert.ToString(Runtime.CurrentTimeMillis());
            var doc1Id = string.Format("doc1-{0}", docIdTimestamp);
            var doc2Id = string.Format("doc2-{0}", docIdTimestamp);
            AddDocWithId(doc1Id, "attachment.png");
            AddDocWithId(doc2Id, "attachment2.png");

			// workaround for https://github.com/couchbase/sync_gateway/issues/228
			Sharpen.Thread.Sleep(1000);
            DoPullReplication();
            Sharpen.Thread.Sleep(5000);

			Log.D(Tag, "Fetching doc1 via id: " + doc1Id);
            var doc1 = database.GetExistingDocument(doc1Id);
			Assert.IsNotNull(doc1);
            Assert.IsNotNull(doc1.CurrentRevisionId);
            Assert.IsTrue(doc1.CurrentRevisionId.StartsWith("1-"));
            Assert.IsNotNull(doc1.Properties);
            Assert.AreEqual(1, doc1.GetProperty("foo"));

            Log.D(Tag, "Fetching doc2 via id: " + doc2Id);
            var doc2 = database.GetExistingDocument(doc2Id);
			Assert.IsNotNull(doc2);
            Assert.IsNotNull(doc2.CurrentRevisionId);
            Assert.IsTrue(doc2.CurrentRevisionId.StartsWith("1-"));
            Assert.IsNotNull(doc2.Properties);
            Assert.AreEqual(1, doc2.GetProperty("foo"));
            Log.D(Tag, "testPuller() finished");
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestPullerWithLiveQuery()
		{
            Assert.Fail(); // TODO.ZJG: Needs debugging.

			// This is essentially a regression test for a deadlock
			// that was happening when the LiveQuery#onDatabaseChanged()
			// was calling waitForUpdateThread(), but that thread was
			// waiting on connection to be released by the thread calling
			// waitForUpdateThread().  When the deadlock bug was present,
			// this test would trigger the deadlock and never finish.
			Log.D(Database.Tag, "testPullerWithLiveQuery");
			string docIdTimestamp = System.Convert.ToString(Runtime.CurrentTimeMillis());
            string doc1Id = string.Format("doc1-{0}", docIdTimestamp);
            string doc2Id = string.Format("doc2-{0}", docIdTimestamp);

			AddDocWithId(doc1Id, "attachment2.png");
			AddDocWithId(doc2Id, "attachment2.png");

			int numDocsBeforePull = database.DocumentCount;
			View view = database.GetView("testPullerWithLiveQueryView");
            view.SetMapReduce((document, emitter) => {
                if (document.Get ("_id") != null) {
                    emitter (document.Get ("_id"), null);
                }
            }, null, "1");

			LiveQuery allDocsLiveQuery = view.CreateQuery().ToLiveQuery();
            allDocsLiveQuery.Changed += (sender, e) => {
                int numTimesCalled = 0;
                if (e.Error != null)
                {
                    throw new RuntimeException(e.Error);
                }
                if (numTimesCalled++ > 0)
                {
                    NUnit.Framework.Assert.IsTrue(e.Rows.Count > numDocsBeforePull);
                }
                Log.D(Database.Tag, "rows " + e.Rows);
            };
			// the first time this is called back, the rows will be empty.
			// but on subsequent times we should expect to get a non empty
			// row set.
			allDocsLiveQuery.Start();
			DoPullReplication();
		}

		private void DoPullReplication()
		{
            var remote = GetReplicationURL();
            var repl = database.CreatePullReplication(remote);
			repl.Continuous = false;
			RunReplication(repl);
		}

		/// <exception cref="System.IO.IOException"></exception>
		private void AddDocWithId(string docId, string attachmentName)
		{
			string docJson;
            if (attachmentName == null)
			{
				// add attachment to document
                var attachmentStream = GetAsset(attachmentName);
                var baos = new MemoryStream();
                attachmentStream.Wrapped.CopyTo(baos);
                var attachmentBase64 = Convert.ToBase64String(baos.ToArray());
                docJson = String.Format("{{\"foo\":1,\"bar\":false, \"_attachments\": {{ \"i_use_couchdb.png\": {{ \"content_type\": \"image/png\", \"data\": \"{0}\" }} }} }}", attachmentBase64);
			}
			else
			{
                docJson = @"{""foo"":1,""bar"":false}";
			}
			// push a document to server
            var replicationUrlTrailingDoc1 = new Uri(string.Format("{0}/{1}", GetReplicationURL(), docId));
			var pathToDoc1 = new Uri(replicationUrlTrailingDoc1, docId);
			Log.D(Tag, "Send http request to " + pathToDoc1);
			CountDownLatch httpRequestDoneSignal = new CountDownLatch(1);
            var getDocTask = Task.Factory.StartNew(()=>
                {
                    var httpclient = new HttpClient(); //CouchbaseLiteHttpClientFactory.Instance.GetHttpClient();
                    HttpResponseMessage response;
                    try
                    {
                        var request = new HttpRequestMessage();
                        request.Headers.Add("Accept", "*/*");
                        //request.Headers..Add("Content-Type", "application/json");
                        var postTask = httpclient.PutAsync(pathToDoc1.AbsoluteUri, new StringContent(docJson, Encoding.UTF8, "application/json"));
                        //var postTask = httpclient.PutAsJsonAsync(pathToDoc1.AbsoluteUri, docJson);
                        postTask.Wait();
                        response = postTask.Result;
                        var statusLine = response.StatusCode;
                        Log.D(ReplicationTest.Tag, "Got response: " + statusLine);
                        NUnit.Framework.Assert.IsTrue(statusLine == HttpStatusCode.Created);
                    }
                    catch (ProtocolViolationException e)
                    {
                        NUnit.Framework.Assert.IsNull(e, "Got ClientProtocolException: " + e.Message);
                    }
                    catch (IOException e)
                    {
                        NUnit.Framework.Assert.IsNull(e, "Got IOException: " + e.Message);
                    }
                    httpRequestDoneSignal.CountDown();
                });
            //getDocTask.Start();
			Log.D(Tag, "Waiting for http request to finish");
			try
			{
				httpRequestDoneSignal.Await(TimeSpan.FromSeconds(10));
				Log.D(Tag, "http request finished");
			}
			catch (Exception e)
			{
				Sharpen.Runtime.PrintStackTrace(e);
			}
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestGetReplicator()
		{
            var replicationUrl = GetReplicationURL();
            var db = StartDatabase();

            var replicator = db.CreatePullReplication(replicationUrl);
            Assert.IsNotNull(replicator);
            Assert.IsTrue(!replicator.IsPull);
            Assert.IsFalse(replicator.Continuous);
            Assert.IsFalse(replicator.IsRunning);
            // start the replicator
            replicator.Start();
            Assert.IsFalse(replicator.IsRunning);
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestGetReplicatorWithAuth()
		{
            var db = StartDatabase();
            var url = GetReplicationURLWithoutCredentials();
            Replication replicator = db.CreatePushReplication(url);
			NUnit.Framework.Assert.IsNotNull(replicator);
            Assert.IsNotNull(replicator.Authorizer);
            Assert.IsTrue(replicator.Authorizer is FacebookAuthorizer);
		}

		private void RunReplication(Replication replication)
		{
//			var replicationDoneSignal = new CountDownLatch(1);
            var replicationDoneSignalPolling = ReplicationWatcherThread(replication);
            replication.Changed += (sender, e) => 
                replicationDoneSignalPolling.CountDown ();
			replication.Start();

			Log.D(Tag, "Waiting for replicator to finish");

			try
			{
                var success = replicationDoneSignalPolling.Await(TimeSpan.FromSeconds(15));
				Assert.IsTrue(success);
                Sharpen.Thread.Sleep(5000);
                replication.Stop();
                Log.D(Tag, "replicator finished");
			}
			catch (Exception e)
			{
				Runtime.PrintStackTrace(e);
			}
		}

		private CountDownLatch ReplicationWatcherThread(Replication replication)
		{
            var doneSignal = new CountDownLatch(2);
            Task.Factory.StartNew(()=>
                {
                    var started = false;
                    var done = false;

                    while (!done)
                    {
                        started |= replication.IsRunning;
                        var statusIsDone = (
                            replication.Status == ReplicationStatus.Stopped 
                            || replication.Status == ReplicationStatus.Idle
                        );
                        if (started && statusIsDone)
                        {
                            done = true;
                        }
                        try
                        {
                            Thread.Sleep(10000);
                        }
                        catch (Exception e)
                        {
                            Runtime.PrintStackTrace(e);
                        }
                    }
                    doneSignal.CountDown();
                });
            return doneSignal;
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestRunReplicationWithError()
		{
            var mockHttpClientFactory = new AlwaysFailingClientFactory();
			var dbUrlString = "http://fake.test-url.com:4984/fake/";
            var remote = new Uri(dbUrlString);
            var continuous = false;
            var r1 = new Puller(database, remote, continuous, mockHttpClientFactory, manager.workExecutor);
			Assert.IsFalse(r1.Continuous);

            RunReplication(r1);

			// It should have failed with a 404:
            Assert.AreEqual(ReplicationStatus.Stopped, r1.Status);			
			Assert.AreEqual(0, r1.CompletedChangesCount);
			Assert.AreEqual(0, r1.ChangesCount);
			Assert.IsNotNull(r1.LastError);
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestReplicatorErrorStatus()
		{
            Assert.Fail(); // TODO.ZJG: Needs FB login stuff removed.

			// register bogus fb token
			IDictionary<string, object> facebookTokenInfo = new Dictionary<string, object>();
			facebookTokenInfo["email"] = "jchris@couchbase.com";
			facebookTokenInfo.Put("remote_url", GetReplicationURL().ToString());
			facebookTokenInfo["access_token"] = "fake_access_token";

            var destUrl = string.Format("{0}/_facebook_token", DefaultTestDb);
			var result = (IDictionary<string, object>)SendBody("POST", destUrl, facebookTokenInfo, (int)StatusCode.Ok, null);
			Log.V(Tag, string.Format("result {0}", result));
			// start a replicator
			IDictionary<string, object> properties = GetPullReplicationParsedJson();
            Replication replicator = manager.GetExistingDatabase(DefaultTestDb).CreatePushReplication(new Uri(destUrl));
			replicator.Start();
			bool foundError = false;
			for (int i = 0; i < 10; i++)
			{
				// wait a few seconds
				Sharpen.Thread.Sleep(5 * 1000);
                // expect an error since it will try to contact the sync gateway with this bogus login,
                // and the sync gateway will reject it.
                var activeTasks = (AList<object>)Send("GET", "/_active_tasks", HttpStatusCode.OK, null);
				Log.D(Tag, "activeTasks: " + activeTasks);
				IDictionary<string, object> activeTaskReplication = (IDictionary<string, object>)
					activeTasks[0];
				foundError = (activeTaskReplication["error"] != null);
				if (foundError == true)
				{
					break;
				}
			}
			NUnit.Framework.Assert.IsTrue(foundError);
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestFetchRemoteCheckpointDoc()
		{
            var mockHttpClientFactory = new AlwaysFailingClientFactory();
            mockHttpClientFactory.GetHttpClient();

			Log.D("TEST", "testFetchRemoteCheckpointDoc() called");
			string dbUrlString = "http://fake.test-url.com:4984/fake/";
			Uri remote = new Uri(dbUrlString);
            database.SetLastSequence("1", dbUrlString, true);
			// otherwise fetchRemoteCheckpoint won't contact remote
            Assert.Fail();
			Replication replicator = new Pusher(database, remote, false, mockHttpClientFactory
                , manager.workExecutor);
			CountDownLatch doneSignal = new CountDownLatch(1);
			ReplicationTest.ReplicationObserver replicationObserver = new ReplicationTest.ReplicationObserver
				(this, doneSignal);
            replicator.Changed += replicationObserver.Changed;
			replicator.FetchRemoteCheckpointDoc();
			Log.D(Tag, "testFetchRemoteCheckpointDoc() Waiting for replicator to finish");
			try
			{
                bool succeeded = doneSignal.Await(TimeSpan.FromSeconds(3));
                NUnit.Framework.Assert.IsTrue(succeeded);
                Log.D(Tag, "testFetchRemoteCheckpointDoc() replicator finished");
			}
			catch (Exception e)
			{
				Sharpen.Runtime.PrintStackTrace(e);
			}
			string errorMessage = "Since we are passing in a mock http client that always throws "
				 + "errors, we expect the replicator to be in an error state";
            NUnit.Framework.Assert.IsNotNull(replicator.LastError, errorMessage);
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestGoOffline()
		{
			Uri remote = GetReplicationURL();
			CountDownLatch replicationDoneSignal = new CountDownLatch(1);
			Replication repl = database.CreatePullReplication(remote);
			repl.Continuous = true;
            repl.Start();
			repl.GoOffline();
            NUnit.Framework.Assert.IsTrue(repl.Status == ReplicationStatus.Offline);
		}

		internal class ReplicationObserver 
		{
			public bool replicationFinished = false;

			private CountDownLatch doneSignal;

			internal ReplicationObserver(ReplicationTest _enclosing, CountDownLatch doneSignal
				)
			{
				this._enclosing = _enclosing;
				this.doneSignal = doneSignal;
			}

            public virtual void Changed(object sender, Replication.ReplicationChangeEventArgs args)
			{
                Replication replicator = args.Source;
				if (!replicator.IsRunning)
				{
					this.replicationFinished = true;
					string msg = string.Format("myobserver.update called, set replicationFinished to: %b"
						, this.replicationFinished);
					Log.D(ReplicationTest.Tag, msg);
					this.doneSignal.CountDown();
				}
				else
				{
					string msg = string.Format("myobserver.update called, but replicator still running, so ignore it"
						);
					Log.D(ReplicationTest.Tag, msg);
				}
			}

			internal virtual bool IsReplicationFinished()
			{
				return this.replicationFinished;
			}

			private readonly ReplicationTest _enclosing;
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestBuildRelativeURLString()
		{
			string dbUrlString = "http://10.0.0.3:4984/todos/";
			Replication replicator = new Pusher(null, new Uri(dbUrlString), false, null);
			string relativeUrlString = replicator.BuildRelativeURLString("foo");
			string expected = "http://10.0.0.3:4984/todos/foo";
			NUnit.Framework.Assert.AreEqual(expected, relativeUrlString);
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestBuildRelativeURLStringWithLeadingSlash()
		{
			string dbUrlString = "http://10.0.0.3:4984/todos/";
			Replication replicator = new Pusher(null, new Uri(dbUrlString), false, null);
			string relativeUrlString = replicator.BuildRelativeURLString("/foo");
			string expected = "http://10.0.0.3:4984/todos/foo";
			NUnit.Framework.Assert.AreEqual(expected, relativeUrlString);
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestChannels()
		{
			Uri remote = GetReplicationURL();
			Replication replicator = database.CreatePullReplication(remote);
			IList<string> channels = new AList<string>();
			channels.AddItem("chan1");
			channels.AddItem("chan2");
            Assert.Fail();
//            replicator.Channels(channels);
//			NUnit.Framework.Assert.AreEqual(channels, replicator.GetChannels());
//			Assert(null);
//			NUnit.Framework.Assert.IsTrue(replicator.GetChannels().IsEmpty());
		}

		/// <exception cref="System.UriFormatException"></exception>
        [Test]
		public virtual void TestChannelsMore()
		{
            Database db = StartDatabase();
            Uri fakeRemoteURL = new Uri("http://couchbase.com/no_such_db");
            Replication r1 = db.CreatePullReplication(fakeRemoteURL);
            Assert.Fail();
//            NUnit.Framework.Assert.IsTrue(r1.GetChannels().IsEmpty());
//            r1.SetFilter("foo/bar");
//            NUnit.Framework.Assert.IsTrue(r1.GetChannels().IsEmpty());
//            IDictionary<string, object> filterParams = new Dictionary<string, object>();
//            filterParams.Put("a", "b");
//            r1.SetFilterParams(filterParams);
//            NUnit.Framework.Assert.IsTrue(r1.GetChannels().IsEmpty());
//            r1.SetChannels(null);
//            NUnit.Framework.Assert.AreEqual("foo/bar", r1.GetFilter());
//            NUnit.Framework.Assert.AreEqual(filterParams, r1.GetFilterParams());
//            IList<string> channels = new AList<string>();
//            channels.AddItem("NBC");
//            channels.AddItem("MTV");
//            r1.SetChannels(channels);
//            NUnit.Framework.Assert.AreEqual(channels, r1.GetChannels());
//            NUnit.Framework.Assert.AreEqual("sync_gateway/bychannel", r1.GetFilter());
//            filterParams = new Dictionary<string, object>();
//            filterParams.Put("channels", "NBC,MTV");
//            NUnit.Framework.Assert.AreEqual(filterParams, r1.GetFilterParams());
//            r1.SetChannels(null);
//            NUnit.Framework.Assert.AreEqual(r1.GetFilter(), null);
//            NUnit.Framework.Assert.AreEqual(null, r1.GetFilterParams());
		}

		/// <exception cref="System.Exception"></exception>
        [Test]
		public virtual void TestHeaders()
		{
            Assert.Fail();
//			CustomizableMockHttpClient mockHttpClient = new CustomizableMockHttpClient();
//			mockHttpClient.AddResponderThrowExceptionAllRequests();
//			HttpClientFactory mockHttpClientFactory = new _HttpClientFactory_741(mockHttpClient
//				);
//			Uri remote = GetReplicationURL();
//			manager.SetDefaultHttpClientFactory(mockHttpClientFactory);
//			Replication puller = database.CreatePullReplication(remote);
//			IDictionary<string, object> headers = new Dictionary<string, object>();
//			headers["foo"] = "bar";
//			puller.SetHeaders(headers);
//			puller.Start();
//			Sharpen.Thread.Sleep(2000);
//            puller.Stop();
//            bool foundFooHeader = false;
//			IList<HttpWebRequest> requests = mockHttpClient.GetCapturedRequests();
//			foreach (HttpWebRequest request in requests)
//			{
//				Header[] requestHeaders = request.GetHeaders("foo");
//				foreach (Header requestHeader in requestHeaders)
//				{
//					foundFooHeader = true;
//					NUnit.Framework.Assert.AreEqual("bar", requestHeader.GetValue());
//				}
//			}
//			NUnit.Framework.Assert.IsTrue(foundFooHeader);
//			AssertClientFactory(null);
		}
	}
}
