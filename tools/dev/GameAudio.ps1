# System (default playback device) mute helper for the automated in-game rounds.
#
# The rounds launch Timberborn unattended while the user is away, so the game audio is muted
# before launch and restored afterwards. Uses the documented CoreAudio COM interfaces directly
# (no NuGet dependency, the same "no external tools" rule as the rest of tools/dev).
#
# Dot-source and use:
#   $saved = Get-HdbfAudioState          # @{ Muted = bool; Volume = float }
#   Set-HdbfMute $true                   # mute the default render endpoint
#   Restore-HdbfAudioState $saved        # put back exactly what was there
#
# All calls return $true/$false and never throw for a missing endpoint (a headless session just
# reports "no device"); a failed capture must not abort a game round.

if (-not ('HdbfCoreAudio' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;

public enum HdbfDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
public enum HdbfRole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
public class HdbfMMDeviceEnumeratorComObject { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IHdbfMMDeviceEnumerator
{
    int EnumAudioEndpoints(HdbfDataFlow dataFlow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(HdbfDataFlow dataFlow, HdbfRole role, out IHdbfMMDevice endpoint);
    int GetDevice(string id, out IHdbfMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IHdbfMMDevice
{
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(int stgmAccess, out IntPtr properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IHdbfAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr notify);
    int UnregisterControlChangeNotify(IntPtr notify);
    int GetChannelCount(out int channelCount);
    int SetMasterVolumeLevel(float level, IntPtr ctx);
    int SetMasterVolumeLevelScalar(float level, IntPtr ctx);
    int GetMasterVolumeLevel(out float level);
    int GetMasterVolumeLevelScalar(out float level);
    int SetChannelVolumeLevel(int channel, float level, IntPtr ctx);
    int SetChannelVolumeLevelScalar(int channel, float level, IntPtr ctx);
    int GetChannelVolumeLevel(int channel, out float level);
    int GetChannelVolumeLevelScalar(int channel, out float level);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr ctx);
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

public static class HdbfCoreAudio
{
    private const int ClsCtxAll = 23;

    private static IHdbfAudioEndpointVolume Endpoint()
    {
        var enumerator = (IHdbfMMDeviceEnumerator)(new HdbfMMDeviceEnumeratorComObject());
        IHdbfMMDevice device;
        var hr = enumerator.GetDefaultAudioEndpoint(HdbfDataFlow.eRender, HdbfRole.eMultimedia, out device);
        if (hr != 0 || device == null) return null;
        var iid = typeof(IHdbfAudioEndpointVolume).GUID;
        object raw;
        hr = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out raw);
        if (hr != 0 || raw == null) return null;
        return (IHdbfAudioEndpointVolume)raw;
    }

    public static bool TryGetState(out bool muted, out float volume)
    {
        muted = false; volume = -1f;
        try
        {
            var endpoint = Endpoint();
            if (endpoint == null) return false;
            bool m; var hrMute = endpoint.GetMute(out m);
            float v; var hrVol = endpoint.GetMasterVolumeLevelScalar(out v);
            if (hrMute != 0 || hrVol != 0) return false;
            muted = m; volume = v;
            return true;
        }
        catch { return false; }
    }

    public static bool TrySetMute(bool muted)
    {
        try
        {
            var endpoint = Endpoint();
            if (endpoint == null) return false;
            return endpoint.SetMute(muted, IntPtr.Zero) == 0;
        }
        catch { return false; }
    }

    public static bool TrySetVolume(float scalar)
    {
        try
        {
            var endpoint = Endpoint();
            if (endpoint == null) return false;
            return endpoint.SetMasterVolumeLevelScalar(scalar, IntPtr.Zero) == 0;
        }
        catch { return false; }
    }
}
'@
}

function Get-HdbfAudioState {
    $muted = $false; $volume = -1.0
    $ok = [HdbfCoreAudio]::TryGetState([ref]$muted, [ref]$volume)
    if (-not $ok) { return $null }
    return [pscustomobject]@{ Muted = $muted; Volume = $volume }
}

function Set-HdbfMute {
    param([bool]$Muted)
    return [HdbfCoreAudio]::TrySetMute($Muted)
}

function Restore-HdbfAudioState {
    param($State)
    if ($null -eq $State) { return $false }
    $ok = $true
    if ($State.Volume -ge 0) { $ok = [HdbfCoreAudio]::TrySetVolume([float]$State.Volume) -and $ok }
    $ok = [HdbfCoreAudio]::TrySetMute([bool]$State.Muted) -and $ok
    return $ok
}
