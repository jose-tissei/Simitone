using System;

namespace Simitone.Conversations
{
    public enum CadetDrillStageKind
    {
        Sprint = 0,
        Vault = 1,
        TargetDrill = 2,
    }

    public enum CadetHitRating
    {
        None = 0,
        Miss = 1,
        Good = 2,
        Perfect = 3,
    }

    public sealed class CadetStageState
    {
        public int StageIndex;           // 0..2
        public CadetDrillStageKind Kind;
        public string Title;
        public string Subtitle;
        public string ActionVerb;
        public float ZoneCenter;         // 0.25..0.75
        public float PerfectHalfWidth;   // Green sweet-spot half-width (scales with Body skill)
        public float GoodHalfWidth;      // Amber near-hit half-width
        public float MarkerSpeed;        // Cycles per second across 0..1..0
        public CadetHitRating Rating;
        public int Points;
        public float LockedMarkerPos;
    }

    public sealed class CadetAcademyOutcome
    {
        public string Grade = "C";
        public string Summary = "";
        public int TotalPoints;
        public int PerfectCount;
        public int GoodCount;
        public int MissCount;
        public int BodySkillLevel;
        public CareerDispatchReward Reward = new CareerDispatchReward();
    }

    // Pure state machine for Law Enforcement Level 2 ("Cadet"): Academy Obstacle & Drill (DISP-05).
    // 3 sequential physical timing stages (Sprint -> Vault -> Target Drill) where the Sim's Body
    // skill (0..10) widens the green sweet-spot zone, driving a live unified 3D academy track.
    public sealed class CadetAcademyGame
    {
        public const int TotalStages = 3;
        public const float ResolutionBeatSeconds = 1.45f;

        readonly Random rng;
        readonly CadetStageState[] stages;

