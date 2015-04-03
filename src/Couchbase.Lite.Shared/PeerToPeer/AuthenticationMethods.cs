﻿//
//  AuthenticationMethods.cs
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
using System.Net;

using Couchbase.Lite.Auth;
using Couchbase.Lite.Internal;

namespace Couchbase.Lite.PeerToPeer
{
    internal static class AuthenticationMethods
    {
        public static ICouchbaseResponseState RegisterFacebookToken(ICouchbaseListenerContext context)
        {
            var response = new CouchbaseLiteResponse(context);
            var body = context.HttpBodyAs<Dictionary<string, object>>();

            string email = body.GetCast<string>("email");
            string remoteUrl = body.GetCast<string>("remote_url");
            string accessToken = body.GetCast<string>("access_token");
            if (email != null && remoteUrl != null && accessToken != null) {
                Uri siteUrl;
                if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out siteUrl)) {
                    response.InternalStatus = StatusCode.BadParam;
                    response.JsonBody = new Body(new Dictionary<string, object> {
                        { "error", "invalid remote_url" }
                    });
                } else if (!FacebookAuthorizer.RegisterAccessToken(accessToken, email, siteUrl.AbsoluteUri)) {
                    response.InternalStatus = StatusCode.BadParam;
                    response.JsonBody = new Body(new Dictionary<string, object> {
                        { "error", "invalid access_token" }
                    });
                } else {
                    response.JsonBody = new Body(new Dictionary<string, object> {
                        { "ok", "registered" },
                        { "email", email }
                    });
                }
            } else {
                response.InternalStatus = StatusCode.BadParam;
                response.JsonBody = new Body(new Dictionary<string, object> {
                    { "error", "required fields: access_token, email, remote_url" }
                });
            }

            return response.AsDefaultState();
        }

        public static ICouchbaseResponseState RegisterPersonaToken(ICouchbaseListenerContext context)
        {
            var response = new CouchbaseLiteResponse(context);
            var body = context.HttpBodyAs<Dictionary<string, object>>();

            string email = PersonaAuthorizer.RegisterAssertion(body.GetCast<string>("assertion"));
            if (email != null) {
                response.JsonBody = new Body(new Dictionary<string, object> {
                    { "ok", "registered" },
                    { "email", email }
                });
            } else {
                response.InternalStatus = StatusCode.BadParam;
                response.JsonBody = new Body(new Dictionary<string, object> {
                    { "error", "invalid assertion" }
                });
            }

            return response.AsDefaultState();
        }
    }
}

