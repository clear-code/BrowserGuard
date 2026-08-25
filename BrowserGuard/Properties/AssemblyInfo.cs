using System.Runtime.Versioning;

// The registry, the message box and the browser this host serves are all
// Windows only. Saying so here is what keeps CA1416 from flagging each call.
[assembly: SupportedOSPlatform("windows")]
