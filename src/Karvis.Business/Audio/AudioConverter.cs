using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NWaves.Audio;
using NWaves.Operations;
using NWaves.Signals;

namespace Karvis.Business.Audio
{
    public static class AudioConverter
    {
        public static byte[] Resample(byte[] input, int inputSampleRate, int outputSampleRate, int inputChannels, int outputChannels, int inputBitDepth = 16, int outputBitDepth = 16)
        {
            if ((inputChannels != 1 && inputChannels != 2) || (outputChannels != 1 && outputChannels != 2))
                return input;

            var buff = ResampleInternal(input, inputSampleRate, outputSampleRate);

            if (inputChannels == 1)
            {
                if (outputChannels == 2)
                {
                    buff = MonoToStereo(buff);
                }
            }
            else if (inputChannels == 2)
            {
                if (outputChannels == 1)
                    buff = StereoToMono(buff);
            }

            return buff;
        }

        private static byte[] MonoToStereo(byte[] input)
        {
            // thanks https://www.codeproject.com/Articles/501521/How-to-convert-between-most-audio-formats-in-NET
            byte[] output = new byte[input.Length * 2];
            int outputIndex = 0;
            for (int n = 0; n < input.Length; n += 2)
            {
                // copy in the first 16 bit sample
                output[outputIndex++] = input[n];
                output[outputIndex++] = input[n + 1];
                // now copy it in again
                output[outputIndex++] = input[n];
                output[outputIndex++] = input[n + 1];
            }
            return output;
        }

        private static byte[] StereoToMono(byte[] input)
        {
            // thanks https://www.codeproject.com/Articles/501521/How-to-convert-between-most-audio-formats-in-NET
            byte[] output = new byte[input.Length / 2];
            int outputIndex = 0;
            for (int n = 0; n < input.Length; n += 4)
            {
                int leftChannel = BitConverter.ToInt16(input, n);
                int rightChannel = BitConverter.ToInt16(input, n + 2);
                int mixed = (leftChannel + rightChannel) / 2;
                byte[] outSample = BitConverter.GetBytes((short)mixed);

                // copy in the first 16 bit sample
                output[outputIndex++] = outSample[0];
                output[outputIndex++] = outSample[1];
            }
            return output;
        }

        private static byte[] ResampleInternal(byte[] buff, int inputSampleRate, int outputSampleRate)
        {
            var floated = buff.AsSpan().Reinterpret().ToArray().Select(b => (float)b);

            var original = new DiscreteSignal(inputSampleRate, floated);

            var resampled = new Resampler().Resample(original, outputSampleRate);

            var max = Math.Abs(resampled.Samples.Max());
            var min = Math.Abs(resampled.Samples.Min());

            if (min > max)
                max = min;

            resampled.Attenuate(max / Int16.MaxValue);

            var defloated = resampled.Samples
                .Select(Convert.ToInt16).ToArray().AsSpan()
                .Reinterpret()
                .ToArray();

            return defloated;
        }
    }
}
