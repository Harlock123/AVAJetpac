using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAJetpac.Engine;
using AVAJetpac.Views;
using Controls = AVAJetpac.Engine.Controls;

namespace AVAJetpac.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set JETPAC_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avajetpac-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("JETPAC_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAJetpac-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int level, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 22990, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 120 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Carrying_a_section() => Moment(1, "section", g => g.World.Carried is { Kind: ItemKind.Section } && g.World.Aliens.Count >= 2);

    [AvaloniaFact]
    public void Fuel() => Moment(2, "fuel", g => g.World.Carried is { Kind: ItemKind.Fuel } && g.World.Fuel >= 2);

    [AvaloniaFact]
    public void The_laser() => Moment(5, "laser", g => g.World.Shots.Any(s => s.T is > 0.1 and < 0.25) && g.World.Aliens.Count >= 2);

    [AvaloniaFact]
    public void Lift_off() => Moment(3, "lift-off", g => g.World.Launching && g.World.Launch > 120);

    [AvaloniaFact]
    public void A_burst() => Moment(7, "burst", g => g.World.Pops.Any(p => p.T is > 0.08 and < 0.2));

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(4);
        g.SetLives(1);
        g.World.Score = 8460;
        for (int i = 0; i < 60 * 120 && g.State == GameState.Playing; i++) { g.Tick(new Controls(0, false, false)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
