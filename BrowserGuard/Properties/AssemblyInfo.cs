/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using System.Runtime.Versioning;

// The registry, the message box and the browser this host serves are all
// Windows only. Saying so here is what keeps CA1416 from flagging each call.
[assembly: SupportedOSPlatform("windows")]
