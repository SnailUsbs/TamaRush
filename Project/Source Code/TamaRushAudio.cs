using TamaRush.TMEmulator;
using UnityEngine;

namespace TamaRush
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class TamaRushAudio : MonoBehaviour
    {
        private AudioSource _audioSource;
        private TamaEmulator _emu;
        private double _phase;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.playOnAwake = true;
            _audioSource.loop = true;
            _audioSource.volume = 1f;
            _audioSource.pitch = 1f;
            _audioSource.spatialBlend = 0f;
            _audioSource.bypassEffects = true;
            _audioSource.bypassListenerEffects = true;
            _audioSource.bypassReverbZones = true;
            _audioSource.outputAudioMixerGroup = null;

            if (!_audioSource.isPlaying)
                _audioSource.Play();
        }

        public void SetEmulator(TamaEmulator emu)
        {
            _emu = emu;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_emu == null || !_emu.BuzzerEnabled || !(TamaRushPlugin.AudioEnabled?.Value ?? true))
            {
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                _phase = 0;
                return;
            }

            float freq = _emu.BuzzerFreqHz;
            if (freq <= 0f)
            {
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                return;
            }

            int sampleRate = AudioSettings.outputSampleRate;
            double phaseStep = freq / sampleRate;
            int frames = data.Length / channels;
            float amplitude = Mathf.Clamp01((TamaRushPlugin.AudioVolume?.Value ?? 5) / 10f) * 0.5f;

            for (int f = 0; f < frames; f++)
            {
                float sample = _phase < 0.5 ? amplitude : -amplitude;
                for (int c = 0; c < channels; c++)
                    data[f * channels + c] = sample;
                _phase += phaseStep;
                if (_phase >= 1.0) _phase -= 1.0;
            }
        }
    }
}
