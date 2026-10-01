/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 ClearCode Inc.
*/

using System.Runtime.Versioning;

// Matches the assembly under test, which is Windows only. Without it every
// call into that assembly draws CA1416.
[assembly: SupportedOSPlatform("windows")]
