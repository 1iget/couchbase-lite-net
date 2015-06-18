﻿//
//  PeerToPeerTest.cs
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
using NUnit.Framework;
using Couchbase.Lite.Listener;
using Couchbase.Lite.Util;
using System.Threading;
using Mono.Zeroconf.Providers.Bonjour;

namespace Couchbase.Lite
{
    public class PeerToPeerTest : LiteTestCase
    {
        private const string TAG = "PeerToPeerTest";

        [Test]
        public void TestBrowser()
        {
            #if __ANDROID__
            if(global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.JellyBean) {
                Assert.Inconclusive("PeerToPeer requires API level 16, but found {0}", global::Android.OS.Build.VERSION.Sdk);
            }
            #endif

            //Use a short timeout to speed up the test since it is performed locally
            //Android will get stuck in DNSProcessResult which hangs indefinitely if
            //no results are found (Which will happen if registration is aborted between
            //the resolve reply and query record steps)
            ServiceParams.Timeout = TimeSpan.FromSeconds(3); 
            var mre = new ManualResetEventSlim();
            CouchbaseLiteServiceBrowser browser = new CouchbaseLiteServiceBrowser(new ServiceBrowser());
            browser.ServiceResolved += (sender, e) => {
                Log.D(TAG, "Discovered service: {0}", e.Service.Name);
                if(e.Service.Name == TAG) {
                    mre.Set();
                }
            };

            browser.ServiceRemoved += (o, args) => {
                Log.D(TAG, "Service destroyed: {0}", args.Service.Name);
                if(args.Service.Name == TAG) {
                    mre.Set();
                }
            };
            browser.Start();

            CouchbaseLiteServiceBroadcaster broadcaster = new CouchbaseLiteServiceBroadcaster(new RegisterService(), 59840);
            broadcaster.Name = TAG;
            broadcaster.Start();
            Assert.IsTrue(mre.Wait(TimeSpan.FromSeconds(10)));

            //FIXME.JHB:  Why does Linux hate this part sporadically?
            mre.Reset();
            broadcaster.Dispose();
            var success = mre.Wait(TimeSpan.FromSeconds(10));
            browser.Dispose();
            Assert.IsTrue(success);
        }

    }
}

