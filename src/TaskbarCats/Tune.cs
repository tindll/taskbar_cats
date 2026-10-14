namespace TaskbarCats;

/// What a busy cat is doing, chosen from the conversation's live signals (Mood).
/// Toy = the filler between tool calls (its pursuit: mouse, yarn or bird); a shell command plays with it too. Sniff/Bat/Watch = tool poses.
/// Box = the model has been thinking for BoxAfter. Rest/Nap = a long run. Limit = rate limited. Supervise = sub-agents only.
public enum Activity { Toy, Sniff, Bat, Watch, Box, Supervise, Rest, Nap, Limit, Butterfly }

/// One-off reactions to something that just happened in the conversation.
public enum Cue { Mrrp, Catch, Slip, Sneeze, Startle, Wonder, Reward }

public enum Trait { Lazy, Hyper, Curious, Shy }
public enum Look { Kitten, Cat, Big }
public enum Toy { Mouse, Yarn, Bird }

/// Every v4 tuning knob, in one place. Times are wall-clock; distances are DIP; speeds DIP/s.
public static class Tune
{
    // Context window (S5): 267k, or 1M once the conversation has gone past 300k. Belly stages at 40% and 70%.
    public const long Window = 267_000, BigWindow = 1_000_000, BigAbove = 300_000;
    public const double BellyAt1 = 0.4, BellyAt2 = 0.7;
    // Yarn ball size (S8): edits since the user last typed.
    public const int YarnAt1 = 3, YarnAt2 = 8;
    public const int MaxKittens = 3;

    // Activity choice (Mood).
    public static readonly TimeSpan MinDwell = TimeSpan.FromSeconds(6), MaxDwell = TimeSpan.FromSeconds(12);
    public static readonly TimeSpan BoxAfter = TimeSpan.FromSeconds(20), WonderAfter = TimeSpan.FromSeconds(45);
    public const double ToolWeight = 6, ToyWeight = 3, RareWeight = 0.3;
    public static readonly TimeSpan RestAfter = TimeSpan.FromMinutes(30), NapAfter = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan RewardAfter = TimeSpan.FromMinutes(10);
    // Cue rate limits: a busy conversation fires dozens of tool calls a minute.
    public static readonly TimeSpan MrrpEvery = TimeSpan.FromSeconds(30), CatchEvery = TimeSpan.FromSeconds(20),
        SlipEvery = TimeSpan.FromSeconds(10), WonderEvery = TimeSpan.FromSeconds(60);

    public static double Speed(Trait t) => t switch { Trait.Lazy => 0.8, Trait.Hyper => 1.3, _ => 1 };
    public static double ToyBias(Trait t) => t switch { Trait.Hyper => 1.5, Trait.Lazy => 0.6, _ => 1 };
    public static double RareBias(Trait t) => t == Trait.Curious ? 3 : 1;

    /// FNV-1a over the UTF-16 chars: stable across runs and machines (string.GetHashCode is not).
    public static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    public static Trait TraitOf(string sessionId) => (Trait)(Hash(sessionId) % 4);

    /// Hyper cats love the mouse, curious ones the bird, lazy ones the yarn; shy cats pick by hash.
    public static Toy FavouriteToy(string sessionId) => TraitOf(sessionId) switch
    {
        Trait.Hyper => Toy.Mouse, Trait.Curious => Toy.Bird, Trait.Lazy => Toy.Yarn,
        _ => (Toy)(Hash(sessionId) / 4 % 3),
    };

    /// S7. Unknown or missing model: the normal cat.
    public static Look LookOf(string? model) =>
        model is null ? Look.Cat
        : model.Contains("haiku", StringComparison.OrdinalIgnoreCase) ? Look.Kitten
        : model.Contains("opus", StringComparison.OrdinalIgnoreCase) || model.Contains("fable", StringComparison.OrdinalIgnoreCase) ? Look.Big
        : Look.Cat;

    /// The context window a conversation is measured against: the 1M window once it has ever been past BigAbove.
    public static long ContextWindow(long context, long maxContext) => Math.Max(context, maxContext) > BigAbove ? BigWindow : Window;

    public static int BellyStage(long context, long maxContext)
    {
        if (context <= 0) return 0;
        double f = context / (double)ContextWindow(context, maxContext);
        return f >= BellyAt2 ? 2 : f >= BellyAt1 ? 1 : 0;
    }

    public static int YarnStage(int edits) => edits >= YarnAt2 ? 2 : edits >= YarnAt1 ? 1 : 0;

    public static Activity? PoseFor(string? tool) => tool switch
    {
        "Read" or "Grep" or "Glob" or "LS" or "NotebookRead" => Activity.Sniff,
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => Activity.Bat,
        "WebFetch" or "WebSearch" => Activity.Watch,
        _ => null,
    };

    public static bool IsPose(Activity a) => a is Activity.Sniff or Activity.Bat or Activity.Watch;

