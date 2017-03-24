﻿using System;
using System.Collections.Generic;
using System.IO;
using Couchbase.Lite;
using Couchbase.Lite.DB;
using Couchbase.Lite.Logging;
using FluentAssertions;
using Newtonsoft.Json;
using Test.Util;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Test
{
    internal static class Convert
    {
        internal static Document ToConcrete(this IDocument doc)
        {
            return doc as Document;
        }

        internal static Database ToConcrete(this IDatabase db)
        {
            return db as Database;
        }

        internal static Subdocument ToConcrete(this ISubdocument subdoc)
        {
            return subdoc as Subdocument;
        }
    }

    public class TestCase : IDisposable
    {
        public const string DatabaseName = "testdb";
        private readonly ITestOutputHelper _output;

        protected IDatabase Db { get; private set; }

        private static string Directory
        {
            get {
                return Path.Combine(Path.GetTempPath().Replace("cache", "files"), "CouchbaseLite");
            }
        }

#if __NET46__
        static TestCase()
        {
            Couchbase.Lite.Support.Net46.Activate();
        }
#endif

        public TestCase(ITestOutputHelper output)
        {
            Log.AddLogger(new XunitLogger(output));
            _output = output;
            Database.Delete(DatabaseName, Directory);
            OpenDB();
        }

        protected void WriteLine(string line)
        {
            _output.WriteLine(line);
        }

        protected void OpenDB()
        {
            if(Db != null) {
                throw new InvalidOperationException();
            }

            var options = DatabaseOptions.Default;
            options.Directory = Directory;
            options.CheckThreadSafety = true;
            Db = DatabaseFactory.Create(DatabaseName, options);
            Db.Should().NotBeNull("because otherwise the database failed to open");
        }

        protected virtual void ReopenDB()
        {
            Db.Dispose();
            Db = null;
            OpenDB();
        }

        protected virtual void Dispose(bool disposing)
        {
            Db?.Dispose();
            Db = null;
            Log.SetDefaultLogger();
        }

        protected void LoadJSONResource(string resourceName)
        {
            Db.ActionQueue.DispatchSync(() =>
            {
                var ok = Db.InBatch(() =>
                {
                    var n = 0ul;
                    ReadFileByLines($"C/tests/data/{resourceName}.json", line =>
                    {
                        var docID = $"doc-{++n:D3}";
                        var json = JsonConvert.DeserializeObject<IDictionary<string, object>>(line);
                        json.Should().NotBeNull("because otherwise the line failed to parse");
                        var doc = Db.GetDocument(docID);
                        doc.Properties = json;
                        doc.Save();

                        return true;
                    });

                    return true;
                });

                ok.Should().BeTrue("because otherwise the batch insert failed");
            });
        }

        internal bool ReadFileByLines(string path, Func<string, bool> callback)
        {
        #if WINDOWS_UWP
                var url = $"ms-appx:///Assets/{path}";
                var file = Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri(url))
                    .AsTask()
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                var lines = Windows.Storage.FileIO.ReadLinesAsync(file).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
                foreach(var line in lines) {
        #elif __ANDROID__
            var ctx = global::Couchbase.Lite.Tests.Android.MainActivity.ActivityContext;
            using (var tr = new StreamReader(ctx.Assets.Open(path))) {
                string line;
                while ((line = tr.ReadLine()) != null) {
        #else
                using(var tr = new StreamReader(File.Open(path, FileMode.Open))) {
                    string line;
                    while((line = tr.ReadLine()) != null) {
        #endif
                    if (!callback(line)) {
                        return false;
                    }
                }
        #if !WINDOWS_UWP
            }
        #endif

            return true;
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
