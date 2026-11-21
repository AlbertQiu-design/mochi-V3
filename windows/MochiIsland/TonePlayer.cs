using System.Runtime.InteropServices;
using System.Text;
using Mochi.Core;

namespace MochiIsland;

internal static class TonePlayer
{
    private const uint Memory = 0x0004;
    private const uint Async = 0x0001;
    private const uint NoDefault = 0x0002;
    private static byte[]? held;
    private static GCHandle heldPin;

    public static void Play(string name, bool cue)
    {
        if (IsMuted()) return;
        var samples = cue ? ToneSynth.RenderCue(name) : ToneSynth.Render(name);
        if (heldPin.IsAllocated) heldPin.Free();
        held = Wav(samples);
        heldPin = GCHandle.Alloc(held, GCHandleType.Pinned);
        PlaySound(held, IntPtr.Zero, Memory | Async | NoDefault);
    }

    public static bool IsMuted()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var found = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            if (found != 0 || device == null) return true;
            var iid = typeof(IAudioEndpointVolume).GUID;
            var activated = device.Activate(ref iid, 1, IntPtr.Zero, out var volume);
            if (activated != 0 || volume == null) return true;
            var read = volume.GetMute(out var mute);
            return read != 0 || mute != 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Wav(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            var clamped = Math.Clamp(samples[index], -1f, 1f);
            var value = (short)(clamped * short.MaxValue);
            pcm[index * 2] = (byte)(value & 0xFF);
            pcm[index * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(44100 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return stream.ToArray();
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(byte[] sound, IntPtr module, uint flags);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.Interface)] out IAudioEndpointVolume volume);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr client);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr client);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute(int mute, IntPtr context);
        [PreserveSig] int GetMute(out int mute);
    }
}
