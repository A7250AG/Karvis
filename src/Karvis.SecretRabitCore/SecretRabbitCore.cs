using System;
using Karvis.SecretRabbitCore.Converters;

namespace Karvis.SecretRabbitCore
{
    public enum ConverterType
    {
        BestQuality,
        MediumQuality,
        Fastest,
        ZeroOrderHold,
        Linear
    }

    public struct ConverterData
    {
        public double LastRatio, LastPosition;
        public int Error, Channels;
    }

    public class SecretRabbitCore
    {
        private ConverterData PrivateData;

        public SecretRabbitCore(ConverterType converterType, int channels)
        {
            if (channels < 1)
                throw new BadChannelCountException();

            PrivateData = new ConverterData()
            {
                Channels = channels
            };

            var converter = SetConverterType(converterType);
        }

        private ConverterBase SetConverterType(ConverterType converterType)
        {
            switch (converterType)
            {
                case ConverterType.Linear:
                    return new LinearConverter(PrivateData);
            }

            return null;
        }
    }
}
