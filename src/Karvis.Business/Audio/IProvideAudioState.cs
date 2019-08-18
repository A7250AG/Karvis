using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Karvis.Business.Audio
{
    public interface IProvideAudioState
    {
        ConcurrentDictionary<ulong, bool> IsSpeechPreservedForUser { get; }
        ConcurrentDictionary<ulong, ConcurrentQueue<byte>> SpeechFromUser { get; }
    }
}
