using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio;
using NAudio.Wave;
using System.IO;
using static vpxmd.VpxCodecCxPkt;
using System.Reflection;
using NAudio.Mixer;
using System.Threading.Channels;
using NAudio.Wave.SampleProviders;
using NAudio.Utils;
namespace Harmony_0_2
{ 
    internal class AudioServices
    {
        WaveFormat waveFormat = new(44100, 1);
        WaveFormat mixerWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1); // IEEE float
        public Action<byte[], int> AudioInAvailable;
        WaveOut waveOut;
        BufferedWaveProvider bufferStream;
        private MixingSampleProvider mixer;
        private readonly Dictionary<int, BufferedWaveProvider> _channelProviders = new Dictionary<int, BufferedWaveProvider>();
        private readonly object lockObj = new object();

        public void StartCapturing()
        {
            WaveIn waveIn = new WaveIn();
            waveIn.WaveFormat = waveFormat;
            waveIn.DataAvailable += OnDataAvailable;

            void OnDataAvailable(object sender, WaveInEventArgs e)
            {
                byte[] audioBuffer = e.Buffer;

                AudioInAvailable.Invoke(audioBuffer, e.BytesRecorded);
            }

            waveIn.StartRecording();
        }
        public void StartPlaying()
        {
            waveOut = new();
            bufferStream = new(waveFormat);
            mixer = new MixingSampleProvider(mixerWaveFormat);
            
            waveOut.Init(bufferStream);
            //waveOut.Init(mixer);
            waveOut.Play();
        }
        public void PlaySound(byte[] data)
        {
            bufferStream.AddSamples(data, 0, data.Length);
        }
        public void MixSound(int channelId, byte[] data)
        {
            if (!_channelProviders.TryGetValue(channelId, out var provider))
            {
                provider = new BufferedWaveProvider(waveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(500)
                };
                _channelProviders[channelId] = provider;
                mixer.AddMixerInput(provider.ToSampleProvider());
            }
            provider.AddSamples(data, 0, data.Length);
            
        }

        public void RemoveChannel(int channelId)
        {
            lock (lockObj)
            {
                if (_channelProviders.Remove(channelId, out var provider))
                {
                    // Дополнительная очистка при необходимости
                }
            }
        }


    }
}
