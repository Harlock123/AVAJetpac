using AVAJetpac.Engine;

namespace AVAJetpac.Tests;

/// <summary>How the computer Jetman gets on, level by level (set JETPAC_PROBE to run it; JETPAC_PROBE=lazy flies about firing).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_Jetman_does()
    {
        if (Environment.GetEnvironmentVariable("JETPAC_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = new World();
        Sim.StartLevel(w, 1);
        var p = new Pilot();
        int deaths = 0, t = 0;
        var causes = new Dictionary<string, int>();
        double levelStart = 0;
        for (; t < 60 * 60 * 10 && w.Level <= 16 && deaths < 30; t++)
        {
            var c = mode == "lazy" ? new Controls(t / 150 % 2 == 0 ? 1 : -1, t % 70 < 30, t % 8 == 0) : p.Decide(w);
            Sim.Step(w, c);
            if (w.Man.Dead) { deaths++; causes[w.Cause] = causes.GetValueOrDefault(w.Cause) + 1; Sim.Respawn(w); p.Reset(); }
            if (w.Gone)
            {
                output.WriteLine($"level {w.Level} ({w.Kind}) in {(t - levelStart) / 60:0}s, deaths so far {deaths}, score {w.Score}");
                levelStart = t;
                Sim.StartLevel(w, w.Level + 1);
                p.Reset();
            }
        }
        output.WriteLine($"reached level {w.Level} in {t / 60}s with {deaths} deaths ({string.Join(", ", causes.Select(k => $"{k.Value} {k.Key}"))}); built {w.Built}, fuel {w.Fuel}; {sw.ElapsedMilliseconds} ms");
    }
}
