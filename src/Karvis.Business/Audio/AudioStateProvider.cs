using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Karvis.Business.Audio
{
    public class AudioStateProvider : IProvideAudioState
    {
        public ConcurrentDictionary<ulong, bool> IsSpeechPreservedForUser { get; } = new ConcurrentDictionary<ulong, bool>();
        public ConcurrentDictionary<ulong, ConcurrentQueue<byte>> SpeechFromUser { get; } = new ConcurrentDictionary<ulong, ConcurrentQueue<byte>>();
    }
}