        public string SimName { get; private set; }
        public int BodySkillLevel { get; private set; }       // 0..10
        public int CurrentStageIndex { get; private set; }    // 0..2
        public float MarkerPosition { get; private set; }     // 0..1
        public float MarkerDirection { get; private set; }    // +1 or -1
        public bool IsResolvingStage { get; private set; }
        public float ResolutionTimer { get; private set; }
        public float ResolutionProgress
        {
            get
            {
                if (!IsResolvingStage) return 0f;
                return Math.Max(0f, Math.Min(1f, 1f - ResolutionTimer / ResolutionBeatSeconds));
            }
        }
        public bool IsBriefing { get; private set; }
        public bool IsFinished { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float StageElapsedSeconds { get; private set; }
        public string LastCallout { get; private set; }
        public CadetHitRating LastRating { get; private set; }
        public int SoundCueSequence { get; private set; }

        public CadetStageState[] Stages { get { return stages; } }
        public CadetStageState ActiveStage
        {
            get
            {
                if (CurrentStageIndex < 0 || CurrentStageIndex >= stages.Length) return null;
                return stages[CurrentStageIndex];
            }
        }

        // Green sweet-spot total width scales linearly with Body skill (0..10):
        // Body 0 -> 0.12 (12% of bar), Body 5 -> 0.19 (19%), Body 10 -> 0.26 (26%).
        public static float ComputePerfectZoneWidth(int bodySkillLevel)
        {
            int lvl = Math.Max(0, Math.Min(10, bodySkillLevel));
            return 0.12f + lvl * 0.014f;
        }

        public static float ComputeGoodZoneWidth(int bodySkillLevel)
        {
            return ComputePerfectZoneWidth(bodySkillLevel) + 0.18f;
        }

        public CadetAcademyGame(string simName, int bodySkillLevel = 0, int? seed = null, bool startInBriefing = true)
        {
            SimName = string.IsNullOrWhiteSpace(simName) ? "Sim" : simName.Trim();
            BodySkillLevel = Math.Max(0, Math.Min(10, bodySkillLevel));
            rng = seed.HasValue ? new Random(seed.Value) : new Random();

            float basePerfectHalf = ComputePerfectZoneWidth(BodySkillLevel) * 0.5f;
            float baseGoodHalf = ComputeGoodZoneWidth(BodySkillLevel) * 0.5f;

            stages = new[]
            {
                new CadetStageState
                {
                    StageIndex = 0,
                    Kind = CadetDrillStageKind.Sprint,
                    Title = "100M ACADEMY SPRINT",
                    Subtitle = "Hit SPACE inside the green pace zone to explode through the finish!",
                    ActionVerb = "SPRINT BURST",
                    ZoneCenter = RandomCenter(0.36f, 0.64f),
                    PerfectHalfWidth = basePerfectHalf,
                    GoodHalfWidth = baseGoodHalf,
                    MarkerSpeed = 0.82f,
                },
                new CadetStageState
                {
                    StageIndex = 1,
                    Kind = CadetDrillStageKind.Vault,
                    Title = "TACTICAL WALL VAULT",
                    Subtitle = "Lock your takeoff in the green zone to clear the timber wall cleanly!",
                    ActionVerb = "VAULT WALL",
                    ZoneCenter = RandomCenter(0.32f, 0.68f),
                    PerfectHalfWidth = basePerfectHalf * 0.96f,
                    GoodHalfWidth = baseGoodHalf * 0.96f,
                    MarkerSpeed = 0.98f,
                },
                new CadetStageState
                {
                    StageIndex = 2,
                    Kind = CadetDrillStageKind.TargetDrill,
                    Title = "PRECISION TARGET DRILL",
                    Subtitle = "Steady your sights on the green bullseye zone to drop the steel target!",
                    ActionVerb = "FIRE / LOCK",
                    ZoneCenter = RandomCenter(0.30f, 0.70f),
                    PerfectHalfWidth = basePerfectHalf * 0.92f,
                    GoodHalfWidth = baseGoodHalf * 0.92f,
                    MarkerSpeed = 1.14f,
                },
            };

            IsBriefing = startInBriefing;
            CurrentStageIndex = 0;
            MarkerPosition = 0.08f;
            MarkerDirection = 1f;
            LastCallout = startInBriefing
                ? "Cadet " + SimName + " reported to the Police Academy yard — review the drill briefing."
                : "Academy whistle blown — lock in your sprint stride!";
            LastRating = CadetHitRating.None;
        }

        public bool BeginDrill()
        {
            if (!IsBriefing || IsFinished) return false;
            IsBriefing = false;
            CurrentStageIndex = 0;
            StageElapsedSeconds = 0f;
            MarkerPosition = 0.08f;
            MarkerDirection = 1f;
            LastCallout = "Academy whistle blown — lock in your sprint stride!";
            LastRating = CadetHitRating.None;
            SoundCueSequence++;
            return true;
        }

        float RandomCenter(float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        public void Tick(float deltaSeconds)
        {
            if (IsFinished || deltaSeconds <= 0f) return;

            ElapsedSeconds += deltaSeconds;
            if (IsBriefing) return;

            StageElapsedSeconds += deltaSeconds;

            if (IsResolvingStage)
            {
                ResolutionTimer -= deltaSeconds;
                if (ResolutionTimer <= 0f)
                {
                    ResolutionTimer = 0f;
                    IsResolvingStage = false;
                    CurrentStageIndex++;
                    StageElapsedSeconds = 0f;
                    if (CurrentStageIndex >= stages.Length)
                    {
                        IsFinished = true;
                        LastCallout = "Academy course complete! Review your drill scorecard.";
                    }
                    else
                    {
                        MarkerPosition = 0.08f;
                        MarkerDirection = 1f;
                        var next = ActiveStage;
                        LastCallout = "Next station: " + next.Title + " — ready on the line!";
                        LastRating = CadetHitRating.None;
                    }
                }
                return;
            }

            var st = ActiveStage;
            if (st == null) return;

            MarkerPosition += MarkerDirection * st.MarkerSpeed * deltaSeconds;
            if (MarkerPosition >= 1f)
            {
                MarkerPosition = 1f - (MarkerPosition - 1f);
                MarkerDirection = -1f;
            }
            else if (MarkerPosition <= 0f)
            {
                MarkerPosition = -MarkerPosition;
                MarkerDirection = 1f;
            }
        }

        public CadetHitRating LockIn()
        {
            if (IsBriefing || IsFinished || IsResolvingStage) return CadetHitRating.None;
            var st = ActiveStage;
            if (st == null) return CadetHitRating.None;

            float dist = Math.Abs(MarkerPosition - st.ZoneCenter);
            CadetHitRating rating;
            int pts;

            if (dist <= st.PerfectHalfWidth)
            {
                rating = CadetHitRating.Perfect;
                pts = 100;
            }
            else if (dist <= st.GoodHalfWidth)
            {
                rating = CadetHitRating.Good;
                pts = 65;
            }
            else
            {
                rating = CadetHitRating.Miss;
                pts = 15;
            }

            st.Rating = rating;
            st.Points = pts;
            st.LockedMarkerPos = MarkerPosition;
            LastRating = rating;
            LastCallout = BuildCallout(st.Kind, rating);
            IsResolvingStage = true;
            ResolutionTimer = ResolutionBeatSeconds;
            SoundCueSequence++;
            return rating;
        }

        static string BuildCallout(CadetDrillStageKind kind, CadetHitRating rating)
        {
            switch (kind)
            {
                case CadetDrillStageKind.Sprint:
                    if (rating == CadetHitRating.Perfect) return "BLISTERING SPRINT! New academy track record (+100)";
                    if (rating == CadetHitRating.Good) return "SOLID PACE! Clean stride across the tape (+65)";
                    return "STUMBLED OUT OF THE BLOCKS! Off-rhythm sprint (+15)";
                case CadetDrillStageKind.Vault:
                    if (rating == CadetHitRating.Perfect) return "TEXTBOOK VAULT! Cleared the timber wall effortlessly (+100)";
                    if (rating == CadetHitRating.Good) return "SCRAMBLED OVER! Heavy landing, still cleared (+65)";
                    return "CLIPPED THE WALL! Bruised shins on takeoff (+15)";
                default:
                    if (rating == CadetHitRating.Perfect) return "DEAD CENTER BULLSEYE! Steel target knocked flat (+100)";
                    if (rating == CadetHitRating.Good) return "ON TARGET! Wing hit spun the steel plate (+65)";
                    return "WIDE SHOT! Grazed the range frame (+15)";
            }
        }

        public void AutoCompleteRemainingPerfect()
        {
            if (IsBriefing) BeginDrill();
            while (!IsFinished)
            {
                if (!IsResolvingStage && ActiveStage != null)
                {
                    MarkerPosition = ActiveStage.ZoneCenter;
                    LockIn();
                }
                Tick(ResolutionBeatSeconds + 0.05f);
            }
        }

        public CadetAcademyOutcome ComputeOutcome()
        {
            int total = 0, perf = 0, good = 0, miss = 0;
            for (int i = 0; i < stages.Length; i++)
            {
                total += stages[i].Points;
                if (stages[i].Rating == CadetHitRating.Perfect) perf++;
                else if (stages[i].Rating == CadetHitRating.Good) good++;
                else if (stages[i].Rating == CadetHitRating.Miss) miss++;
            }

            var o = new CadetAcademyOutcome
            {
                TotalPoints = total,
                PerfectCount = perf,
                GoodCount = good,
                MissCount = miss,
                BodySkillLevel = BodySkillLevel,
            };

            if (total >= 265)
            {
                o.Grade = "S";
                o.Summary = "Honor Cadet! Sergeant posted your drill times on the academy board.";
                o.Reward = new CareerDispatchReward
                {
                    Grade = "S",
                    Summary = o.Summary,
                    BonusSimoleons = 75,
                    JobPerformanceDelta = 24,
                    BodyXpDelta = 75, // +0.75 Body skill point
                    EnergyDelta = -10,
                    FunDelta = 10,
                    MemoryFact = SimName + " aced the Police Academy obstacle course with Honor Cadet marks.",
                };
            }
            else if (total >= 195)
            {
                o.Grade = "A";
                o.Summary = "Sharp academy form — strong cardio and steady aim.";
                o.Reward = new CareerDispatchReward
                {
                    Grade = "A",
                    Summary = o.Summary,
                    BonusSimoleons = 55,
                    JobPerformanceDelta = 16,
                    BodyXpDelta = 55,
                    EnergyDelta = -10,
                    FunDelta = 6,
                    MemoryFact = SimName + " passed the Police Academy obstacle drill with strong scores.",
                };
            }
            else if (total >= 130)
            {
                o.Grade = "B";
                o.Summary = "Passed the course — keep building Body skill for a wider sweet spot.";
                o.Reward = new CareerDispatchReward
                {
                    Grade = "B",
                    Summary = o.Summary,
                    BonusSimoleons = 35,
                    JobPerformanceDelta = 10,
                    BodyXpDelta = 35,
                    EnergyDelta = -12,
                    FunDelta = 2,
                };
            }
            else
            {
                o.Grade = "C";
                o.Summary = "Rough morning on the obstacle course — hit the gym before next drill!";
                o.Reward = new CareerDispatchReward
                {
                    Grade = "C",
                    Summary = o.Summary,
                    BonusSimoleons = 20,
                    JobPerformanceDelta = 4,
                    BodyXpDelta = 20,
                    EnergyDelta = -14,
                    FunDelta = -4,
                };
            }

            return o;
        }
    }
}
