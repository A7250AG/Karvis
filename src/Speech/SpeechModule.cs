using System;
using System.IO;
using System.Threading.Tasks;
using DSharpPlus;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;

namespace Karvis.Speech
{
    public class SpeechModule
    {
        private readonly DebugLogger debugLogger;

        public SpeechModule(DebugLogger debugLogger)
        {
            this.debugLogger = debugLogger;
        }

        public async Task<byte[]> SynthesisToStreamAsync(string text)
        {
            debugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, $"Synthesizing speech for text [{text}]", DateTime.Now);

            // Creates a speech synthesizer using the default speaker as audio output.
            using (var synthesizer = BuildAzureSpeechSynthesizer())
            using (var result = await synthesizer.SpeakTextAsync(text))
            {
                if (SpeechWasSynthesized(result))
                {
                    debugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, $"Speech synthesized for text [{text}]", DateTime.Now);
                    return ConvertAudioToSupportedFormat(result.AudioData);
                }
                else
                    debugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, $"Speech synthesized failed for text [{text}]", DateTime.Now);

                return new byte[0];
            }
        }

        private SpeechSynthesizer BuildAzureSpeechSynthesizer()
        {
            // Creates an instance of a speech config with specified subscription key and service region.
            // Replace with your own subscription key and service region (e.g., "westus").
            var config = SpeechConfig.FromSubscription("subscription-key", "region");

            // Create an audio config to tell Azure Speech SDK to return speech output as a memory stream
            // using its default output format (16kHz, 16bit, mono).
            var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream(AudioStreamFormat.GetDefaultOutputFormat()));

            // Create an instance of the Azure Speech SDK speech synthesizer
            return new SpeechSynthesizer(config, audioConfig);
        }

        /// <summary>
        /// Based on the Azure SDK example, ensure that speech synthesis was successful
        /// </summary>
        /// <param name="result"></param>
        private bool SpeechWasSynthesized(SpeechSynthesisResult result)
        {
            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return true;
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

            return false;
        }

        private static byte[] ConvertAudioToSupportedFormat(byte[] audioData)
        {
            // NAudio resampling from Azure Speech default to Opus default
            using (var output = new MemoryStream())
            using (var ms = new MemoryStream(audioData))
            using (var rs = new RawSourceWaveStream(ms, new WaveFormat(16000, 16, 1)))
            using (var resampler = new MediaFoundationResampler(rs, new WaveFormat(48000, 16, 2)))
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

                return output.GetBuffer();
            }
        }
    }
}
