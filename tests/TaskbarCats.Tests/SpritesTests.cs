using TaskbarCats;
using Xunit;

public class SpritesTests
{
    const string Allowed = ".KFDLPEWMSCGABYNHO";

    static IEnumerable<(string Name, string[] Rows)> AnimFrames() =>
        Sprites.Frames.SelectMany(kv => kv.Value.Select((f, i) => ($"{kv.Key}[{i}]", f)));

    static IEnumerable<(string Name, string[] Rows)> Props() =>
        Sprites.Mouse.Select((f, i) => ($"Mouse[{i}]", f))
            .Append(("Sardine", Sprites.Sardine))
            .Concat(Sprites.Yarn.SelectMany((size, s) => size.Select((f, i) => ($"Yarn[{s}][{i}]", f))))
            .Concat(Sprites.Bird.Select((f, i) => ($"Bird[{i}]", f)))
            .Concat(Sprites.BirdFly.Select((f, i) => ($"BirdFly[{i}]", f)))
            .Append(("Cheese", Sprites.Cheese))
            .Concat(Sprites.BatFly.Select((f, i) => ($"BatFly[{i}]", f)))
            .Concat(Sprites.Butterfly.Select((f, i) => ($"Butterfly[{i}]", f)))
            .Append(("Fish", Sprites.Fish))
            .Concat(Sprites.WandMouse.Select((f, i) => ($"WandMouse[{i}]", f)));

    [Fact] public void Every_anim_has_exactly_the_timed_frame_count()
    {
        foreach (var (anim, (frames, _)) in Cat.Timing)
            Assert.True(Sprites.Frames.TryGetValue(anim, out var f) && f.Length == frames, $"{anim}: want {frames}");
    }

    [Fact] public void Every_frame_is_16x16_with_known_chars()
    {
        foreach (var (name, rows) in AnimFrames().Concat(Props()))
        {
            Assert.True(rows.Length == 16, $"{name} has {rows.Length} rows");
            for (int y = 0; y < 16; y++)
            {
                Assert.True(rows[y].Length == 16, $"{name} row {y} has {rows[y].Length} chars");
                Assert.True(rows[y].All(Allowed.Contains), $"{name} row {y} bad char");
            }
        }
    }

    [Fact] public void Frames_within_an_anim_differ()
    {
        foreach (var (anim, frames) in Sprites.Frames)
            Assert.True(frames.Select(f => string.Concat(f)).Distinct().Count() == frames.Length, $"{anim} has duplicate frames");
    }

    // The code owns vertical motion (Lift). A frame whose feet are not on row 15 floats or pops.
    // Drawn in the air (Lift > 0 or a flight path): no bottom-row rule.
    static readonly string[] Airborne = ["Sardine", "BirdFly", "BatFly", "Butterfly", "WandMouse"];

    [Fact] public void Every_cat_frame_and_ground_prop_stands_on_the_bottom_row()
    {
        var grounded = AnimFrames().Concat(Props().Where(p => !Airborne.Contains(p.Name.Split('[')[0])));
        foreach (var (name, rows) in grounded)
            Assert.True(rows[15].Any(ch => ch != '.'), $"{name}: bottom row is empty, sprite floats");
    }

    [Fact] public void Every_frame_is_one_connected_shape()
    {
        foreach (var (name, rows) in AnimFrames().Concat(Props()))
            Assert.True(Components(rows) == 1, $"{name} has {Components(rows)} separate pieces (stray pixels)");
    }

