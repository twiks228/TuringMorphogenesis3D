using System;
using NAudio.Wave;

using WaveFormat = NAudio.Wave.WaveFormat;
using WaveOutEvent = NAudio.Wave.WaveOutEvent;
using IWaveProvider = NAudio.Wave.IWaveProvider;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>
/// Эмбиент-сонификация без «гласного дрона»:
/// - частота квантуется по пентатонике (звуки, а не сплошной тон);
/// - медленный glide между нотами, лёгкий LFO и feedback-delay дают «пад»,
///   а не формантное «ааа»;
/// - яркость (2-я гармоника) управляется энтропией;
/// - при avg → 0 громкость плавно уходит в ноль (на паузе тишина).
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // Пентатоника (полутона от базовой ноты) — 2 октавы
    private static readonly double[] Semitones = [0, 3, 5, 7, 10, 12, 15, 17, 19, 22, 24];
    private const double BaseFreq = 110.0;
    private const int SampleRate = 44100;

    private sealed class SynthProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = new WaveFormat(SampleRate, 16, 1);

        private double _phase, _phase2, _phaseShim, _lfo, _t;
        private double _freq = BaseFreq, _vol;
        private double _targetFreq = BaseFreq, _targetVol;
        private double _brightness;
        private double _targetBrightness;

        // Feedback delay для «пространства»
        private readonly float[] _delay = new float[SampleRate / 2];
        private int _delayIdx;
        private const float Feedback = 0.32f;
        private const float Wet = 0.28f;

        public void Set(double avg, double entropy)
        {
            // avg → нота пентатоники
            double pos = Math.Clamp(avg / 0.30, 0, 1) * (Semitones.Length - 1);
            int idx = (int)pos;
            double frac = pos - idx;
            double semi = Semitones[idx] + (idx + 1 < Semitones.Length
                ? (Semitones[idx + 1] - Semitones[idx]) * frac : 0);

            _targetFreq = BaseFreq * Math.Pow(2.0, semi / 12.0);
            _targetBrightness = Math.Clamp(entropy, 0, 1) * 0.30;
            _targetVol = Math.Clamp(avg * 4.0, 0, 0.30) * (0.30 + 0.70 * entropy) * 0.5;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int samples = count / 2;

            // Очень медленный glide — звучит как пад, а не как сигнал
            _freq += (_targetFreq - _freq) * 0.0008;
            _vol += (_targetVol - _vol) * 0.004;
            _brightness += (_targetBrightness - _brightness) * 0.002;

            for (int i = 0; i < samples; i++)
            {
                _t += 1.0 / SampleRate;
                _phase += 2 * Math.PI * _freq / SampleRate;
                _phase2 += 2 * Math.PI * _freq * 2.0 / SampleRate;
                _phaseShim += 2 * Math.PI * _freq * 1.005 / SampleRate;
                _lfo = 1.0 + 0.22 * Math.Sin(2 * Math.PI * 0.25 * _t);

                double s = Math.Sin(_phase) * 0.70
                         + Math.Sin(_phaseShim) * 0.14
                         + Math.Sin(_phase2) * _brightness;

                s *= _vol * _lfo;

                // Delay-пространство
                float d = _delay[_delayIdx];
                s += d * Wet;
                _delay[_delayIdx] = (float)s + d * Feedback;
                _delayIdx = (_delayIdx + 1) % _delay.Length;

                short sample = (short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue);
                int o = offset + i * 2;
                buffer[o] = (byte)(sample & 0xFF);
                buffer[o + 1] = (byte)(sample >> 8);
            }
            return count;
        }
    }

    private WaveOutEvent? _out;
    private SynthProvider? _synth;

    public bool Running => _out != null;

    public void Start()
    {
        if (_out != null) return;
        _synth = new SynthProvider();
        _out = new WaveOutEvent();
        _out.Init(_synth);
        _out.Play();
    }

    public void Stop()
    {
        _out?.Stop();
        _out?.Dispose();
        _out = null;
        _synth = null;
    }

    public void Update(double avg, double entropy) => _synth?.Set(avg, entropy);

    /// <summary>Плавное затухание (вызывается при паузе/стопе).</summary>
    public void Mute() => _synth?.Set(0, 0);

    public void Dispose() => Stop();
}