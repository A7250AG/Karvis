using System;

namespace Karvis.SecretRabbitCore.Converters
{
    public sealed class LinearConverter : ConverterBase
    {

        private int MagicMarker;
        private int Channels;
        private bool Reset;
        private long InCount, InUsed;
        private long OutCount, OutGenerated;
        private float[] LastValue;

        private double Ratio;

        private ConverterData ConverterData;

        public LinearConverter(ConverterData privateData)
        {
            if (privateData.Channels < 1)
                throw new BadChannelCountException();

            MagicMarker = default;
            ConverterData = privateData;

            Channels = privateData.Channels;
        }

        public void Convert(BaseData baseData)
        {
            if (baseData.InputFrames <= 0)
                return;

            if (Reset)
            {
                for (int channel = 0; channel < Channels; channel++)
                    LastValue[channel] = baseData.Input[channel];
                Reset = false;
            }

            InCount = baseData.InputFrames * Channels;
            OutCount = baseData.OutputFrames * Channels;
            InUsed = OutGenerated = 0;

            Ratio = baseData.Ratio;

            if (IsBadRatio(Ratio))
                throw new BadInternalStateException();

            double inputIndex = ConverterData.LastPosition;

            /* Calculate samples before first sample in input array. */
            while (inputIndex < 1.0 && OutGenerated < OutCount)
            {
                if (InUsed + Channels * (1.0 + inputIndex) >= InCount)
                    break;

                if (OutCount > 0 && Math.Abs(ConverterData.LastRatio - baseData.Ratio) > Constants.MinimumRatioDiff)
                    Ratio = ConverterData.LastRatio + OutGenerated * (Ratio - ConverterData.LastRatio) / OutCount;

                for (int channel = 0; channel < Channels; channel++)
                {
                    baseData.Output[OutGenerated] =
                        (float) (LastValue[channel] + (inputIndex * (baseData.Input[channel] - LastValue[channel])));
                    OutGenerated++;
                }

                /* Figure out the next index. */
                inputIndex += 1.0 / Ratio;
            }

            double rem = FmodOne(inputIndex);
            InUsed += Channels * (long) Math.Round(inputIndex - rem);
            inputIndex = rem;

            /* Main processing loop. */
            while (OutGenerated < OutCount && InUsed + Channels * inputIndex < InCount)
            {
                if (OutCount > 0 && Math.Abs(ConverterData.LastRatio - Ratio) > Constants.MinimumRatioDiff)
                    Ratio = ConverterData.LastRatio + OutGenerated * (Ratio - ConverterData.LastRatio) / OutCount;

                /*
                 * if (SRC_DEBUG && priv->in_used < priv->channels && input_index < 1.0)
		        {	printf ("Whoops!!!!   in_used : %ld     channels : %d     input_index : %f\n", priv->in_used, priv->channels, input_index) ;
			        exit (1) ;
			        } ;
                 */

                for (int channel = 0; channel < Channels; channel++)
                {
                    baseData.Output[OutGenerated] = 
                        (float) (baseData.Input[InUsed - Channels + channel] + inputIndex *
                                 (baseData.Input[InUsed + channel] - baseData.Input[InUsed - Channels + channel]));
                    OutGenerated++;
                }

                /* Figure out the next index. */
                inputIndex += 1.0 / Ratio;
                rem = FmodOne(inputIndex);
                InUsed += Channels * (long)Math.Round(inputIndex - rem);
                inputIndex = rem;
            }

            if (InUsed > 0)
                for (int channel = 0; channel < Channels; channel++)
                    LastValue[channel] = baseData.Input[InUsed - Channels + channel];

            ConverterData.LastRatio = Ratio;
            baseData.InputFramesUsed = InUsed / Channels;
            baseData.OutputFramesGenerated = OutGenerated / Channels;
        }

        public static double FmodOne(double x)
        {
            var res = x - (long) Math.Round(x);
            if (res < 0.0)
            {
                return res + 1.0;
            }

            return res;
        }

        public static bool IsBadRatio(double ratio)
        {
            return (ratio < (1.0 / Constants.MaxRatio) || ratio > (1.0 * Constants.MaxRatio));
        }
    }
}