    // Fur ('F','D','L') must never touch transparency or the edge directly: it needs a 'K' outline.
    [Fact] public void Fur_is_always_outlined()
    {
        foreach (var (name, rows) in AnimFrames())
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if ("FDL".IndexOf(rows[y][x]) < 0) continue;
                    foreach (var (dy, dx) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        int ny = y + dy, nx = x + dx;
                        bool open = ny is < 0 or > 15 || nx is < 0 or > 15 || rows[ny][nx] == '.';
                        Assert.False(open, $"{name}: bare fur at row {y} col {x}");
                    }
                }
    }

    [Fact] public void Cats_never_have_more_than_two_eyes()
    {
        foreach (var (name, rows) in AnimFrames())
        {
            int eyes = rows.Sum(r => r.Count(ch => ch == 'E'));
            Assert.True(eyes <= 2, $"{name} has {eyes} eye pixels");
        }
    }

    [Fact] public void Transparent_is_zero_alpha_and_fur_uses_palette()
    {
        var p = Sprites.Palettes[0];
        var px = Sprites.ToPixels(Sprites.Frames[Anim.Sit][0], p);
        Assert.Equal(256, px.Length);
        Assert.Equal(0u, px[0] >> 24);
        Assert.Contains(p.Fur, px);
    }

    [Fact] public void Props_render_with_any_palette()
    {
        foreach (var (_, rows) in Props())
            Assert.Equal(Sprites.ToPixels(rows, Sprites.Palettes[0]), Sprites.ToPixels(rows, Sprites.Palettes[2]));
    }

    // Walk: a planted foot moves back one sprite pixel per frame, so the body must move exactly that far per frame.
    [Fact] public void Walking_feet_do_not_slide() =>
        Assert.Equal(Cat.Size / Sprites.Px, Cat.WalkSpeed * Cat.Timing[Anim.Walk].Ms / 1000.0, 3);

    // Dangle 2 (swing left) and 3 (swing right) are mirror images: the swing looks the same both ways.
    [Fact] public void Dangle_swings_are_mirrored()
    {
        var left = Sprites.Frames[Anim.Dangle][2];
        var right = Sprites.Frames[Anim.Dangle][3];
        for (int y = 0; y < 16; y++) Assert.Equal(left[y], new string(right[y].Reverse().ToArray()));
    }

    [Fact] public void Zzz_frames_are_16x16_white_and_shadow_only_and_all_differ()
    {
        Assert.Equal(4, Sprites.Zzz.Length);
        foreach (var f in Sprites.Zzz)
        {
            Assert.Equal(16, f.Length);
            Assert.All(f, row => { Assert.Equal(16, row.Length); Assert.True(row.All(".WK".Contains), row); });
            Assert.Contains(f, row => row.Contains('W'));
        }
        Assert.Equal(4, Sprites.Zzz.Select(f => string.Concat(f)).Distinct().Count());
    }

    // The big Z sits low (nearest the cat), the small one high: frame 0 is only the big Z, frame 3 only the small one.
    [Fact] public void Zzz_starts_with_the_big_low_z_and_ends_with_the_small_high_z()
    {
        int Top(string[] f) => Array.FindIndex(f, r => r.Contains('W'));
        int Count(string[] f) => f.Sum(r => r.Count(ch => ch == 'W'));
        Assert.True(Top(Sprites.Zzz[0]) > Top(Sprites.Zzz[3]));
        Assert.True(Count(Sprites.Zzz[0]) > Count(Sprites.Zzz[3]));
    }

    [Fact] public void Zzz_cycles_big_to_small_every_step_and_loops()
    {
        Assert.Equal([0, 1, 2, 3, 0], new long[] { 0, 350, 700, 1050, 1400 }.Select(Sprites.ZzzFrame));
        Assert.Equal(0, Sprites.ZzzFrame(Sprites.ZzzMs - 1));
        Assert.InRange(Sprites.ZzzFrame(long.MaxValue), 0, 3);   // no overflow after a very long uptime
    }

    // v4: Sit, Hop and Leap share Walk's side-view head. Walk[0]'s head block (rows 3-7, cols 8-15) appears in every frame,
    // at any height, except Sit's blink (frame 1).
    [Fact] public void Sit_hop_and_leap_have_the_walking_profile_head()
    {
        var walk = Sprites.Frames[Anim.Walk][0];
        var head = Enumerable.Range(3, 5).Select(y => walk[y][8..]).ToArray();
        foreach (var anim in new[] { Anim.Sit, Anim.Hop, Anim.Leap })
            for (int i = 0; i < Sprites.Frames[anim].Length; i++)
            {
                if (anim == Anim.Sit && i == 1) continue;
                var f = Sprites.Frames[anim][i];
                bool found = Enumerable.Range(0, 12).Any(top => Enumerable.Range(0, 5).All(k => f[top + k][8..] == head[k]));
                Assert.True(found, $"{anim}[{i}] lacks the profile head");
            }
    }

    [Fact] public void Tool_poses_are_drawn() =>
        Assert.All(new[] { Anim.Bat, Anim.LookUp }, a => Assert.True(Sprites.Frames.ContainsKey(a), a.ToString()));

    [Fact] public void Thinking_and_tired_poses_are_drawn() =>
        Assert.All(new[] { Anim.Box, Anim.Pumpkin, Anim.Loaf }, a => Assert.True(Sprites.Frames.ContainsKey(a), a.ToString()));

    [Fact] public void Event_poses_are_drawn() =>
        Assert.All(new[] { Anim.Sneeze, Anim.Nuzzle, Anim.Slip }, a => Assert.True(Sprites.Frames.ContainsKey(a), a.ToString()));

    [Fact] public void Toys_have_their_frames()
    {
        Assert.Equal(3, Sprites.Yarn.Length);
        Assert.All(Sprites.Yarn, s => Assert.Equal(2, s.Length));
        Assert.Equal(2, Sprites.Bird.Length);
        Assert.Equal(2, Sprites.BirdFly.Length);
    }

    // WandMouse: the toy that follows the cursor in play mode.
    [Fact] public void The_cursor_mouse_is_pink_with_ears_a_bell_and_a_feather_tail_and_its_bell_glints()
    {
        Assert.Equal(2, Sprites.WandMouse.Length);
        Assert.NotEqual(string.Concat(Sprites.WandMouse[0]), string.Concat(Sprites.WandMouse[1]));
        Assert.All(Sprites.WandMouse, f =>
        {
            string all = string.Concat(f);
            Assert.Contains('P', all); Assert.Contains('C', all); Assert.Contains('W', all);
        });
    }

    [Fact] public void Bat_and_butterfly_animate() =>
        Assert.All(new[] { Sprites.BatFly, Sprites.Butterfly }, f => Assert.Equal(2, f.Length));

    [Fact] public void Chubby_frames_keep_every_lint()
    {
        foreach (var (name, rows) in AnimFrames())
            for (int stage = 1; stage <= 2; stage++)
                Lint($"{name} belly {stage}", Sprites.Chubby(rows, stage));
    }

    [Fact] public void Chubby_doubles_the_lowest_torso_row()
    {
        var walk = Sprites.Frames[Anim.Walk][0];
        var fat = Sprites.Chubby(walk, 2);
        Assert.Equal(walk[12..], fat[12..]);                  // legs stay put
        Assert.Equal([walk[11], walk[11], walk[11]], fat[9..12]);
        Assert.Equal(walk[2], fat[0]);                        // the rest moved up two rows
        Assert.Same(walk, Sprites.Chubby(walk, 0));
    }

    // v5: every sleep pose keeps Sleep's head on the right with its ears on row 9 or 10, so the drawn Zzz sits just above it.
    [Fact] public void Sleep_poses_keep_the_head_where_the_zzz_expects_it()
    {
        Assert.Equal(3, Tune.SleepPoses.Distinct().Count());
        foreach (var a in Tune.SleepPoses)
            foreach (var f in Sprites.Frames[a])
            {
                int ears = Array.FindIndex(f, r => r[10..].Any(ch => ch != '.'));
                Assert.InRange(ears, 9, 10);
                Assert.Equal(0, f.Sum(r => r.Count(ch => ch == 'E')));   // eyes closed
            }
    }

    static void Lint(string name, string[] rows)
    {
        Assert.Equal(16, rows.Length);
        Assert.True(rows[15].Any(ch => ch != '.'), $"{name}: floats");
        Assert.True(Components(rows) == 1, $"{name}: in pieces");
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if ("FDL".IndexOf(rows[y][x]) >= 0)
                    foreach (var (dy, dx) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        int ny = y + dy, nx = x + dx;
                        Assert.False(ny is < 0 or > 15 || nx is < 0 or > 15 || rows[ny][nx] == '.', $"{name}: bare fur at row {y} col {x}");
                    }
    }

    [Fact] public void Palette_choice_is_stable_per_session() =>
        Assert.Equal(Sprites.PaletteFor("494d4841-0e3a-4fe4-92ab-24b6811922d0"), Sprites.PaletteFor("494d4841-0e3a-4fe4-92ab-24b6811922d0"));

    static int Components(string[] rows)
    {
        var seen = new bool[16, 16];
        int count = 0;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                if (rows[y][x] == '.' || seen[y, x]) continue;
                count++;
                var stack = new Stack<(int, int)>([(y, x)]);
                seen[y, x] = true;
                while (stack.TryPop(out var p))
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int ny = p.Item1 + dy, nx = p.Item2 + dx;
                            if (ny is < 0 or > 15 || nx is < 0 or > 15 || rows[ny][nx] == '.' || seen[ny, nx]) continue;
                            seen[ny, nx] = true;
                            stack.Push((ny, nx));
                        }
            }
        return count;
    }
    // v5 customization: coats, plain coat, collars.
    // head down in a bowl or box, or tucked under the tail: no neck to ring, the frame comes back unchanged
    static readonly Anim[] CollarLess = [Anim.Box, Anim.Pumpkin, Anim.Curl];
    // a low pose with no chest under the band (it lies on the ground): the medallion takes the band's front px
    static readonly Anim[] NoChest = [Anim.Slip, Anim.Flat, Anim.Sleep, Anim.Stretch, Anim.Crouch, Anim.Eat];
    [Fact] public void There_are_ten_coats_with_unique_names_and_six_collars()
    {
        Assert.Equal(10, Sprites.Coats.Select(c => c.Name).Distinct().Count());
        Assert.Equal(6, Sprites.Collars.Select(c => c.Name).Distinct().Count());
        Assert.All(Sprites.Collars, c => Assert.NotEqual(0u, c.Colour));
    }

    [Fact] public void Dress_with_the_default_look_is_the_session_palette() =>
        Assert.Equal(Sprites.Palettes[1], Sprites.Dress(new CatLook(), Sprites.Palettes[1]));

    [Fact] public void Dress_applies_coat_plain_and_collar_and_ignores_unknown_names()
    {
        var tux = Sprites.Coats.Single(c => c.Name == "Tuxedo").P;
        var d = Sprites.Dress(new CatLook { Coat = "Tuxedo", Plain = true, Collar = "Red" }, Sprites.Palettes[0]);
        Assert.Equal(tux with { Dark = tux.Fur, Collar = Sprites.Collars.Single(c => c.Name == "Red").Colour }, d);
        Assert.Equal(Sprites.Palettes[0], Sprites.Dress(new CatLook { Coat = "Plaid", Collar = "Gold" }, Sprites.Palettes[0]));
    }

    [Fact] public void Every_pose_wears_a_horizontal_collar_band_and_a_medallion()
    {
        foreach (var (a, frames) in Sprites.Frames)
            for (int i = 0; i < frames.Length; i++)
            {
                var f = frames[i];
                var on = Sprites.Collar(f, a);
                string at = $"{a}[{i}]";
                if (CollarLess.Contains(a)) { Assert.Equal(f, on); continue; }
                var changed = Enumerable.Range(0, 256).Where(p => on[p / 16][p % 16] != f[p / 16][p % 16]).ToList();
                var band = changed.Where(p => on[p / 16][p % 16] == 'R').ToList();
                var gold = changed.Where(p => on[p / 16][p % 16] == 'T').ToList();
                Assert.True(band.Count + gold.Count == changed.Count && gold.Count == 1, $"{at}: {band.Count} band px, {gold.Count} medallion px");
                // only fur is painted
                Assert.All(changed, p => Assert.Contains(f[p / 16][p % 16], "FDL"));
                // one row, 1-4 px wide
                int y = band.Concat(gold).Min() / 16;
                var cols = band.Concat(gold).Where(p => p / 16 == y).Select(p => p % 16).Order().ToList();
                Assert.True(cols.Count is >= 2 and <= 4 && cols[^1] - cols[0] == cols.Count - 1, $"{at}: band is not one short run on one row");
                // covers every fur px of its run: the px just past either end is not fur, or the run is at its four-px cap
                int lo = cols[0], hi = cols[^1];
                Assert.False(hi + 1 < 16 && "FDL".Contains(on[y][hi + 1]), $"{at}: fur left in front of the band");
                Assert.True(cols.Count == 4 || lo == 0 || !"FDL".Contains(on[y][lo - 1]), $"{at}: fur left behind the band");
                // the medallion hangs below the band, at its front px; a low pose with no chest wears it in the band
                int gy = gold[0] / 16, gx = gold[0] % 16;
                if (NoChest.Contains(a) && gy == y) continue;
                Assert.True(gy == y + 1 && gx >= lo && gx <= hi, $"{at}: medallion not below the band");
            }
    }

    [Fact] public void Medallion_is_a_fixed_gold_that_recolouring_the_collar_leaves_alone()
    {
        var rows = Sprites.Collar(Sprites.Frames[Anim.Walk][0], Anim.Walk);
        var red = Sprites.ToPixels(rows, Sprites.Palettes[0] with { Collar = 0xFFD94A4A });
        var blue = Sprites.ToPixels(rows, Sprites.Palettes[0] with { Collar = 0xFF3D6FB6 });
        var gold = Enumerable.Range(0, 256).Where(p => rows[p / 16][p % 16] == 'T').ToList();
        Assert.NotEmpty(gold);
        Assert.All(gold, p => { Assert.Equal(red[p], blue[p]); Assert.Equal(0xFFFFD23Cu, red[p]); });
        Assert.DoesNotContain(rows.SelectMany(r => r), ch => false);
        Assert.DoesNotContain('T', Sprites.Frames.Values.SelectMany(fs => fs).SelectMany(f => f).SelectMany(r => r));   // no collar, no medallion
    }
    [Fact] public void Collar_pixels_render_in_the_collar_colour()
    {
        var p = Sprites.Palettes[0] with { Collar = 0xFF123456 };
        Assert.Contains(0xFF123456u, Sprites.ToPixels(Sprites.Collar(Sprites.Frames[Anim.Walk][0], Anim.Walk), p));
        Assert.DoesNotContain(0xFF123456u, Sprites.ToPixels(Sprites.Frames[Anim.Walk][0], p));
    }
}
