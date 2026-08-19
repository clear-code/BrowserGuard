using System;
using System.Runtime.InteropServices;

namespace BrowserGuard
{
    // A warning the user cannot miss. A browser notification is at the mercy of
    // the operating system's notification settings and a page of its own would
    // take up a tab, so the warning is shown by this process instead.
    internal static class Dialog
    {
        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONWARNING = 0x00000030;
        private const uint MB_SETFOREGROUND = 0x00010000;
        private const uint MB_TOPMOST = 0x00040000;

        private const string Caption = "BrowserGuard";

        // Long enough for a sentence or two; a dialog taller than the screen
        // cannot be dismissed.
        private const int MaxTextLength = 500;

        // user32 directly, rather than WinForms MessageBox.Show. That would mean
        // net8.0-windows and UseWindowsForms, which pulls the desktop runtime
        // into the self-contained publish the installer carries and roughly
        // doubles its size. It would also drag BrowserGuard.Tests onto the same
        // target framework, because a net8.0 project cannot reference a
        // net8.0-windows one. WinForms calls this very function underneath, so a
        // dialog with one button and no message loop gains nothing from going
        // through it.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        // Returns once the dialog is dismissed. The browser starts a host of its
        // own for every message, so waiting here holds nothing else up.
        internal static void Show(string text, Logger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                logger?.Log("Dialog: nothing to show");
                return;
            }
            if (text.Length > MaxTextLength)
            {
                text = text[..MaxTextLength];
            }
            try
            {
                MessageBoxW(IntPtr.Zero, text, Caption,
                    MB_OK | MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST);
            }
            catch (Exception ex)
            {
                logger?.Log($"Cannot show the dialog: {ex.Message}");
            }
        }
    }
}
