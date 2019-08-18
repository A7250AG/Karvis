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
                    var buffer = buff.AsSpan().Reinterpret().ToArray().Select(b => (float) b);

                    DiscreteSignal signal = new DiscreteSignal(inputSampleRate, buffer);

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
                    var mono = new byte[buff.Length/2]; // convert to mono: take 2 bytes (16bit audio), skip 2 bytes
                    for (int b = 0, i = 0; b < buff.Length; b += 4, i+=2)
                    {
                        mono[i] = buff[b];
                        mono[i + 1] = buff[b + 1];
                    }

                    var buffer = mono.AsSpan().Reinterpret().ToArray().Select(b => (int) b);

                    DiscreteSignal signal = new DiscreteSignal(inputSampleRate, buffer);

                    var resampled = resampler.Resample(signal, outputSampleRate);

                    var max = Math.Abs(resampled.Samples.Max());
                    var min = Math.Abs(resampled.Samples.Min());

                    if (min > max)
                        max = min;

                    resampled.Attenuate(max/Int16.MaxValue);

                    var output = resampled.Samples
                        .Select(Convert.ToInt16).ToArray().AsSpan()
                        .Reinterpret()
                        .ToArray();

                    return output;
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
