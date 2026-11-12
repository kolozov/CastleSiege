using System.Media;

namespace CastleSiege.Audio;

// Звуковые эффекты и фоновая 8-битная MIDI-мелодия
public sealed class SoundManager : IDisposable
{
    private const int SampleRate = 22050;

    private readonly Dictionary<string, SoundPlayer> _effects =
        new Dictionary<string, SoundPlayer>();

    private bool _musicEnabled;
    private bool _disposed;
    private SoundPlayer? _activeEffect;
    private SoundPlayer? _musicLoopPlayer;

    public bool SoundEffectsEnabled { get; private set; } = true;
    public bool MusicAvailable => true;
    public bool MusicEnabled => _musicEnabled;

    public SoundManager()
    {
        var rng = new Random(42);
        _effects["shoot"] = CreatePlayer(
            GenerateTone(new float[] { 220f }, 80f, 0.14f, 0.45f));

        _effects["ricochet"] = CreatePlayer(
            GenerateTone(new float[] { 520f }, 940f, 0.11f, 0.35f));

        _effects["place"] = CreatePlayer(
            GenerateTone(new float[] { 320f }, 480f, 0.055f, 0.28f));

        _effects["remove"] = CreatePlayer(
            GenerateTone(new float[] { 380f }, 210f, 0.055f, 0.24f));

        _effects["click"] = CreatePlayer(
            GenerateTone(new float[] { 540f }, 720f, 0.032f, 0.22f));

        _effects["split"] = CreatePlayer(
            GenerateTone(new float[] { 660f, 880f, 1100f },
                -1f, 0.045f, 0.32f));

        _effects["bonus"] = CreatePlayer(
            GenerateTone(
                new float[] { 523.25f, 659.25f, 783.99f, 1046.5f },
                -1f, 0.065f, 0.35f));

        _effects["debuff"] = CreatePlayer(
            GenerateTone(
                new float[] { 349.23f, 311.13f, 261.63f },
                -1f, 0.08f, 0.35f));

        _effects["error"] = CreatePlayer(
            GenerateTone(new float[] { 190f, 150f }, -1f, 0.07f, 0.4f));

        _effects["victory"] = CreatePlayer(
            GenerateTone(
                new float[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f },
                -1f, 0.10f, 0.4f));

        _effects["explosion"] = CreatePlayer(
            GenerateExplosion(0.28f, 0.45f, rng));

        _effects["explosion_heavy"] = CreatePlayer(
            GenerateExplosion(0.42f, 0.6f, rng));

    }

    public void ToggleMusic()
    {
        _musicEnabled = !_musicEnabled;
        if (_musicEnabled) _musicLoopPlayer?.PlayLooping();
        else _musicLoopPlayer?.Stop();
    }
    public void ToggleSoundEffects()
    {
        SoundEffectsEnabled = !SoundEffectsEnabled;
        if (!SoundEffectsEnabled) StopEffect();
    }

    public void PlayShoot() => PlayEffect("shoot");
    public void PlayExplosion(bool heavy) =>
        PlayEffect(heavy ? "explosion_heavy" : "explosion");
    public void PlayRicochet() => PlayEffect("ricochet");
    public void PlaySplit() => PlayEffect("split");
    public void PlayCrateBonus() => PlayEffect("bonus");
    public void PlayCrateDebuff() => PlayEffect("debuff");
    public void PlayError() => PlayEffect("error");
    public void PlayVictory() => PlayEffect("victory");
    public void PlayPlaceBlock() => PlayEffect("place");
    public void PlayRemoveBlock() => PlayEffect("remove");
    public void PlayUiClick() => PlayEffect("click");

    private void PlayEffect(string key)
    {
        if (!SoundEffectsEnabled || _disposed) return;
        if (!_effects.TryGetValue(key, out var player)) return;

        try
        {
            player.Play();
            _activeEffect = player;
        }
        catch { }
    }

    private void StopEffect()
    {
        if (_activeEffect == null) return;
        try { _activeEffect.Stop(); } catch { }
        _activeEffect = null;
    }

    private static SoundPlayer CreatePlayer(byte[] wavBytes)
    {
        var stream = new MemoryStream(wavBytes);
        var player = new SoundPlayer(stream);
        try
        {
            player.Load();
            return player;
        }
        catch
        {
            player.Dispose();
            stream.Dispose();
            throw;
        }
    }

    private static byte[] GenerateTone(
        float[] freqs,
        float sweepEndFreq,
        float partSec,
        float amp)
    {
        int perPart = Convert.ToInt32(SampleRate * partSec);
        short[] pcm = new short[perPart * freqs.Length];

        for (int n = 0; n < freqs.Length; n++)
        {
            double phase = 0;
            for (int i = 0; i < perPart; i++)
            {
                float t = 1f * i / perPart;
                float freq = sweepEndFreq >= 0f
                    ? freqs[0] + (sweepEndFreq - freqs[0]) * t
                    : freqs[n];

                float env = sweepEndFreq >= 0f
                    ? (1f - t) * (1f - t)
                    : Math.Min(1f, t * 12f) * (1f - t);

                phase += 2.0 * Math.PI * freq / SampleRate;

                float sample = MathF.Sin(Convert.ToSingle(phase)) * env * amp;
                pcm[n * perPart + i] = Convert.ToInt16(
                    Math.Clamp(sample, -1f, 1f) * short.MaxValue);
            }
        }
        return EncodeWav(pcm);
    }

    private static byte[] GenerateExplosion(
        float durationSec,
        float amp,
        Random rng)
    {
        int samples = Convert.ToInt32(SampleRate * durationSec);
        short[] pcm = new short[samples];
        float lowPass = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = 1f * i / samples;
            lowPass = lowPass * 0.72f + (rng.NextSingle() * 2f - 1f) * 0.28f;
            float val = lowPass * MathF.Pow(1f - t, 1.8f) * amp * 1.8f;
            pcm[i] = Convert.ToInt16(
                Math.Clamp(val, -1f, 1f) * short.MaxValue);
        }
        return EncodeWav(pcm);
    }

    private static byte[] EncodeWav(short[] pcm)
    {
        int dataBytes = pcm.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms);
        short one = 1, two = 2, bits = 16;

        bw.Write("RIFF"u8);
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write(one);
        bw.Write(one);
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2);
        bw.Write(two);
        bw.Write(bits);
        bw.Write("data"u8);
        bw.Write(dataBytes);

        foreach (short s in pcm) bw.Write(s);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopEffect();
        foreach (var player in _effects.Values)
        {
            Stream? stream = player.Stream;
            player.Dispose();
            stream?.Dispose();
        }
        _effects.Clear();
    }

}
