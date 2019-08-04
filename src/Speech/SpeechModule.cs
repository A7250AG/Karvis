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

            //discord.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, $"Speech synthesizing to speaker for text [{text}]", DateTime.Now);

            // Creates an instance of a speech config with specified subscription key and service region.
            // Replace with your own subscription key and service region (e.g., "westus").
            var config = SpeechConfig.FromSubscription("subscription-key", "region");

            var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream(AudioStreamFormat.GetDefaultOutputFormat()));
            //var audioConfig = AudioConfig.FromDefaultSpeakerOutput();

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

                // Endian swap
                //for (int i = 0; i < result.AudioData.Length; i = i + 2)
                //{
                //    byte one = result.AudioData[i];
                //    byte two = result.AudioData[i + 1];
                //    result.AudioData[i] = two;
                //    result.AudioData[i + 1] = one;
                //}

                //return result.AudioData;

                // Fake stereo
                //List<byte> output = new List<byte>();
                //for (int i = 0; i < result.AudioData.Length; i = i + 2)
                //{
                //    byte one = result.AudioData[i];
                //    byte two = result.AudioData[i + 1];
                //    output.Add(one);
                //    output.Add(two);
                //    output.Add(one);
                //    output.Add(two);
                //}

                //return output.ToArray();

                // Raw Azure
                //return result.AudioData;

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

                // ffmpeg
                //sample size shouldn't really matter as long as it's not too low and the format is set properly
                //can you try encoding your audio into 48khz stereo with a frame size of 960
                //that's 1920 bytes per frame
                //that should work flawlessly
                // ffmpeg -ac 1 -f s16le -ar 16000 -i pipe:0 -ac 2 -ar 48000 -f s16le pipe:1
                //                    Input #0, s16le, from 'pipe:0':
                //  Duration: N / A, bitrate: 256 kb / s
                //    Stream #0:0: Audio: pcm_s16le, 16000 Hz, mono, s16, 256 kb/s
                //Stream mapping:
                //                    Stream #0:0 -> #0:0 (pcm_s16le (native) -> pcm_s16le (native))
                //Output #0, s16le, to 'pipe:1':
                //  Metadata:
                //                encoder: Lavf57.72.101
                //    Stream #0:0: Audio: pcm_s16le, 48000 Hz, stereo, s16, 1536 kb/s
                //    Metadata:
                //      encoder: Lavc57.96.101 pcm_s16le

                //var psi = new ProcessStartInfo
                //{
                //    FileName = "ffmpeg",
                //    Arguments = "-ac 1 -f s16le -ar 16000 -i pipe:0 -ac 2 -ar 48000 -f s16le pipe:1",
                //    RedirectStandardOutput = true,
                //    RedirectStandardInput = true,
                //    UseShellExecute = false
                //};

                //var ffmpeg = Process.Start(psi);
                //var ffin = ffmpeg.StandardInput.BaseStream;
                //var ffout = ffmpeg.StandardOutput.BaseStream;

                //// My Version
                ////ffin.Write(result.AudioData);
                ////ffmpeg.StandardInput.Close();

                ////var output = new List<byte>();

                ////var buff = new byte[1920];
                ////var br = 0;
                ////var diff_start = DateTime.Now;
                ////DateTime diff_end = DateTime.Now;
                ////while ((br = ffout.Read(buff, 0, buff.Length)) > 0)
                ////{
                ////    if (br < buff.Length) // not a full sample, mute the rest
                ////        for (var i = br; i < buff.Length; i++)
                ////            buff[i] = 0;

                ////    output.AddRange(buff);
                ////}

                ////return output.ToArray();

                //// Maxine version
                //var outStream = new MemoryStream();

                //var inputTask = Task.Run(() =>
                //{
                //    ffin.Write(result.AudioData);
                //    ffmpeg.StandardInput.Close();
                //});

                //var outputTask = Task.Run(() =>
                //{
                //    ffout.CopyTo(outStream);
                //});

                //Task.WaitAll(inputTask, outputTask);

                //ffmpeg.WaitForExit();

                //return outStream.ToArray();
            }
            
        }
    }
}
