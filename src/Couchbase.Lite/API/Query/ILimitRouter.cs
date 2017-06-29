﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Couchbase.Lite.Query
{
    public interface ILimitRouter
    {
        ILimit Limit(object limit);

        ILimit Limit(object limit, object offset);
    }
}
