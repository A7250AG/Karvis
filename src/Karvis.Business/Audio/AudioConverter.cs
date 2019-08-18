using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NWaves.Audio;
using NWaves.Operations;
using NWaves.Signals;
using WaveFormat = NAudio.Wave.WaveFormat;

namespace Karvis.Business.Audio
{
    public static class AudioConverter
    {
        public static byte[] ResampleWindowsOnly(byte[] buff, int inputSampleRate, int outputSampleRate, int inputChannels,  int outputChannels, int inputBitDepth = 16, int outputBitDepth = 16)
        {
            byte[] resampled;
            // NAudio resampling from Azure Speech default to Opus default 
            using (var output = new MemoryStream())
            using (var ms = new MemoryStream(buff))
            using (var rs = new RawSourceWaveStream(ms, new WaveFormat(inputSampleRate, inputBitDepth, inputChannels)))
            using (var resampler = new MediaFoundationResampler(rs, new WaveFormat(outputSampleRate, outputBitDepth, outputChannels)))
            {
                // thanks https://csharp.hotexamples.com/examples/NAudio.Wave/MediaFoundationResampler/Read/php-mediafoundationresampler-read-method-examples.html#0xe8c3188aa82ab5c60c681c14b7336b52f1b3546fd75d133baef6572074b6028c-125,,155,
                byte[] bytes = new byte[rs.WaveFormat.AverageBytesPerSecond * 4];
                while (true)
                {
                    int bytesRead = resampler.Read(bytes, 0, bytes.Length);
                    if (bytesRead == 0)
                        break;
                    output.Write(bytes, 0, bytesRead);
                }

                resampled = output.GetBuffer();
            }

            return resampled;
        }

        public static byte[] Resample(byte[] buff, int inputSampleRate, int outputSampleRate, int inputChannels, int outputChannels, int inputBitDepth = 16, int outputBitDepth = 16)
        {
            var resampler = new Resampler();

            if (inputChannels == 1)
            {
                if (outputChannels == 1)
                {
                    DiscreteSignal signal;

                    using (var stream = new MemoryStream(buff))
                    {
                        var waveFile = new WaveFile(stream);
                        if (waveFile.WaveFmt.ChannelCount != 1)
                            throw new InvalidOperationException("Input channel counts do not match.");

                        signal = waveFile.Signals.FirstOrDefault();
                    }

                    return resampler.Resample(signal, outputSampleRate).Samples
                        .SelectMany(BitConverter.GetBytes)
                        .ToArray();
                }
                else if (outputChannels == 2)
                {
                    throw new NotImplementedException();
                }
            }
            else if (inputChannels == 2)
            {
                if (outputChannels == 1)
                {
                    DiscreteSignal signal;

                    using (var stream = new MemoryStream(buff))
                    {
                        var waveFile = new WaveFile(stream);
                        if (waveFile.WaveFmt.ChannelCount != 2)
                            throw new InvalidOperationException("Input channel counts do not match.");

                        signal = waveFile[Channels.Average];
                    }

                    return resampler.Resample(signal, outputSampleRate).Samples
                        .SelectMany(BitConverter.GetBytes)
                        .ToArray();
                }
                else if (outputChannels == 2)
                {
                    throw new NotImplementedException();
                }
            }

            return buff;
        }
    }
}
