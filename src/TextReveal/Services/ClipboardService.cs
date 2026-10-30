using System.Runtime.InteropServices;
using System.Windows;

namespace TextReveal.Services;

/// <summary>Clipboard writes that survive a temporarily locked clipboard.</summary>
internal static class ClipboardService
{
    private const int MaxAttempts = 3;

    /// <summary>Copies <paramref name="text"/>; returns <c>false</c> when the clipboard stays busy.</summary>
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                // Another process holds the clipboard open. Retry, then give up quietly:
                // an exception escaping here would take the whole resident application down.
                if (attempt < MaxAttempts - 1)
                {
                    Thread.Sleep(15);
                }
            }
        }

        return false;
    }
}
