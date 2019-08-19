using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using DSharpPlus;
using Grpc.Core.Logging;
using Karvis.Business.Audio;
using Karvis.Business.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using LogLevel = DSharpPlus.LogLevel;

namespace Karvis.Business.Speech
{
    public class AzureSpeechModule
    {
        private readonly SpeechConfig SpeechConfig;
        private readonly DebugLogger DebugLogger;
        private readonly TaskCompletionSource<int> StopRecognition = new TaskCompletionSource<int>();

        private static readonly ConcurrentDictionary<Guid, string> Text = new ConcurrentDictionary<Guid, string>();

        public AzureSpeechModule(KarvisConfiguration configuration, DebugLogger debugLogger)
        {
            DebugLogger = debugLogger;
            SpeechConfig = SpeechConfig.FromSubscription(configuration.AzureSpeechConfiguration.SubscriptionKey, configuration.AzureSpeechConfiguration.Region);
        }

        public async Task<string> AudioToTextAsync(byte[] pcm)
        {
            var guid = Guid.NewGuid();
            if (!Text.ContainsKey(guid))
                Text[guid] = null;

            // Build out the speech recognizer
            using (var pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetDefaultInputFormat()))
            using (var audioInput = AudioConfig.FromStreamInput(pushStream))
            using (var recognizer = new SpeechRecognizer(SpeechConfig, audioInput))
            {
                // Subscribe to speech recognizer events.
                recognizer.SessionStarted += OnSpeechRecognitionSessionStarted;
                recognizer.Recognizing += OnSpeechRecognizing;
                recognizer.Recognized += (s, e) => OnSpeechRecognized(s, e, guid);
                recognizer.Canceled += OnSpeechCanceled;
                recognizer.SessionStopped += OnSpeechRecognitionSessionStopped;

                // Start continuous recognition. Uses StopContinuousRecognitionAsync() to stop recognition.
                await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

                // Send the pcm data to the speech recognizer
                pushStream.Write(pcm);
                pushStream.Close();

                // Wait for completion.
                // Use Task.WaitAny to keep the task rooted.
                Task.WaitAny(StopRecognition.Task);

                // Stop recognition.
                await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);

                return Text[guid];
            }
        }

        public async Task<byte[]> TextToAudioAsync(string text)
        {
            DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                $"AzureSpeechModule: Synthesizing speech for text [{text}]", DateTime.Now);

            // Creates a speech synthesizer using the default speaker as audio output.
            using (var synthesizer = BuildAzureSpeechSynthesizer())
            using (var result = await synthesizer.SpeakTextAsync(text))
            {
                if (SpeechWasSynthesized(result))
                {
                    DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                        $"AzureSpeechModule: Speech synthesized for text [{text}]", DateTime.Now);
                    return AudioConverter.Resample(result.AudioData, 16000, 48000, 1, 2);
                }

                DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                    $"AzureSpeechModule: Speech synthesized failed for text [{text}]", DateTime.Now);

                return new byte[0];
            }
        }

        private SpeechSynthesizer BuildAzureSpeechSynthesizer()
        {
            // Create an audio config to tell Azure Speech SDK to return speech output as a memory stream
            // using its default output format (16kHz, 16bit, mono).
            var audioConfig =
                AudioConfig.FromStreamOutput(
                    AudioOutputStream.CreatePullStream(AudioStreamFormat.GetDefaultOutputFormat()));

            // Create an instance of the Azure Speech SDK speech synthesizer
            return new SpeechSynthesizer(SpeechConfig, audioConfig);
        }

        /// <summary>
        ///     Based on the Azure SDK example, ensure that speech synthesis was successful
        /// </summary>
        /// <param name="result"></param>
        private bool SpeechWasSynthesized(SpeechSynthesisResult result)
        {
            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return true;
            }

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                    $"AzureSpeechModule: CANCELED: Reason={cancellation.Reason}", DateTime.Now);

                if (cancellation.Reason == CancellationReason.Error)
                {
                    DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                        $"AzureSpeechModule: CANCELED: ErrorCode={cancellation.ErrorCode}", DateTime.Now);
                    DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                        $"AzureSpeechModule: CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]", DateTime.Now);
                    DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                        "AzureSpeechModule: CANCELED: Did you update the subscription info?", DateTime.Now);
                }
            }

            return false;
        }

        private void OnSpeechRecognized(object s, SpeechRecognitionEventArgs e, Guid guid)
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech)
            {
                DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                    $"AzureSpeechModule: RECOGNIZED: Text={e.Result.Text}", DateTime.Now);
                Text[guid] = e.Result.Text;
            }
            else if (e.Result.Reason == ResultReason.NoMatch)
            {
                DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                    "AzureSpeechModule: NOMATCH: Speech could not be recognized.", DateTime.Now);
            }
        }

        private void OnSpeechRecognitionSessionStarted(object s, SessionEventArgs e)
        {
            DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                "AzureSpeechModule: Speech recognition session started.", DateTime.Now);
        }

        private void OnSpeechRecognitionSessionStopped(object s, SessionEventArgs e)
        {
            DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                "AzureSpeechModule: Speech recognition session stopped.", DateTime.Now);
            DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, "AzureSpeechModule: Stopping recognition.",
                DateTime.Now);

            if (StopRecognition.TrySetResult(0))
                DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                    "AzureSpeechModule: Stopped recognition session.", DateTime.Now);
            else
                DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                    "AzureSpeechModule: Failed to stop recognition session.", DateTime.Now);
        }

        private void OnSpeechRecognizing(object s, SpeechRecognitionEventArgs e)
        {
            DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                $"AzureSpeechModule: RECOGNIZING: Text={e.Result.Text}", DateTime.Now);
        }

        private void OnSpeechCanceled(object s, SpeechRecognitionCanceledEventArgs e)
        {
            DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                $"AzureSpeechModule: CANCELED: Reason={e.Reason}", DateTime.Now);

            if (e.Reason == CancellationReason.Error)
            {
                DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                    $"AzureSpeechModule: CANCELED: ErrorCode={e.ErrorCode}", DateTime.Now);
                DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                    $"AzureSpeechModule: CANCELED: ErrorDetails={e.ErrorDetails}", DateTime.Now);
                DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                    "AzureSpeechModule: CANCELED: Did you update the subscription info?", DateTime.Now);
            }

            StopRecognition.TrySetResult(0);
        }
    }
}