#!/bin/bash

pushd `dirname $0`
sed "s/$1-b..../$1-$2/g" Couchbase.Lite.Tests.NetCore.csproj > tmp
mv tmp Couchbase.Lite.Tests.NetCore.csproj
popd