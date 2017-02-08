﻿//
//  DocPerfTest.cs
//
//  Author:
//  	Jim Borden  <jim.borden@couchbase.com>
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
using System.IO;
using System.Text;
using Couchbase.Lite;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Test
{
    public sealed class DocPerfTest : PerfTest
    {
        public DocPerfTest(ITestOutputHelper output) : base(output)
        {
            
        }

        [Fact]
        [Trait("slow", "true")]
        public void TestPerformance()
        {
            var options = DatabaseOptions.Default;
            options.Directory = Path.Combine(Path.GetTempPath(), "CouchbaseLite");

            WriteLine("Starting test...");
            SetOptions(options);
            Run();
        }

        protected override void Test()
        {
            const uint revs = 10000;
            WriteLine($"--- Creating {revs} revisions ---");
            Measure(revs, "revision", () => AddRevisions(revs));
        }

        private void AddRevisions(uint count)
        {
            var doc = Db.ActionQueue.DispatchSync(() => Db["doc"]);
            var ok = Db.ActionQueue.DispatchSync(() =>
            {
                return Db.InBatch(() =>
                {
                    for(uint i = 0; i < count; i++) {
                        doc.ActionQueue.DispatchSync(() =>
                        {
                            doc.Set("count", i);
                            doc.Save();
                        });
                    }

                    return true;
                });
            });

            ok.Should().BeTrue("because otherwise the batch operation failed");
        }
    }
}
