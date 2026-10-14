using TaskbarCats;
using Xunit;

public class CatCueTests
{
    static Cat Landed(SessionStatus s = SessionStatus.Working, Toy toy = Toy.Mouse, int seed = 1)
    {
        var c = new Cat("a", 300, new Random(seed)) { Status = s, Toy = toy };
        CatTests.Run(c, 0.6);
        return c;
    }

    [Fact] public void A_murmur_is_brief_and_leaves_the_status_bubble_alone()
    {
        var c = Landed(SessionStatus.Idle);
        c.React(Cue.Mrrp);
        Assert.Equal(Murmur.Mrrp, c.Murmur);
        var bubbles = new HashSet<Bubble>();
        CatTests.Run(c, 2, each: x => bubbles.Add(x.Bubble));
        Assert.Contains(Bubble.Meow, bubbles);
        Assert.Equal(Murmur.None, c.Murmur);
    }

    [Fact] public void A_sneeze_plays_once_then_the_cat_carries_on()
    {
        var c = Landed();
        c.React(Cue.Sneeze);
        Assert.Equal(Anim.Sneeze, c.Anim);
        Assert.Equal(Murmur.Achoo, c.Murmur);
        CatTests.Run(c, 1);
        Assert.NotEqual(Anim.Sneeze, c.Anim);
    }

    [Fact] public void A_startle_is_a_hissing_jump()
    {
        var c = Landed(SessionStatus.Idle);
        c.React(Cue.Startle);
        double top = 0;
        CatTests.Run(c, Tune.StartleTime, each: x => top = Math.Max(top, x.Lift));
        Assert.True(top > 10);
        CatTests.Run(c, 0.2);
        Assert.Equal(0, c.Lift);
        Assert.NotEqual(Anim.Hiss, c.Anim);
    }

    [Fact] public void A_slip_lets_the_mouse_escape()
    {
        var c = Landed();
        CatTests.Run(c, 0.2);
        c.React(Cue.Slip);
        Assert.Equal(Anim.Slip, c.Anim);
        Assert.Equal(Murmur.Oops, c.Murmur);
        double gap = Math.Abs(c.MouseX - c.X);
        CatTests.Run(c, 0.4);
        Assert.True(Math.Abs(c.MouseX - c.X) > gap + 30);
    }

    [Fact] public void A_catch_pounces_holds_the_mouse_then_lets_it_go()
    {
        var c = Landed();
        c.React(Cue.Catch);
        Assert.Equal(Anim.Crouch, c.Anim);
        CatTests.Run(c, Cat.CrouchTime + Cat.LeapTime + 0.1);
        Assert.Equal(Anim.Bat, c.Anim);
        Assert.Equal(2, c.Frame);
        Assert.True(c.HasMouse);
        Assert.Equal(Cat.ToyGap, Math.Abs(c.MouseX - c.X), 1);   // pinned in front of the nose, not under the face
        CatTests.Run(c, Tune.CatchTime + 0.5);
        Assert.NotEqual(Anim.Bat, c.Anim);
        Assert.True(Math.Abs(c.MouseX - c.X) > 40);
    }

    [Fact] public void A_yarn_catch_is_a_big_bat()
    {
        var c = Landed(toy: Toy.Yarn);
        c.React(Cue.Catch);
        Assert.True(Math.Abs(c.Yarn!.Vx) >= Tune.YarnBat * 1.5);
    }

    [Fact] public void A_bird_flutters_off_on_a_catch()
    {
        var c = Landed(toy: Toy.Bird);
        c.React(Cue.Catch);
        Assert.True(c.Bird!.Flying);
    }

    [Fact] public void A_reward_leaves_a_fish_and_brings_zoomies_without_hiding_meow()
    {
        var c = Landed(SessionStatus.Idle);
        c.React(Cue.Reward);
        Assert.True(c.HasPresent);
        var anims = new HashSet<Anim>();
        var bubbles = new HashSet<Bubble>();
        double x0 = c.X;
        CatTests.Run(c, 2, each: x => { anims.Add(x.Anim); bubbles.Add(x.Bubble); });
        Assert.Contains(Anim.Run, anims);
        Assert.Contains(Bubble.Meow, bubbles);
        Assert.NotEqual(x0, c.X);
        Assert.True(c.TakePresent());
        Assert.False(c.TakePresent());
        c.React(Cue.Reward);
        c.Status = SessionStatus.Working;
        Assert.False(c.HasPresent);
    }

    [Fact] public void Petting_shows_a_heart_at_most_every_ten_seconds()
    {
        var c = Landed(SessionStatus.Idle);
        c.Pet();
        Assert.Equal(Murmur.Heart, c.Murmur);
        CatTests.Run(c, 2);
        c.Pet();
        Assert.Equal(Murmur.None, c.Murmur);
        CatTests.Run(c, Tune.PetCooldown);
        c.Pet();
        Assert.Equal(Murmur.Heart, c.Murmur);
    }

    [Fact] public void Sleep_poses_vary_by_seed_and_a_click_wakes_any_of_them()
    {
        var poses = new HashSet<Anim>();
        for (int seed = 0; seed < 50; seed++)
        {
            var c = Landed(SessionStatus.Idle, seed: seed);
            CatTests.Run(c, Cat.SleepAfter + 1);
            Assert.True(c.IsAsleep);
            Assert.Contains(c.Anim, Tune.SleepPoses);
            poses.Add(c.Anim);
            c.Click();
            CatTests.Run(c, 0.1);
            Assert.False(c.IsAsleep);
        }
        Assert.Equal(Tune.SleepPoses.Length, poses.Count);   // every pose shows up
    }

    [Fact] public void Rate_limited_cats_keep_saying_so()
    {
        var c = Landed();
        c.Activity = Activity.Limit;
        CatTests.Run(c, 5, each: x => Assert.Equal(Murmur.Limit, x.Murmur));
    }

    [Fact] public void A_held_cat_does_not_react()
    {
        var c = Landed();
        Assert.True(c.Grab());
        c.React(Cue.Sneeze);
        Assert.Equal(Anim.Dangle, c.Anim);
    }

    [Fact] public void A_hard_landing_makes_the_cat_dizzy_then_it_carries_on()
    {
        var c = Landed();
        Assert.True(c.Grab());
        c.Land(200, true, hard: true);
        CatTests.Run(c, 0.1);
        Assert.Equal(Anim.Slip, c.Anim);
        Assert.Equal(Murmur.Oops, c.Murmur);
        CatTests.Run(c, Tune.HardLandTime);
        Assert.True(c.IsLive);
        Assert.NotEqual(Anim.Slip, c.Anim);
    }
}
