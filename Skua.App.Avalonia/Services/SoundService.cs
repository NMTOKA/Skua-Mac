using Skua.Core.Interfaces;
using System;
using System.Diagnostics;

namespace Skua.App.Avalonia.Services;

public class SoundService : ISoundService
{
    public void Beep()
    {
        if (!OperatingSystem.IsWindows())
        {
            PlaySystemSound();
            return;
        }

        Console.Beep();
    }

    public void Beep(int frequency, int duration)
    {
        if (frequency < 37)
            frequency = 37;
        else if (frequency > 32767)
            frequency = 32767;

        // Tone beeps are Windows-only; elsewhere play the system alert sound instead.
        if (!OperatingSystem.IsWindows())
        {
            PlaySystemSound();
            return;
        }

        Console.Beep(frequency, duration);
    }

    private static void PlaySystemSound()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("afplay", "/System/Library/Sounds/Ping.aiff") { UseShellExecute = false })?.Dispose();
            else
                Console.Write('\a');
        }
        catch { /* ignored */ }
    }
}
