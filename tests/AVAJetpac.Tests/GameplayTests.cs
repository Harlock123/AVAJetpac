using Avalonia.Input;
using AVAJetpac.Engine;

namespace AVAJetpac.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avajetpac-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A level with no aliens coming (for a while).</summary>
    private static World Quiet(int level = 1)
    {
        var w = new World();
        Sim.StartLevel(w, level);
        w.NextAlien = 1e9;
        w.NextDrop = 1e9;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    // ───────────────────────────── the rocket ─────────────────────────────

    [Fact]
    public void Every_fourth_level_starts_with_the_rocket_in_pieces_the_others_with_it_built()
    {
        var w = Quiet(1);
        Assert.Equal(1, w.Built);
        Assert.Equal(2, w.Items.Count(i => i.Kind == ItemKind.Section));
        Sim.StartLevel(w, 2);
        Assert.Equal(3, w.Built);
        Assert.Empty(w.Items);
        Sim.StartLevel(w, 5);
        Assert.Equal(1, w.Built);
    }

    [Fact]
    public void Sections_go_on_in_order_carried_over_the_pad()
    {
        var w = Quiet(1);
        var top = w.Items.First(i => i.Section == 3);
        // The top first won't come: the middle's needed.
        w.Man.X = top.X;
        w.Man.Y = top.Y;
        Run(w, 2);
        Assert.Null(w.Carried);
        var middle = w.Items.First(i => i.Section == 2);
        w.Man.X = middle.X;
        w.Man.Y = middle.Y;
        Run(w, 2);
        Assert.Same(middle, w.Carried);
        w.Man.X = Field.RocketX;
        w.Man.Y = 400;
        Run(w, 2);
        Assert.Null(w.Carried);
        Run(w, 300, new Controls(0, true));
        Assert.Equal(2, w.Built);
        Assert.Equal(Sim.PartPoints, w.Score);
    }

    [Fact]
    public void Fuel_pods_fall_once_the_rocket_is_built_and_six_fill_it()
    {
        var w = Quiet(2);
        w.NextDrop = 0;
        Run(w, 2);
        var pod = Assert.Single(w.Items, i => i.Kind == ItemKind.Fuel);
        Run(w, 600);
        Assert.True(pod.Landed);
        for (int k = 0; k < Sim.FuelPods; k++)
        {
            w.Items.Clear();
            w.Items.Add(new Item { Kind = ItemKind.Fuel, X = Field.RocketX, Y = 300, Dropping = true });
            Run(w, 300);
        }
        Assert.Equal(Sim.FuelPods, w.Fuel);
        Assert.True(w.Ready);
    }

    [Fact]
    public void Aboard_a_ready_rocket_it_takes_off_and_the_level_is_done()
    {
        var w = Quiet(2);
        w.Fuel = Sim.FuelPods;
        w.Man.X = Field.RocketX;
        Run(w, 2);
        Assert.True(w.Launching);
        Run(w, 60 * 5);
        Assert.True(w.Gone);
    }

    // ───────────────────────────── Jetman ─────────────────────────────

    [Fact]
    public void Jetman_walks_falls_and_thrusts_up_and_wraps_round_the_screen()
    {
        var w = Quiet(2);
        Run(w, 10);
        Assert.True(w.Man.Grounded);
        double y = w.Man.Y;
        Run(w, 30, new Controls(0, true));
        Assert.True(w.Man.Y < y - 30);
        Run(w, 180);
        Assert.Equal(Field.Ground, w.Man.Y);
        w.Man.X = 10;
        Run(w, 20, new Controls(-1));
        Assert.True(w.Man.X > Field.Width - 60);
    }

    [Fact]
    public void A_ledge_holds_Jetman_up_and_bumps_his_head_from_below()
    {
        var (l, r, ly) = Field.Ledges[0];
        var w = Quiet(2);
        w.Man.X = (l + r) / 2;
        w.Man.Y = ly - 40;
        Run(w, 60);
        Assert.Equal(ly, w.Man.Y);
        Assert.True(w.Man.Grounded);
        var v = Quiet(2);
        v.Man.X = (l + r) / 2;
        v.Man.Y = ly + 120;
        Run(v, 60, new Controls(0, true));
        Assert.True(v.Man.Y - Jetman.Height >= ly + Field.LedgeDepth - 0.5);
    }

    // ───────────────────────────── aliens ─────────────────────────────

    [Fact]
    public void The_laser_kills_an_alien_for_points_and_an_alien_kills_Jetman()
    {
        var w = Quiet(2);
        w.Aliens.Add(new Alien { Kind = AlienKind.Fuzz, X = w.Man.X + 200, Y = w.Man.Y - 30, Speed = 100 });
        Sim.Step(w, new Controls(1, false, true));
        Run(w, 20);
        Assert.Empty(w.Aliens);
        Assert.Equal(Sim.AlienPoints(AlienKind.Fuzz), w.Score);

        var v = Quiet(2);
        v.Aliens.Add(new Alien { Kind = AlienKind.Fuzz, X = v.Man.X + 60, Y = v.Man.Y - 24, Vx = -120 });
        Run(v, 60);
        Assert.True(v.Man.Dead);
    }

    [Fact]
    public void Meteors_burst_on_the_ground_and_homers_come_after_Jetman()
    {
        var w = Quiet(1);
        w.Aliens.Add(new Alien { Kind = AlienKind.Meteor, X = 900, Y = 650, Vx = -50, Vy = 200, Speed = 200 });
        Run(w, 20);
        Assert.Empty(w.Aliens);
        Assert.NotEmpty(w.Pops);

        var v = Quiet(4);
        v.Aliens.Add(new Alien { Kind = AlienKind.Jet, X = 200, Y = 200, Vx = 0, Vy = -150, Speed = 150 });
        double d0 = Math.Abs(Sim.Across(200, v.Man.X)) + Math.Abs(200 - v.Man.Y);
        Run(v, 120);
        var a = v.Aliens.FirstOrDefault();
        Assert.True(a == null || Math.Abs(Sim.Across(a.X, v.Man.X)) + Math.Abs(a.Y - v.Man.Y) < d0);
    }

    [Fact]
    public void Gems_are_worth_picking_up()
    {
        var w = Quiet(2);
        w.Items.Add(new Item { Kind = ItemKind.Gem, X = w.Man.X, Y = w.Man.Y, Landed = true });
        Run(w, 2);
        Assert.Empty(w.Items);
        Assert.Equal(Sim.GemPoints, w.Score);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_life_lost_drops_what_he_carried_and_brings_him_back_by_the_rocket()
    {
        var g = Engine();
        g.Go(1);
        var w = g.World;
        var middle = w.Items.First(i => i.Section == 2);
        middle.Carried = true;
        middle.Landed = false;
        // Lost well away from where he comes back.
        w.Man.X = 300;
        w.Man.Y = Field.Ground;
        Sim.Step(w, default);
        w.Man.Dead = true;
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.False(middle.Carried);
        Assert.False(g.World.Man.Dead);
    }

    [Fact]
    public void Lives_run_out_and_the_game_is_over()
    {
        var g = Engine();
        g.Go(1);
        for (int i = 0; i < GameEngine.Lives0; i++)
        {
            g.World.Man.Dead = true;
            g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        }
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_playing_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 5) && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_everything()
    {
        var g = Engine();
        g.Go();
        g.Run(30);
        double t = g.World.Clock;
        g.Pause();
        g.Run(200);
        Assert.Equal(t, g.World.Clock);
        g.Resume();
        g.Run(10);
        Assert.True(g.World.Clock > t);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = Quiet(2);
        var c = w.Copy();
        Run(c, 30, new Controls(0, true));
        Assert.Equal(Field.Ground, w.Man.Y);
        Assert.True(c.Man.Y < Field.Ground);
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Up);
        Assert.True(input.Player.Thrust);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.Space);
        Assert.Equal(default, input.Player);
        input.Dispose();

        var g = Engine();
        g.Go();
        double x = g.World.Man.X;
        g.Run(20, new Controls(1));
        Assert.True(g.World.Man.X > x + 30);
    }

    // ───────────────────────────── the computer Jetman ─────────────────────────────

    [Fact]
    public void The_computer_gets_through_the_first_four_planets_and_flying_about_does_not_build_the_rocket()
    {
        var w = new World();
        Sim.StartLevel(w, 1);
        var p = new Pilot();
        int deaths = 0;
        for (int t = 0; t < 60 * 60 * 5 && w.Level <= 4 && deaths < 3; t++)
        {
            Sim.Step(w, p.Decide(w));
            if (w.Man.Dead) { deaths++; Sim.Respawn(w); p.Reset(); }
            if (w.Gone) { Sim.StartLevel(w, w.Level + 1); p.Reset(); }
        }
        Assert.Equal(5, w.Level);

        var lazy = new World();
        Sim.StartLevel(lazy, 1);
        for (int t = 0; t < 60 * 120; t++)
        {
            Sim.Step(lazy, new Controls(t / 150 % 2 == 0 ? 1 : -1, t % 70 < 30, t % 8 == 0));
            if (lazy.Man.Dead) Sim.Respawn(lazy);
        }
        Assert.True(lazy.Built < 3);
    }
}
