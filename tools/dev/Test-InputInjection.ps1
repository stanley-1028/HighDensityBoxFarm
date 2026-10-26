# Input-injection capability probe for the automated HDBF verification rounds.
#
# Background: shell commands started through the harness report success for SetCursorPos and
# SendInput yet no window receives the events (not even a WinForms window created by the same
# process). This probe distinguishes "input injection blocked for this process tree" from
# "input injection does not work in this session at all": it creates its own window, injects one
# click and one key, and appends the observed counters plus desktop/session facts to a result file.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev/Test-InputInjection.ps1 -OutFile C:\Temp\probe.txt
#
# The caller compares results from different launch contexts (direct shell, WMI Win32_Process.Create,
# scheduled task). A nonzero "clicks" means injected input reached an application window.

param([string]$OutFile = (Join-Path $env:TEMP 'hdbf-input-probe.txt'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HdbfInputProbe {
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint id);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool GetUserObjectInformation(IntPtr h, int idx, System.Text.StringBuilder sb, int len, out int needed);

    public static string DesktopName() {
        var sb = new System.Text.StringBuilder(256); int n;
        return GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, sb, 512, out n) ? sb.ToString() : "?";
    }
    public static uint ClickAbs(int x, int y) {
        var i = new INPUT[3];
        i[0].type = 0; i[0].mi.dx = x; i[0].mi.dy = y; i[0].mi.dwFlags = 0x0001 | 0x8000;
        i[1].type = 0; i[1].mi.dx = x; i[1].mi.dy = y; i[1].mi.dwFlags = 0x0002;
        i[2].type = 0; i[2].mi.dx = x; i[2].mi.dy = y; i[2].mi.dwFlags = 0x0004;
        return SendInput(3, i, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@

$script:clicks = 0
$script:keys = 0
$form = New-Object System.Windows.Forms.Form
$form.Text = 'hdbf input probe'
$form.Width = 420
$form.Height = 220
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(40, 40)
$form.Add_MouseDown({ param($s, $e) $script:clicks++ })
$form.Add_KeyDown({ param($s, $e) $script:keys++ })
$form.Show()
$form.Activate()
$form.Focus()
Start-Sleep -Milliseconds 700

$point = $form.PointToScreen((New-Object System.Drawing.Point(120, 110)))
[void][HdbfInputProbe]::SetCursorPos($point.X, $point.Y)
Start-Sleep -Milliseconds 300
$sent = [HdbfInputProbe]::ClickAbs($point.X, $point.Y)
Start-Sleep -Milliseconds 400
[HdbfInputProbe]::keybd_event(0x41, 0, 0, [IntPtr]::Zero)
[HdbfInputProbe]::keybd_event(0x41, 0, 2, [IntPtr]::Zero)
Start-Sleep -Milliseconds 500

$lines = @(
    ('time=' + (Get-Date).ToString('s')),
    ('pid=' + $PID),
    ('session=' + (Get-Process -Id $PID).SessionId),
    ('desktop=' + [HdbfInputProbe]::DesktopName()),
    ('elevated=' + (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))),
    ('sendinput_return=' + $sent),
    ('form_clicks=' + $script:clicks),
    ('form_keys=' + $script:keys),
    ('window_station=' + $env:SESSIONNAME)
)
Add-Content -Path $OutFile -Value $lines -Encoding UTF8
$form.Close()
$form.Dispose()
