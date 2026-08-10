using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Buddy.Core.Abstractions;

namespace Buddy.Audio.Windows;

internal static class AudioLevelMeter
{
    internal static float GetPeak(
        ReadOnlySpan<byte> bytes,
        int bitsPerSample,
        AudioSampleEncoding encoding)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        return (encoding, bitsPerSample) switch
        {
            (AudioSampleEncoding.IeeeFloat, 32) => GetFloatPeak(bytes),
            (AudioSampleEncoding.Pcm, 16) => GetPcm16Peak(bytes),
            (AudioSampleEncoding.Pcm, 24) => GetPcm24Peak(bytes),
            (AudioSampleEncoding.Pcm, 32) => GetPcm32Peak(bytes),
            _ => 0,
        };
    }

    private static float GetFloatPeak(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(
            bytes[..(bytes.Length - (bytes.Length % sizeof(float)))]);
        float peak = 0;

        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                continue;
            }

            peak = Math.Max(peak, Math.Abs(sample));
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static float GetPcm16Peak(ReadOnlySpan<byte> bytes)
    {
        int completeLength = bytes.Length - (bytes.Length % sizeof(short));
        ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(bytes[..completeLength]);
        int peak = 0;

        foreach (short sample in samples)
        {
            peak = Math.Max(peak, Math.Abs((int)sample));
        }

        return peak / 32_768f;
    }

    private static float GetPcm24Peak(ReadOnlySpan<byte> bytes)
    {
        int peak = 0;
        for (int index = 0; index + 2 < bytes.Length; index += 3)
        {
            int sample = bytes[index]
                | (bytes[index + 1] << 8)
                | (bytes[index + 2] << 16);
            if ((sample & 0x0080_0000) != 0)
            {
                sample |= unchecked((int)0xFF00_0000);
            }

            peak = Math.Max(peak, sample == int.MinValue ? int.MaxValue : Math.Abs(sample));
        }

        return peak / 8_388_608f;
    }

    private static float GetPcm32Peak(ReadOnlySpan<byte> bytes)
    {
        int peak = 0;
        for (int index = 0; index + 3 < bytes.Length; index += sizeof(int))
        {
            int sample = BinaryPrimitives.ReadInt32LittleEndian(bytes[index..]);
            int magnitude = sample == int.MinValue ? int.MaxValue : Math.Abs(sample);
            peak = Math.Max(peak, magnitude);
        }

        return peak / 2_147_483_648f;
    }
}