    /// Pumpkin instead of the box, a bat instead of the overhead bird.
    public static bool IsHalloween(DateTime d) => d.Month == 10 && d.Day >= 17;

    // Pursuits (item 16): a cat's toy moves on one step every RotateEvery of its busy run.
    public static readonly TimeSpan RotateEvery = TimeSpan.FromMinutes(20);
    // Yarn ball. A bat at YarnBat rolls YarnBat² / (2 × YarnFriction) = 135 DIP.
    public const double YarnFriction = 120, YarnBat = 180, YarnBounce = 0.6, YarnStop = 5, YarnRollPx = 6;
    public const double YarnReach = 30;        // the cat bats when the ball is this close (DIP, left edge to left edge)
    public const double YarnFlipHz = 12;       // the 2-frame roll flips at most this often: faster aliases at 30 fps
    public const double YarnLandKeep = 0.8, YarnMaxRoll = 500;   // a thrown ball rolls on with most of its landing speed, capped
    public const double YarnFloorBounce = 0.45;   // vertical speed a thrown ball keeps off the taskbar (a cat: 0, lands on its feet)
    public const double YarnBounceMin = 150;      // DIP/s: a softer touchdown stops bouncing and rolls
    public const double YarnGroundKeep = 0.85;    // sideways speed kept at each floor bounce
    public const double HardLanding = 1600;       // DIP/s down at touchdown: the cat lands dizzy (a fall of ~500 DIP)
    public const double HardLandTime = 0.8;       // s of dizzy slip after a hard landing
    public const double YarnPlay = 8;          // s a cat plays with a thrown ball once it has caught up with it
    public const double YarnOrphan = 20;       // s a ball may lie on another monitor's strip with nobody crossing for it
    public const double CrossSlop = 0.5;       // DIP: a cat this close to its strip's end has reached it (float rounding)
    public const double YarnRun = 120;         // DIP/s: a cat chasing the yarn ball runs (4 x the 30 DIP/s walk)
    public const double YarnSprint = 270;      // DIP/s: the fastest fetch, for a ball that came down YarnFar or more away
    public const double YarnFar = 700;         // DIP from the cat to where the ball came down: full sprint from here
    public const double RunRateMax = 1.6;      // Run frames play at most this much faster: at 30 fps every frame still shows
    /// Fetch speed for a ball that came down `distance` DIP away: YarnRun close by, rising to YarnSprint at YarnFar.
    public static double FetchRun(double distance) =>
        YarnRun + (YarnSprint - YarnRun) * Math.Clamp(Math.Abs(distance) / YarnFar, 0, 1);
    // Bird. It flees when the cat is within BirdFlee (left edge to left edge) and lands BirdFlyDist away.
    // BirdFlyArc + the 48 DIP sprite stays inside the 120 DIP strip, and clears a cat it flies over.
    public const double BirdFlee = 70, BirdFlyDist = 220, BirdFlyTime = 1.0, BirdFlyArc = 56, BirdHop = 8;
    public const double BirdFlapMs = 120, BirdPeckEvery = 1.5, BirdPeckTime = 0.3;
    // Cat side (CatPlay.cs). Distances are from the cat's left edge to the thing's left edge.
    public const double YarnStart = 60, BirdStart = 160, MouseBeside = 60, CheeseGap = 22;
    public const double SniffSpeed = 15, StalkRange = 140, WiggleMin = 0.8, WiggleMax = 1.8, BatTime = 0.45;
    public const double FlyerSwing = 70, FlyerLift = 64, ButterflyAhead = 30, ButterflyLift = 40;
    public const double KittenGap = 20, KittenHop = 10;
    // Reactions (CatCues.cs), seconds unless noted.
    public const double MurmurTime = 1.5, CatchTime = 1.2, CatchRange = 180, SlipTime = 0.8, SneezeTime = 0.6;
    public const double StartleTime = 0.6, StartleArc = 20, ZoomiesTime = 3, ZoomiesSpeed = 110;
    public const double PetCooldown = 10;
    // Friends (CatFriends.cs): cats on one strip whose conversations run in the same folder.
    public const double GreetTimeout = 12, NuzzleTime = 1.5, FriendChance = 0.15;
    // Spreading out (Cat.MoveApart): idle cats keep this far from each other and from the strip end (the tray/clock).
    public const double PersonalSpace = Cat.Size, EdgeMargin = Cat.Size;
    public const double PetHover = 1.0;   // s the pointer rests on a cat before it counts as petting
    public const int NameMax = 16;        // characters in a cat's name tag (right-click menu)
    public const int StretchPercent = 3;       // % of idle picks that are a stretch (v4: 10, felt constant)
    public const double StretchHold = 0.2;     // s the stretch is held after its 3 frames (v4: 0.6)

