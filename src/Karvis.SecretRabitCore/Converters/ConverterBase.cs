using System.Collections.Generic;
using System.Text;

namespace Karvis.SecretRabbitCore
{
    public abstract class ConverterBase
    {
        public struct BaseData
        {
            public float[] Input;
            public float[] Output;
            public long InputFrames, OutputFrames;
            public long InputFramesUsed, OutputFramesGenerated;
            public double Ratio;
        }

        protected ConverterBase()
        {
        }

        public virtual void Convert(BaseData baseData)
        {
        }
    }
}
