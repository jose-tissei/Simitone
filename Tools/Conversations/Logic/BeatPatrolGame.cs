using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public enum PatrolIncidentKind
    {
        BurglarProwler = 0,
        NoiseDisturbance = 1,
        CivicHazard = 2,
        InnocentCitizen = 3
    }

    public enum PatrolActionKind
    {
        SneakApprehend = 1,
        ConfrontLoud = 2,
        WarnCitizen = 3,
        IssueTicket = 4,
        InspectHazard = 5,
        CheckInCitizen = 6,
        ReturnToCruiser = 7,
        EndShift = 8
    }

    public enum PatrolActionRating
    {
        None = 0,
        Perfect = 1,
        Good = 2,
        Miss = 3
    }

    public enum PatrolLotZone
    {
        Downtown = 0,
        OldTown = 1,
        NeighborHouse = 2
    }

    public sealed class PatrolPieActionOption
    {
        public PatrolActionKind Action;
        public string Label;
        public bool UsesSneak;
        public string ResolveClip;
    }

    public sealed class PatrolRelationshipDelta
    {
        public short NeighborId;
        public string SimName;
        public short DailyDelta;
        public string MemoryFact;
    }

    public sealed class BeatPatrolIncident
    {
        public int Index;
        public PatrolIncidentKind Kind;
        public bool IsRequiredTarget; // true for Burglar, NoiseDisturbance, CivicHazard; false for InnocentCitizen
        public string ActorName;
        public short NeighborId;
        public string Title;
        public string RadioClue;
        public string HighLogicClue;

        public bool Resolved;
        public PatrolActionKind ChosenAction;
        public PatrolActionRating Rating;
        public int PointsAwarded;
        public int BonusFineSimoleons;
        public short DailyRelDelta;
        public string OfficerLine;
        public string SuspectLine;
        public string OutcomeBanner;
    }

    public sealed class BeatPatrolOutcome
    {
        public string Grade;
        public int TotalPoints;
        public int MaxPoints;
        public int ResolvedTargets;
        public int TotalTargets;
        public int StealthArrests;
        public string Summary;
        public CareerDispatchReward Reward;
        public List<PatrolRelationshipDelta> RelationshipDeltas = new List<PatrolRelationshipDelta>();
    }

    // Pure-C# simulation & scoring logic for Law Enforcement Level 3 ("Patrol Officer"):
    // Real-Lot Live Beat Patrol (DISP-06). Zero MonoGame/FSO dependencies.
    public sealed class BeatPatrolGame
    {
        public const float DefaultShiftDurationSec = 180f;
        public const int RequiredIncidentCount = 3;

        public string OfficerName { get; private set; }
        public int LogicSkillLevel { get; private set; }
        public int CharismaSkillLevel { get; private set; }
        public int PatrolLotId { get; private set; }
        public PatrolLotZone LotZone { get; private set; }
        public string SectorDisplayName { get; private set; }

        public bool IsBriefing { get; private set; }
        public bool IsFinished { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float ShiftDurationSec { get; private set; }
        public float RemainingSeconds { get { return Math.Max(0f, ShiftDurationSec - ElapsedSeconds); } }

        public IList<BeatPatrolIncident> Incidents { get { return incidents; } }
        public string LastCallout { get; private set; }
        public PatrolActionRating LastRating { get; private set; }
        public int Score { get; private set; }
        public int ExtraFineSimoleons { get; private set; }
        public int StealthArrests { get; private set; }

        readonly List<BeatPatrolIncident> incidents = new List<BeatPatrolIncident>();
        readonly Random rng;

        public BeatPatrolGame(
            string officerName,
            int logicSkillLevel,
            int charismaSkillLevel,
            int patrolLotId,
            PatrolLotZone lotZone,
            string sectorDisplayName,
            string disturbanceCitizenName = null,
            short disturbanceNeighborId = 0,
            string innocentCitizenName = null,
            short innocentNeighborId = 0,
            int? seed = null,
            bool startInBriefing = true)
        {
            OfficerName = string.IsNullOrWhiteSpace(officerName) ? "Officer" : officerName.Trim();
            LogicSkillLevel = Math.Max(0, Math.Min(10, logicSkillLevel));
            CharismaSkillLevel = Math.Max(0, Math.Min(10, charismaSkillLevel));
            PatrolLotId = patrolLotId;
            LotZone = lotZone;
            SectorDisplayName = string.IsNullOrWhiteSpace(sectorDisplayName)
                ? ("SECTOR " + patrolLotId)
                : sectorDisplayName.Trim();
            ShiftDurationSec = DefaultShiftDurationSec;
            rng = seed.HasValue ? new Random(seed.Value) : new Random();

            string disturbName = string.IsNullOrWhiteSpace(disturbanceCitizenName) ? "Local Resident" : disturbanceCitizenName.Trim();
            string innocentName = string.IsNullOrWhiteSpace(innocentCitizenName) ? "Evening Stroller" : innocentCitizenName.Trim();

            incidents.Add(new BeatPatrolIncident
            {
                Index = 0,
                Kind = PatrolIncidentKind.BurglarProwler,
                IsRequiredTarget = true,
                ActorName = "Burglar",
                NeighborId = 0,
                Title = "Prowler in Striped Mask",
                RadioClue = "Report of a masked prowler creeping around the premises — sneak up to catch them!",
                HighLogicClue = "LOGIC TIP: Click the Burglar and pick 'Sneak Up & Apprehend' so they don't bolt.",
            });

            incidents.Add(new BeatPatrolIncident
            {
                Index = 1,
                Kind = PatrolIncidentKind.NoiseDisturbance,
                IsRequiredTarget = true,
                ActorName = disturbName,
                NeighborId = disturbanceNeighborId,
                Title = "After-Hours Noise Complaint",
                RadioClue = "Neighbors reported loud boombox music outside — Warn or Issue Citation.",
                HighLogicClue = "LOGIC TIP: Friendly Warning builds goodwill (+Rel); Ticket pays +$25 fine (-6 Rel).",
            });

            incidents.Add(new BeatPatrolIncident
            {
                Index = 2,
                Kind = PatrolIncidentKind.CivicHazard,
                IsRequiredTarget = true,
                ActorName = "Trash Hazard",
                NeighborId = 0,
                Title = "Sidewalk Litter / Vandalism",
                RadioClue = "Knocked trash pile blocking the walkway — inspect and log the civic hazard.",
                HighLogicClue = "LOGIC TIP: Click the trash pile and choose 'Inspect & Log Hazard' for +Logic XP.",
            });

            incidents.Add(new BeatPatrolIncident
            {
                Index = 3,
                Kind = PatrolIncidentKind.InnocentCitizen,
                IsRequiredTarget = false,
                ActorName = innocentName,
                NeighborId = innocentNeighborId,
                Title = "Evening Bystander",
                RadioClue = "Innocent citizen enjoying the evening — check in politely, do NOT ticket.",
                HighLogicClue = "LOGIC TIP: Innocent bystander — 'Check In' earns goodwill; false tickets lose points.",
            });

            IsBriefing = startInBriefing;
            LastCallout = startInBriefing
                ? "Cruiser parked on " + SectorDisplayName + " — review dispatch orders to begin patrol."
                : "Patrol active! Pan the lot, spot trouble, and click suspects or hazards.";
        }

        public int RequiredCount { get { return incidents.Count(i => i.IsRequiredTarget); } }
        public int ResolvedRequiredCount { get { return incidents.Count(i => i.IsRequiredTarget && i.Resolved); } }

        public bool BeginPatrol()
        {
            if (!IsBriefing) return false;
            IsBriefing = false;
            LastCallout = "Cruiser staged at curb — scan the lot & click suspects or hazards to respond!";
            return true;
        }

        public void Tick(float dt)
        {
            if (IsBriefing || IsFinished || dt <= 0f) return;
            ElapsedSeconds += dt;
            if (ElapsedSeconds >= ShiftDurationSec)
            {
                ElapsedSeconds = ShiftDurationSec;
                IsFinished = true;
                LastCallout = "Patrol shift ended — returning to precinct with your report!";
            }
        }

        public void FinishShiftEarly()
        {
            if (IsFinished) return;
            IsBriefing = false;
            IsFinished = true;
            LastCallout = ResolvedRequiredCount >= RequiredCount
                ? "All sector calls cleared! Head back to your home lot."
                : "Shift logged (" + ResolvedRequiredCount + "/" + RequiredCount + " calls cleared).";
        }

        public string ActiveRadioHint()
        {
            var next = incidents.FirstOrDefault(i => i.IsRequiredTarget && !i.Resolved);
            if (next == null)
                return "ALL CLEAR! Click the Police Car or Claim Rewards to return home.";
            if (LogicSkillLevel >= 3 && !string.IsNullOrEmpty(next.HighLogicClue))
                return next.RadioClue + "  (" + next.HighLogicClue + ")";
            return next.RadioClue;
        }

        public static List<PatrolPieActionOption> GetActionsForIncident(PatrolIncidentKind kind, bool alreadyResolved)
        {
            var list = new List<PatrolPieActionOption>();
            if (alreadyResolved) return list;

            switch (kind)
            {
                case PatrolIncidentKind.BurglarProwler:
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.SneakApprehend,
                        Label = "Sneak Up & Apprehend",
                        UsesSneak = true,
                        ResolveClip = "patrol_apprehend",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.ConfrontLoud,
                        Label = "Halt! Confront Loudly",
                        UsesSneak = false,
                        ResolveClip = "patrol_apprehend",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.WarnCitizen,
                        Label = "Give Verbal Warning",
                        UsesSneak = false,
                        ResolveClip = "cadet_idle_attention",
                    });
                    break;

                case PatrolIncidentKind.NoiseDisturbance:
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.WarnCitizen,
                        Label = "Issue Friendly Warning",
                        UsesSneak = false,
                        ResolveClip = "cadet_idle_attention",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.IssueTicket,
                        Label = "Write Citation ($50)",
                        UsesSneak = false,
                        ResolveClip = "patrol_write_ticket",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.ConfrontLoud,
                        Label = "Order Noise Shutoff",
                        UsesSneak = false,
                        ResolveClip = "patrol_apprehend",
                    });
                    break;

                case PatrolIncidentKind.CivicHazard:
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.InspectHazard,
                        Label = "Inspect & Log Hazard",
                        UsesSneak = false,
                        ResolveClip = "patrol_inspect_clue",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.IssueTicket,
                        Label = "Write Litter Ticket",
                        UsesSneak = false,
                        ResolveClip = "patrol_write_ticket",
                    });
                    break;

                case PatrolIncidentKind.InnocentCitizen:
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.CheckInCitizen,
                        Label = "Check In (All Clear)",
                        UsesSneak = false,
                        ResolveClip = "cadet_idle_attention",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.IssueTicket,
                        Label = "Write Citation ($50)",
                        UsesSneak = false,
                        ResolveClip = "patrol_write_ticket",
                    });
                    list.Add(new PatrolPieActionOption
                    {
                        Action = PatrolActionKind.ConfrontLoud,
                        Label = "Detain Suspect",
                        UsesSneak = false,
                        ResolveClip = "patrol_apprehend",
                    });
                    break;
            }
            return list;
        }

        public static List<PatrolPieActionOption> GetActionsForCruiser(bool officerInCruiser)
        {
            var list = new List<PatrolPieActionOption>();
            if (!officerInCruiser)
            {
                list.Add(new PatrolPieActionOption
                {
                    Action = PatrolActionKind.ReturnToCruiser,
                    Label = "Return to Cruiser",
                    UsesSneak = false,
                    ResolveClip = "cadet_idle_attention",
                });
            }
            list.Add(new PatrolPieActionOption
            {
                Action = PatrolActionKind.EndShift,
                Label = "End Patrol & Return Home",
                UsesSneak = false,
                ResolveClip = "cadet_victory_salute",
            });
            return list;
        }

        public static bool ShouldEscortToCruiser(PatrolIncidentKind kind, PatrolActionKind action)
        {
            return kind == PatrolIncidentKind.BurglarProwler &&
                   (action == PatrolActionKind.SneakApprehend || action == PatrolActionKind.ConfrontLoud);
        }

        public PatrolActionRating ResolveIncident(int incidentIndex, PatrolActionKind action)
        {
            if (IsFinished) return PatrolActionRating.None;
            if (IsBriefing) BeginPatrol();
            if (incidentIndex < 0 || incidentIndex >= incidents.Count) return PatrolActionRating.None;
            var inc = incidents[incidentIndex];
            if (inc.Resolved) return PatrolActionRating.None;

            inc.Resolved = true;
            inc.ChosenAction = action;

            switch (inc.Kind)
            {
                case PatrolIncidentKind.BurglarProwler:
                    if (action == PatrolActionKind.SneakApprehend)
                    {
                        inc.Rating = PatrolActionRating.Perfect;
                        inc.PointsAwarded = 100;
                        inc.BonusFineSimoleons = 35;
                        StealthArrests++;
                        inc.OfficerLine = "SimCity PD! Caught you red-handed — freeze!";
                        inc.SuspectLine = "Whoa! I didn't even hear you sneaking up!";
                        inc.OutcomeBanner = "PERFECT STEALTH ARREST! (+100 pts, +$35 collar bonus)";
                    }
                    else if (action == PatrolActionKind.ConfrontLoud)
                    {
                        inc.Rating = PatrolActionRating.Good;
                        inc.PointsAwarded = 65;
                        inc.BonusFineSimoleons = 15;
                        inc.OfficerLine = "Halt! Step away from the door!";
                        inc.SuspectLine = "Uh-oh — fine, I'm dropping the crowbar!";
                        inc.OutcomeBanner = "GOOD STOP — noisy approach almost spooked them! (+65 pts)";
                    }
                    else
                    {
                        inc.Rating = PatrolActionRating.Miss;
                        inc.PointsAwarded = 15;
                        inc.OfficerLine = "Hey now, please don't loiter here.";
                        inc.SuspectLine = "Heh, see ya later, copper!";
                        inc.OutcomeBanner = "MISS — a polite warning let the burglar slip away! (+15 pts)";
                    }
                    break;

                case PatrolIncidentKind.NoiseDisturbance:
                    if (action == PatrolActionKind.WarnCitizen)
                    {
                        inc.Rating = PatrolActionRating.Perfect;
                        inc.PointsAwarded = 100;
                        inc.DailyRelDelta = (short)(CharismaSkillLevel >= 3 ? 5 : 3);
                        inc.OfficerLine = "Evening, " + inc.ActorName + " — mind keeping the boombox down tonight?";
                        inc.SuspectLine = "Oh! Sorry Officer " + OfficerName + ", turning it down right now.";
                        inc.OutcomeBanner = "PERFECT COMMUNITY WARNING! (+100 pts, +" + inc.DailyRelDelta + " Rel w/ " + inc.ActorName + ")";
                    }
                    else if (action == PatrolActionKind.IssueTicket)
                    {
                        inc.Rating = PatrolActionRating.Good;
                        inc.PointsAwarded = 85;
                        inc.BonusFineSimoleons = 25;
                        inc.DailyRelDelta = -6;
                        inc.OfficerLine = "Noise ordinance citation — sign right here, " + inc.ActorName + ".";
                        inc.SuspectLine = "Fifty Simoleons?! Ugh, fine!";
                        inc.OutcomeBanner = "CITATION ISSUED (+85 pts, +$25 fine, -6 Rel w/ " + inc.ActorName + ")";
                    }
                    else
                    {
                        inc.Rating = PatrolActionRating.Good;
                        inc.PointsAwarded = 70;
                        inc.DailyRelDelta = -2;
                        inc.OfficerLine = "SimCity PD — kill the music immediately!";
                        inc.SuspectLine = "Alright, alright, shutting it off!";
                        inc.OutcomeBanner = "DISTURBANCE CLEARED (+70 pts, -2 Rel w/ " + inc.ActorName + ")";
                    }
                    break;

                case PatrolIncidentKind.CivicHazard:
                    if (action == PatrolActionKind.InspectHazard)
                    {
                        inc.Rating = PatrolActionRating.Perfect;
                        inc.PointsAwarded = 100;
                        inc.OfficerLine = "Dispatch, 10-4 — walkway hazard logged and cleared.";
                        inc.SuspectLine = "";
                        inc.OutcomeBanner = "PERFECT HAZARD LOGGED! (+100 pts, +Logic XP)";
                    }
                    else
                    {
                        inc.Rating = PatrolActionRating.Miss;
                        inc.PointsAwarded = 20;
                        inc.OfficerLine = "Hmm... can't really cite a pile of trash.";
                        inc.SuspectLine = "";
                        inc.OutcomeBanner = "WRONG PROTOCOL — should have inspected & logged hazard! (+20 pts)";
                    }
                    break;

                case PatrolIncidentKind.InnocentCitizen:
                    if (action == PatrolActionKind.CheckInCitizen)
                    {
                        inc.Rating = PatrolActionRating.Good;
                        inc.PointsAwarded = 20;
                        inc.DailyRelDelta = (short)(CharismaSkillLevel >= 3 ? 4 : 2);
                        inc.OfficerLine = "Everything quiet out here tonight, " + inc.ActorName + "?";
                        inc.SuspectLine = "Sure is! Glad SimCity PD is watching the block.";
                        inc.OutcomeBanner = "GOODWILL CHECK-IN (+20 bonus pts, +" + inc.DailyRelDelta + " Rel w/ " + inc.ActorName + ")";
                    }
                    else
                    {
                        inc.Rating = PatrolActionRating.Miss;
                        inc.PointsAwarded = -25;
                        inc.DailyRelDelta = -10;
                        inc.OfficerLine = "Hold it — I'm writing you up right now!";
                        inc.SuspectLine = "What?! I wasn't doing anything wrong!";
                        inc.OutcomeBanner = "WRONGFUL CITATION! (-25 pts, -10 Rel w/ " + inc.ActorName + ")";
                    }
                    break;
            }

            Score = Math.Max(0, Score + inc.PointsAwarded);
            ExtraFineSimoleons += inc.BonusFineSimoleons;
            LastRating = inc.Rating;
            LastCallout = inc.OutcomeBanner;

            if (ResolvedRequiredCount >= RequiredCount)
                IsFinished = true;

            return inc.Rating;
        }

        public BeatPatrolOutcome ComputeOutcome()
        {
            int maxPts = RequiredCount * 100;
            int totalPts = Score;
            int resolved = ResolvedRequiredCount;

            string grade;
            int bonus;
            short perf;
            short logicXp;
            short charismaXp;
            short fun;
            string summary;
            string fact;

            if (resolved >= RequiredCount && totalPts >= 285)
            {
                grade = "S";
                bonus = 110 + ExtraFineSimoleons;
                perf = 26;
                logicXp = 55;
                charismaXp = 45;
                fun = 18;
                summary = "Exemplary Beat Patrol on " + SectorDisplayName + "! Every call handled by the book.";
                fact = OfficerName + " earned a Precinct Commendation for an S-Rank Beat Patrol on " + SectorDisplayName + ".";
            }
            else if (resolved >= RequiredCount && totalPts >= 220)
            {
                grade = "A";
                bonus = 80 + ExtraFineSimoleons;
                perf = 18;
                logicXp = 40;
                charismaXp = 35;
                fun = 14;
                summary = "Strong neighborhood patrol on " + SectorDisplayName + " — streets are safe tonight.";
                fact = OfficerName + " completed a clean Beat Patrol shift on " + SectorDisplayName + ".";
            }
            else if (resolved >= 2 || totalPts >= 130)
            {
                grade = "B";
                bonus = 50 + ExtraFineSimoleons;
                perf = 10;
                logicXp = 25;
                charismaXp = 20;
                fun = 8;
                summary = "Solid beat coverage — a few rough calls, but the block stayed calm.";
                fact = OfficerName + " worked an evening Beat Patrol shift on " + SectorDisplayName + ".";
            }
            else
            {
                grade = "C";
                bonus = 25 + ExtraFineSimoleons;
                perf = 4;
                logicXp = 15;
                charismaXp = 10;
                fun = 4;
                summary = "Quiet or incomplete beat report — keep a closer eye on the sector next shift.";
                fact = OfficerName + " patrolled " + SectorDisplayName + " on an evening shift.";
            }

            var outcome = new BeatPatrolOutcome
            {
                Grade = grade,
                TotalPoints = totalPts,
                MaxPoints = maxPts,
                ResolvedTargets = resolved,
                TotalTargets = RequiredCount,
                StealthArrests = StealthArrests,
                Summary = summary,
                Reward = new CareerDispatchReward
                {
                    Grade = grade,
                    Summary = summary,
                    BonusSimoleons = bonus,
                    JobPerformanceDelta = perf,
                    LogicXpDelta = logicXp,
                    CharismaXpDelta = charismaXp,
                    FunDelta = fun,
                    EnergyDelta = -6,
                    MemoryFact = fact,
                }
            };

            foreach (var inc in incidents)
            {
                if (inc.Resolved && inc.NeighborId > 0 && inc.DailyRelDelta != 0)
                {
                    string relFact = inc.DailyRelDelta > 0
                        ? (OfficerName + " handled an evening patrol call politely with " + inc.ActorName + ".")
                        : (OfficerName + " issued a patrol citation to " + inc.ActorName + " during Beat Patrol.");
                    outcome.RelationshipDeltas.Add(new PatrolRelationshipDelta
                    {
                        NeighborId = inc.NeighborId,
                        SimName = inc.ActorName,
                        DailyDelta = inc.DailyRelDelta,
                        MemoryFact = relFact,
                    });
                }
            }

            return outcome;
        }

        // Rotates cleanly across Downtown (21..30), Old Town (50..78), and Neighbor Residential lots (1..10, excluding homeHouseId).
        public static int PickPatrolLot(
            int homeHouseId,
            IList<int> availableHouseIds,
            int seed,
            out PatrolLotZone zone,
            out string sectorName)
        {
            zone = PatrolLotZone.Downtown;
            sectorName = "DOWNTOWN DISTRICT";

            var downtown = new List<int>();
            var oldTown = new List<int>();
            var neighbors = new List<int>();

            if (availableHouseIds != null)
            {
                foreach (int id in availableHouseIds)
                {
                    if (id <= 0 || id == homeHouseId) continue;
                    if (id >= 21 && id <= 30) downtown.Add(id);
                    else if (id >= 50 && id <= 78) oldTown.Add(id);
                    else if (id >= 1 && id <= 10) neighbors.Add(id);
                }
            }

            downtown.Sort();
            oldTown.Sort();
            neighbors.Sort();

            var pools = new List<Tuple<PatrolLotZone, List<int>>>();
            if (downtown.Count > 0) pools.Add(Tuple.Create(PatrolLotZone.Downtown, downtown));
            if (oldTown.Count > 0) pools.Add(Tuple.Create(PatrolLotZone.OldTown, oldTown));
            if (neighbors.Count > 0) pools.Add(Tuple.Create(PatrolLotZone.NeighborHouse, neighbors));

            if (pools.Count == 0)
            {
                zone = PatrolLotZone.Downtown;
                sectorName = "DOWNTOWN LOT 21";
                return 21;
            }

            int safeSeed = Math.Abs(seed);
            var chosenPool = pools[safeSeed % pools.Count];
            zone = chosenPool.Item1;
            int lotId = chosenPool.Item2[(safeSeed / pools.Count) % chosenPool.Item2.Count];
            sectorName = FormatSectorName(lotId, zone);
            return lotId;
        }

        public static PatrolLotZone ClassifyLotZone(int lotId)
        {
            if (lotId >= 21 && lotId <= 30) return PatrolLotZone.Downtown;
            if (lotId >= 50 && lotId <= 78) return PatrolLotZone.OldTown;
            return PatrolLotZone.NeighborHouse;
        }

        public static string FormatSectorName(int lotId, PatrolLotZone zone)
        {
            switch (zone)
            {
                case PatrolLotZone.Downtown: return "DOWNTOWN PLAZA  •  LOT " + lotId;
                case PatrolLotZone.OldTown: return "OLD TOWN DISTRICT  •  LOT " + lotId;
                default: return "NEIGHBORHOOD BEAT  •  HOUSE " + lotId;
            }
        }
    }
}