    // Carrying (Carry in Flight.cs, Herd): the cat or ball trails the pointer on a critically damped spring.
    public const double CarryLag = 0.06;       // s: smoothing time; bigger = lazier, heavier follow
    public const double HoldToLift = 0.35;     // s: a press held this long picks the cat (or ball) up without moving
    public const int DragPx = 4;               // physical px of movement before a press becomes a carry
    public const double CatGrabPad = 6;        // DIP of clickable margin left, right and above a cat (never below: the taskbar)
    public const double YarnGrab = 48;         // DIP: the ball's square grab box, centred on it, standing on the strip floor

    // Teaser wand (Teaser.cs, CatChase.cs). Heights are DIP above the cats' feet; times are seconds.
    public const int WandSwingMs = 400;        // the cursor mouse swings between its two frames this often
    public const double PlayMax = 180;         // play mode stops by itself after this long
    public const double PlayReach = 400;       // the cursor lower than this: the cats run to it; higher: they sit and watch
    public const double JumpReach = 160;       // the cursor lower than this: they jump for it
    public const double JumpMin = 20, JumpMax = 64;   // jump height follows the cursor height; 64 + 48 sprite < the 120 strip
    public const double JumpTime = 0.55, JumpRest = 0.25, JumpUnder = 24;   // JumpUnder: how close to under it the cat must be to jump
    public const double PlayRun = YarnRun;     // DIP/s, times the trait speed
    public const double PlayStop = 6;          // DIP: close enough to stand still
    public const double CatchHeight = 24, CatchChance = 0.3, CatchHold = 0.7;   // the lure at strip level: sometimes caught

    // Drawing (OverlayWindow). 2/3 and 4/3 of 48 DIP = 2 and 4 DIP per sprite pixel: still crisp.
    public static double LookScale(Look l) => l switch { Look.Kitten => 2 / 3.0, Look.Big => 4 / 3.0, _ => 1 };
    /// Poses whose torso row reads as a belly when doubled (Sprites.Chubby).
    public static readonly IReadOnlySet<Anim> BellyAnims = new HashSet<Anim> { Anim.Walk, Anim.Run, Anim.Sit, Anim.Loaf, Anim.Bat, Anim.LookUp };
    /// v5: the poses a cat may fall asleep in, one picked at random each time it dozes off. No belly-up.
    public static readonly Anim[] SleepPoses = [Anim.Sleep, Anim.Curl, Anim.Flat];
    /// "!?" not "!": a bare "!" only ever means "waiting for you".
    public static string MurmurText(Murmur m) => m switch
    {
        Murmur.Mrrp => "mrrp", Murmur.Wonder => "?", Murmur.Heart => "♥", Murmur.Oops => "!?",
        Murmur.Achoo => "achoo!", Murmur.Limit => "rate limit", _ => "",
    };

    // v5 tools: hook-reported sessions (AgentHooks.cs).
    public static readonly TimeSpan EventExpiry = TimeSpan.FromHours(24);   // event files older than this are deleted
    public static readonly TimeSpan StaleWorking = TimeSpan.FromMinutes(10); // busy with no live pid to check: shown idle after this
    public const int HookTimeoutSec = 5;                                     // written into each tool's hook config
    /// Collar badge per tool (0xAARRGGBB); Claude cats keep their plain v4 look.
    public static uint? ToolColor(AgentTool t) => t switch
    {
        AgentTool.Codex => 0xFF10A37F,    // green
        AgentTool.Copilot => 0xFF8957E5,  // purple
        AgentTool.Cursor => 0xFFF2F2F2,   // white
        AgentTool.Gemini => 0xFF4285F4,   // blue
        _ => null,
    };
    public static string ToolLabel(AgentTool t) => t == AgentTool.Copilot ? "Copilot" : t.ToString();

    // Hover card (HoverCard.cs) and chimes (Chimes.cs).
    public const double CardDelay = 0.4;     // s the pointer rests on a cat before its card shows
    public const double CardLift = 26;       // DIP between the cat's head and the card's bottom: clears the MEOW bubble row
    public const double ChimeCatGap = 10;    // s: at most one chime per cat this often
    public const double ChimeGap = 2;        // s: at most one chime from all cats this often

    // Tag (CatFriends.cs): two idle friends (same folder, same strip) chase each other now and then.
    public const double TagChance = 0.1;          // of an idle pick with a friend that is not a greeting
    public const double TagTime = 10, TagCooldown = 240;      // s a game lasts; s before either cat plays again
    public const double TagRun = 100, TagChaseBoost = 1.25;   // DIP/s the runner flees (x trait speed); the chaser is this much faster
    public const double TagReach = 30, TagWait = 0.6, TagRest = 2.5;   // DIP for a tap; s the new chaser crouches; s they sit after
    // Roaming (CatRoam.cs, Herd.RoamOffer): an idle cat now and then walks off its strip onto the next monitor's.
    public const double RoamChance = 0.01;               // per idle pick (about one every 5 s): a lone cat crosses every 8 min or so
    public const double RoamSpread = 2, RoamPile = 0.25; // x chance when going spreads its project out / piles it up
    public const double RoamStay = 120;                  // s a cat that just arrived (or turned back) stays before it may go again
}
