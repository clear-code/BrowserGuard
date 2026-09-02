using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BrowserGuard.NetLogger
{
    // Who recorded an entry and where. Stamped onto every entry, so it is read
    // once: none of it changes while the host runs, and the display name costs
    // a lookup that must not be paid per request.
    //
    // The shapes follow the operation log this product's entries are read
    // beside, so that the two can be lined up on the user and the session.
    internal static class NetLogIdentity
    {
        private const int NameDisplay = 3;
        // USER_INFO_1011: the first field is the full name.
        private const int FullNameLevel = 1011;

        internal static string Host { get; } = Environment.MachineName;

        // DOMAIN\user rather than the bare name, which is what the operation
        // log records: the bare name alone does not say whose it is.
        internal static string Account { get; } =
            $@"{Environment.UserDomainName}\{Environment.UserName}";

        // Empty when it cannot be had, and then the entry leaves it out.
        internal static string DisplayName { get; } = ResolveDisplayName();

        internal static int Session { get; } = ResolveSession();

        private static int ResolveSession()
        {
            try
            {
                using var self = Process.GetCurrentProcess();
                return self.SessionId;
            }
            catch
            {
                return -1;
            }
        }

        // A domain or Entra account answers the first; a local one answers the
        // second. Neither is guaranteed, hence the empty string.
        private static string ResolveDisplayName()
        {
            try
            {
                var name = new StringBuilder(256);
                var size = (uint)name.Capacity;
                if (GetUserNameExW(NameDisplay, name, ref size) && name.Length > 0)
                {
                    return name.ToString();
                }
            }
            catch
            {
            }

            var buffer = IntPtr.Zero;
            try
            {
                if (NetUserGetInfo(null, Environment.UserName, FullNameLevel, out buffer) == 0 &&
                    buffer != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer)) ?? "";
                }
            }
            catch
            {
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
            return "";
        }

        [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserNameExW(int format, StringBuilder name, ref uint size);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserGetInfo(
            string? server, string user, int level, out IntPtr buffer);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);
    }
}
