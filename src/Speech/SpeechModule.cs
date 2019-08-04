using DSharpPlus;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Karvis.Speech
{
    public class SpeechModule
    {
        private DebugLogger debugLogger;

        public SpeechModule(DebugLogger debugLogger)
        {
            this.debugLogger = debugLogger;
        }

        public async Task<byte[]> SynthesisToSpeakerAsync(string text)
        {
            debugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, $"Synthesizing speech for text [{text}]", DateTime.Now);

            // Creates an instance of a speech config with specified subscription key and service region.
            // Replace with your own subscription key and service region (e.g., "westus").
            var config = SpeechConfig.FromSubscription("subscription-key", "region");

            var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream(AudioStreamFormat.GetDefaultOutputFormat()));

            // Creates a speech synthesizer using the default speaker as audio output.
            using (var synthesizer = new SpeechSynthesizer(config, audioConfig))
            using (var result = await synthesizer.SpeakTextAsync(text))
            {
                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    debugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, $"Speech synthesized for text [{text}]", DateTime.Now);
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    debugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, $"CANCELED: Reason={cancellation.Reason}", DateTime.Now);

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        debugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, $"CANCELED: ErrorCode={cancellation.ErrorCode}", DateTime.Now);
                        debugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, $"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]", DateTime.Now);
                        debugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, $"CANCELED: Did you update the subscription info?", DateTime.Now);
                    }
                }

                // NAudio resampling from Azure Speech default to Opus default
                using (var output = new MemoryStream())
                using (var ms = new MemoryStream(result.AudioData))
                using (var rs = new RawSourceWaveStream(ms, new WaveFormat(16000, 16, 1)))
                using (var resampler = new MediaFoundationResampler(rs, new WaveFormat(48000, 16, 2)))
                {
                    byte[] bytes = new byte[rs.WaveFormat.AverageBytesPerSecond * 4];
                    while (true)
                    {
                        int bytesRead = resampler.Read(bytes, 0, bytes.Length);
                        if (bytesRead == 0)
                            break;
                        output.Write(bytes, 0, bytesRead);
                    }

                    return output.GetBuffer();
                }
            }
        }
    }
}
