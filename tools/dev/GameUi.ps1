# In-game UI automation helpers for the HDBF verification rounds.
#
# Dot-source this file from a pwsh/powershell command, then call:
#   $game = Get-HdbfGameProcess
#   Focus-HdbfGame $game
#   Click-HdbfFraction $game 0.5 0.789          # Mods dialog OK (2560x1440 and 1920x1080)
#   Click-HdbfFraction $game 0.952 0.015        # fastest speed ">>>" button
#   Save-HdbfScreenshot "$env:TEMP\shot.png"
#
# The game window is always fullscreen on the primary monitor here, so window-fraction
# coordinates are resolution independent. All calls are cheap; keep the whole interaction in
# one shell command because the DSH window steals the foreground between tool calls.

if (-not ('HdbfGameUi' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HdbfGameUi
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool alt);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(90);
        mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Focus(IntPtr hwnd)
    {
        ShowWindow(hwnd, 9);
        uint fgPid;
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
        uint myThread = GetCurrentThreadId();
        AttachThreadInput(myThread, fgThread, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        SetFocus(hwnd);
        AttachThreadInput(myThread, fgThread, false);
        SwitchToThisWindow(hwnd, true);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
    }

    public static int ClickFraction(IntPtr hwnd, double fx, double fy)
    {
        RECT r;
        GetClientRect(hwnd, out r);
        POINT p = new POINT();
        p.X = (int)((r.Right - r.Left) * fx);
        p.Y = (int)((r.Bottom - r.Top) * fy);
        ClientToScreen(hwnd, ref p);
        ClickAt(p.X, p.Y);
        return p.X * 100000 + p.Y;
    }
}
'@
}

function Get-HdbfGameProcess {
    Get-Process -Name Timberborn -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Focus-HdbfGame {
    param($Game = (Get-HdbfGameProcess))
    if ($null -eq $Game) { throw 'Timberborn is not running' }
    [HdbfGameUi]::Focus($Game.MainWindowHandle)
    Start-Sleep -Milliseconds 500
    return $Game
}

function Click-HdbfFraction {
    param($Game, [double]$X, [double]$Y)
    if ($null -eq $Game) { throw 'Timberborn is not running' }
    $encoded = [HdbfGameUi]::ClickFraction($Game.MainWindowHandle, $X, $Y)
    return $encoded
}

# Press and release a virtual key (e.g. 0x0D Enter, 0x20 Space, 0x1B Esc) in the focused window.
function Send-HdbfKey {
    param([byte]$VirtualKey)
    [HdbfGameUi]::keybd_event($VirtualKey, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [HdbfGameUi]::keybd_event($VirtualKey, 0, 2, [UIntPtr]::Zero)
    return $VirtualKey
}

function Save-HdbfScreenshot {
    param([string]$Path)
    Add-Type -AssemblyName System.Drawing | Out-Null
    Add-Type -AssemblyName System.Windows.Forms | Out-Null
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    return $Path
}
