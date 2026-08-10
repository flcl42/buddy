using System.Buffers;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

internal sealed class MonoMixingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;

    internal MonoMixingSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sourceChannels = source.WaveFormat.Channels;
        if (_sourceChannels < 1)
        {
            throw new ArgumentException("The source must expose at least one channel.", nameof(source));
        }

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            source.WaveFormat.SampleRate,
            1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The requested range is outside the output buffer.");
        }

        if (_sourceChannels == 1)
        {
            return _source.Read(buffer, offset, count);
        }

        int sourceCount = checked(count * _sourceChannels);
        float[] rented = ArrayPool<float>.Shared.Rent(sourceCount);
        try
        {
            int samplesRead = _source.Read(rented, 0, sourceCount);
            int framesRead = samplesRead / _sourceChannels;
            for (int frame = 0; frame < framesRead; frame++)
            {
                float sum = 0;
                int frameOffset = frame * _sourceChannels;
                for (int channel = 0; channel < _sourceChannels; channel++)
                {
                    sum += rented[frameOffset + channel];
                }

                buffer[offset + frame] = Math.Clamp(sum / _sourceChannels, -1, 1);
            }

            return framesRead;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }
}
