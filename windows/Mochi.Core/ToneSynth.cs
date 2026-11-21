namespace Mochi.Core;

public static class ToneSynth
{
    public enum Oscillator
    {
        Sine,
        Triangle
    }

    public readonly record struct Partial(
        Oscillator Wave,
        double Frequency,
        double Start,
        double Duration,
        double Gain,
        double? SlideTo = null);

    public const double SampleRate = 44100;
    public const int CueRepeats = 3;
    public const double CueGap = 0.08;

    public static IReadOnlyList<Partial> Recipe(string name)
    {
        return name switch
        {
            "chirp" =>
            [
                new(Oscillator.Triangle, 988, 0, 0.07, 0.16),
                new(Oscillator.Triangle, 1319, 0.07, 0.09, 0.14)
            ],
            "mew" =>
            [
                new(Oscillator.Sine, 620, 0, 0.18, 0.15, 980),
                new(Oscillator.Sine, 980, 0.12, 0.16, 0.1, 540)
            ],
            "pop" => [new(Oscillator.Sine, 420, 0, 0.09, 0.14, 180)],
            "twinkle" =>
            [
                new(Oscillator.Sine, 1319, 0, 0.16, 0.12),
                new(Oscillator.Sine, 1976, 0.04, 0.18, 0.08)
            ],
            "glass" =>
            [
                new(Oscillator.Sine, 784, 0, 0.14, 0.09, 1175),
                new(Oscillator.Sine, 1568, 0.012, 0.12, 0.035, 2349)
            ],
            "ripple" =>
            [
                new(Oscillator.Sine, 988, 0, 0.06, 0.1),
                new(Oscillator.Sine, 1319, 0.05, 0.08, 0.09),
                new(Oscillator.Sine, 1760, 0.1, 0.12, 0.06)
            ],
            "ping" => [new(Oscillator.Sine, 1480, 0, 0.1, 0.13, 1760)],
            "chime" =>
            [
                new(Oscillator.Sine, 523, 0, 0.15, 0.10),
                new(Oscillator.Sine, 659, 0.10, 0.18, 0.09),
                new(Oscillator.Sine, 262, 0.10, 0.16, 0.035)
            ],
            "bloom" =>
            [
                new(Oscillator.Sine, 784, 0, 0.18, 0.1),
                new(Oscillator.Sine, 1175, 0.04, 0.2, 0.07)
            ],
            "spark" =>
            [
                new(Oscillator.Triangle, 1976, 0, 0.05, 0.09),
                new(Oscillator.Sine, 1568, 0.04, 0.12, 0.1)
            ],
            "drop" =>
            [
                new(Oscillator.Sine, 698, 0, 0.24, 0.08, 440),
                new(Oscillator.Sine, 349, 0.02, 0.22, 0.03, 220)
            ],
            "thud" => [new(Oscillator.Triangle, 240, 0, 0.1, 0.15, 140)],
            "hush" => [new(Oscillator.Sine, 880, 0, 0.2, 0.09, 440)],
            _ => [new(Oscillator.Sine, 880, 0, 0.12, 0.16, 420)]
        };
    }

    public static float[] Render(string name, double sampleRate = SampleRate)
    {
        var notes = Recipe(name);
        var end = notes.Count == 0 ? 0.12 : notes.Max(note => note.Start + note.Duration + 0.02);
        var count = Math.Max(1, (int)(end * sampleRate));
        var buffer = new float[count];
        foreach (var note in notes)
        {
            Mix(note, buffer, sampleRate);
        }
        return buffer;
    }

    public static float[] RenderCue(string name, double sampleRate = SampleRate)
    {
        var once = Render(name, sampleRate);
        var silence = Math.Max(0, (int)(CueGap * sampleRate));
        var buffer = new List<float>(CueRepeats * once.Length + (CueRepeats - 1) * silence);
        for (var index = 0; index < CueRepeats; index++)
        {
            if (index > 0) buffer.AddRange(Enumerable.Repeat(0f, silence));
            buffer.AddRange(once);
        }
        return buffer.ToArray();
    }

    public static float Peak(string name)
    {
        var samples = Render(name);
        var peak = 0f;
        foreach (var sample in samples)
        {
            var abs = Math.Abs(sample);
            if (abs > peak) peak = abs;
        }
        return peak;
    }

    private static void Mix(Partial note, float[] buffer, double sampleRate)
    {
        var phase = 0.0;
        const double attack = 0.012;
        for (var index = 0; index < buffer.Length; index++)
        {
            var time = index / sampleRate;
            var local = time - note.Start;
            if (local < 0 || local > note.Duration + 0.02) continue;
            double frequency;
            if (note.SlideTo is double slide && note.Duration > 0 && note.Frequency > 0 && slide > 0)
            {
                var progress = Math.Min(1, Math.Max(0, local / note.Duration));
                frequency = note.Frequency * Math.Pow(Math.Max(40, slide) / note.Frequency, progress);
            }
            else
            {
                frequency = note.Frequency;
            }
            double envelope;
            if (local < attack)
            {
                envelope = 0.0001 * Math.Pow(note.Gain / 0.0001, local / attack);
            }
            else if (local < note.Duration)
            {
                var span = Math.Max(0.001, note.Duration - attack);
                var progress = Math.Min(1, (local - attack) / span);
                envelope = note.Gain * Math.Pow(0.0001 / Math.Max(note.Gain, 0.0001), progress);
            }
            else
            {
                envelope = 0.0001;
            }
            phase += 2 * Math.PI * frequency / sampleRate;
            double wave;
            if (note.Wave == Oscillator.Sine)
            {
                wave = Math.Sin(phase);
            }
            else
            {
                var wrapped = phase / (2 * Math.PI);
                var fraction = wrapped - Math.Floor(wrapped);
                wave = fraction < 0.5 ? (4 * fraction - 1) : (3 - 4 * fraction);
            }
            var mixed = buffer[index] + (float)(wave * envelope);
            buffer[index] = Math.Min(1, Math.Max(-1, mixed));
        }
    }
}
