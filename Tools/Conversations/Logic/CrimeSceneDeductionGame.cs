using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public enum ClueCategory
    {
        Footprint = 0,
        Motive = 1,
        Alibi = 2,
    }

    public enum DetectivePhase
    {
        Briefing = 0,
        Investigating = 1,
        AccusationClimax = 2,
        Finished = 3,
    }

    public sealed class CrimeClue
    {
        public ClueCategory Category;
        public string StationLabel = "";
        public string ShortTag = "";
        public string ForensicFinding = "";
        public string LogicHint = "";
        public string DetectiveLine = "";
        public bool Discovered;
    }

    public sealed class CrimeSuspect
    {
        public int SlotIndex;
        public string Name = "";
        public string RoleHint = "";
        public string FootprintClaim = "";
        public string MotiveClaim = "";
        public string AlibiClaim = "";
        public string QuestionAnswerLine = "";
        public string CaughtLine = "";
        public string WronglyAccusedLine = "";
        public bool ContradictsFootprint;
        public bool ContradictsMotive;
        public bool ContradictsAlibi;
        public bool IsCulprit;
        public bool Questioned;
        public bool EliminatedByForensics;

        public int VisibleContradictions(bool footprintDiscovered, bool motiveDiscovered, bool alibiDiscovered)
        {
            if (!Questioned) return 0;
            int n = 0;
            if (footprintDiscovered && ContradictsFootprint) n++;
            if (motiveDiscovered && ContradictsMotive) n++;
            if (alibiDiscovered && ContradictsAlibi) n++;
            return n;
        }
    }

    public sealed class CrimeCaseDefinition
    {
        public string Id = "";
        public string CaseTitle = "";
        public string VenueName = "";
        public string IncidentBrief = "";
        public CrimeClue[] Clues = Array.Empty<CrimeClue>();
        public CrimeSuspect CulpritTemplate;
        public CrimeSuspect RedHerringTemplate;
        public CrimeSuspect InnocentTemplate;
    }

    public sealed class ClueInspectResult
    {
        public bool Accepted;
        public bool AlreadyDiscovered;
        public ClueCategory Category;
        public CrimeClue Clue;
        public int PointsEarned;
        public int NewlyExposedContradictions;
        public string FeedbackBanner = "";
        public string DetectiveLine = "";
    }

    public sealed class SuspectQuestionResult
    {
        public bool Accepted;
        public bool AlreadyQuestioned;
        public int SuspectIndex;
        public CrimeSuspect Suspect;
        public int PointsEarned;
        public int VisibleContradictions;
        public string DetectiveLine = "";
        public string SuspectLine = "";
        public string FeedbackBanner = "";
    }

    public sealed class ForensicsEliminateResult
    {
        public bool Accepted;
        public int EliminatedIndex = -1;
        public string EliminatedName = "";
        public string FeedbackBanner = "";
    }

    public sealed class CrimeAccuseResult
    {
        public bool Accepted;
        public bool CorrectCulprit;
        public int AccusedIndex;
        public string AccusedName = "";
        public string TrueCulpritName = "";
        public string DetectiveAccuseLine = "";
        public string SuspectReactionLine = "";
        public string FeedbackBanner = "";
        public CrimeSceneOutcome Outcome;
    }

    public sealed class CrimeSceneOutcome
    {
        public string DetectiveName = "Detective";
        public string CaseTitle = "";
        public string VenueName = "";
        public string AccusedName = "";
        public string TrueCulpritName = "";
        public bool SolvedCorrectly;
        public int CluesFound;
        public int SuspectsQuestioned;
        public bool UsedForensics;
        public int FinalScore;
        public string Grade = "C";
        public string Summary = "";
        public CareerDispatchReward Reward = new CareerDispatchReward();
        public List<string> SuspectNames = new List<string>();
        public int AccusedIndex = -1;
        public int CulpritIndex = -1;
    }

    // Pure net461/net9.0 logic for Law Enforcement Level 6 ("Detective"): Crime Scene Deduction.
    // Zero MonoGame/FreeSO types so every rule, case file, Logic contradiction flag, Forensics
    // cross-check, scoring rule, and grade reward is deterministically unit-tested.
    public sealed class CrimeSceneDeductionGame
    {
        static readonly CrimeCaseDefinition[] CaseBank =
        {
            new CrimeCaseDefinition
            {
                Id = "simcity_diamond",
                CaseTitle = "The Missing SimCity Diamond",
                VenueName = "Manor Parlor Study",
                IncidentBrief = "The insured SimCity Diamond vanished during the evening gala. Inspect all 3 forensic stations, question the parlor guests, and expose the thief!",
                Clues = new[]
                {
                    new CrimeClue
                    {
                        Category = ClueCategory.Footprint,
                        StationLabel = "Entry Rug & Pedestal",
                        ShortTag = "Muddy size-10 boot tread",
                        ForensicFinding = "Fresh red-clay size-10 boot prints lead straight from the patio rug to the empty diamond pedestal.",
                        LogicHint = "Whoever claims they wore clean indoor dress shoes is lying.",
                        DetectiveLine = "Fresh red clay on the rug... heavy size-10 boot treads leading right to the case.",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Motive,
                        StationLabel = "Study Desk Ledger",
                        ShortTag = "Overdue §12,000 debt note",
                        ForensicFinding = "A torn promissory note tucked under the desk lamp demands §12,000 by midnight tonight.",
                        LogicHint = "Match the sudden financial pressure against the guests' money claims.",
                        DetectiveLine = "A final debt notice for twelve thousand Simoleons hidden on the study desk!",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Alibi,
                        StationLabel = "Grandfather Clock",
                        ShortTag = "Clock stopped at 9:15 PM",
                        ForensicFinding = "The parlor pendulum jammed at exactly 9:15 PM when the thief bumped the cabinet.",
                        LogicHint = "Anyone claiming they were far away at 9:15 PM needs a tight alibi.",
                        DetectiveLine = "The pendulum stopped cold at 9:15 PM — that pins down the exact minute of the heist.",
                    },
                },
                CulpritTemplate = new CrimeSuspect
                {
                    RoleHint = "Estate Appraiser",
                    FootprintClaim = "Wore 'polished loafers' — yet red clay cakes their boot heels.",
                    MotiveClaim = "Insists finances are spotless, despite frantic creditor calls all week.",
                    AlibiClaim = "Claims they were in the conservatory at 9:15 PM — no witness saw them.",
                    QuestionAnswerLine = "Me? I wore clean loafers and stayed in the conservatory past 9:15. I don't need anyone's diamond!",
                    CaughtLine = "Alright, alright! The debts were crushing me — the SimCity Diamond is in my coat pocket!",
                    WronglyAccusedLine = "Check your notes again, Detective!",
                    ContradictsFootprint = true,
                    ContradictsMotive = true,
                    ContradictsAlibi = true,
                    IsCulprit = true,
                },
                RedHerringTemplate = new CrimeSuspect
                {
                    RoleHint = "Rival Collector",
                    FootprintClaim = "Wearing delicate satin evening heels — zero clay or boot tread.",
                    MotiveClaim = "Openly bitter after losing the diamond auction last Tuesday.",
                    AlibiClaim = "Confirmed singing karaoke in the foyer at 9:15 PM with four witnesses.",
                    QuestionAnswerLine = "Fine, I wanted that diamond at auction! But I was singing in the foyer at 9:15 in spotless heels!",
                    CaughtLine = "Impossible!",
                    WronglyAccusedLine = "Outrageous! Four witnesses heard me singing at 9:15 — and look at my satin heels!",
                    ContradictsFootprint = false,
                    ContradictsMotive = true,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
                InnocentTemplate = new CrimeSuspect
                {
                    RoleHint = "Neighborhood Violinist",
                    FootprintClaim = "Wearing dry velvet slippers — size 7, spotless soles.",
                    MotiveClaim = "Just received a generous SimCity Symphony grant yesterday.",
                    AlibiClaim = "Performing a duet on the front porch at 9:15 PM under the streetlamp.",
                    QuestionAnswerLine = "I was playing violin on the porch at 9:15 in velvet slippers. My grant just cleared — why steal?",
                    CaughtLine = "No way!",
                    WronglyAccusedLine = "Detective, I was on the porch playing violin at 9:15! You accused the wrong Sim!",
                    ContradictsFootprint = false,
                    ContradictsMotive = false,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
            },
            new CrimeCaseDefinition
            {
                Id = "forged_llama_deed",
                CaseTitle = "The Forged Llama Deed",
                VenueName = "Old Town Antique Study",
                IncidentBrief = "Someone swapped the original SimCity Llama Park land deed with a crude forgery before the council vote!",
                Clues = new[]
                {
                    new CrimeClue
                    {
                        Category = ClueCategory.Footprint,
                        StationLabel = "Threshold Rug",
                        ShortTag = "Chalk dust scuff marks",
                        ForensicFinding = "White drafting-chalk footprints trail from the hallway rug straight to the deed cabinet.",
                        LogicHint = "Look for the guest hiding chalk-dusted shoes.",
                        DetectiveLine = "White drafting chalk on the parlor rug — fresh tracks leading right to the desk.",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Motive,
                        StationLabel = "Executive Study Desk",
                        ShortTag = "Mall zoning bid rejected",
                        ForensicFinding = "A stamped rejection letter on the desk killed a lucrative shopping-plaza deal on Llama Park.",
                        LogicHint = "Who stands to profit if the original park deed disappears?",
                        DetectiveLine = "A rejected plaza zoning permit... somebody wanted Llama Park rezoned badly.",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Alibi,
                        StationLabel = "Antique Timepiece",
                        ShortTag = "Chime muted at 8:40 PM",
                        ForensicFinding = "Someone stuffed velvet cloth into the clock chime at 8:40 PM to mask the lockpick clicks.",
                        LogicHint = "Pin down every guest's whereabouts at exactly 8:40 PM.",
                        DetectiveLine = "Velvet stuffed inside the chime mechanism — the swap happened at 8:40 PM sharp.",
                    },
                },
                CulpritTemplate = new CrimeSuspect
                {
                    RoleHint = "Commercial Developer",
                    FootprintClaim = "Claims they stayed off the hallway rug, yet white chalk dusts both soles.",
                    MotiveClaim = "Secretly financed the rejected Llama Park shopping-plaza bid.",
                    AlibiClaim = "Claims they were phoning City Hall at 8:40 PM — switchboard shows zero calls.",
                    QuestionAnswerLine = "I was calling City Hall at 8:40! I barely glanced at that dusty desk or the park deed.",
                    CaughtLine = "Curse that chalk dust! Fine — the real Llama Park deed is taped inside my briefcase!",
                    WronglyAccusedLine = "Prove it!",
                    ContradictsFootprint = true,
                    ContradictsMotive = true,
                    ContradictsAlibi = true,
                    IsCulprit = true,
                },
                RedHerringTemplate = new CrimeSuspect
                {
                    RoleHint = "Local Historian",
                    FootprintClaim = "Clean rain galoshes — no drafting chalk anywhere.",
                    MotiveClaim = "Argued loudly yesterday over who should archive the historic deed.",
                    AlibiClaim = "Spotted lecturing to six neighbors in the library at 8:40 PM.",
                    QuestionAnswerLine = "Yes, I argued about archiving the deed! But I was lecturing six neighbors at 8:40 PM!",
                    CaughtLine = "Never!",
                    WronglyAccusedLine = "Ridiculous! Six people heard my 8:40 lecture, and there isn't a speck of chalk on me!",
                    ContradictsFootprint = false,
                    ContradictsMotive = true,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
                InnocentTemplate = new CrimeSuspect
                {
                    RoleHint = "Botanical Gardener",
                    FootprintClaim = "Wearing clean canvas sneakers — zero chalk residue.",
                    MotiveClaim = "Volunteers every weekend to plant roses inside Llama Park.",
                    AlibiClaim = "Watering prize azaleas outside with the mail carrier watching at 8:40 PM.",
                    QuestionAnswerLine = "I love Llama Park! I was outside watering azaleas at 8:40 — ask the mail carrier!",
                    CaughtLine = "What?!",
                    WronglyAccusedLine = "Why would I forge a deed to destroy my own rose garden? You got the wrong Sim, Detective!",
                    ContradictsFootprint = false,
                    ContradictsMotive = false,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
            },
            new CrimeCaseDefinition
            {
                Id = "vanished_golden_gnome",
                CaseTitle = "The Vanished Golden Gnome",
                VenueName = "Suburban Manor Parlor",
                IncidentBrief = "The prize-winning Solid Gold Lawn Gnome vanished from its parlor display minutes before the SimCity Garden Gala!",
                Clues = new[]
                {
                    new CrimeClue
                    {
                        Category = ClueCategory.Footprint,
                        StationLabel = "Display Rug",
                        ShortTag = "Gold-leaf paint flecks",
                        ForensicFinding = "Metallic gold-leaf flakes and heavy work-boot treads smudge the carpet beside the empty plinth.",
                        LogicHint = "Check whose footwear has fresh metallic gold flecks.",
                        DetectiveLine = "Metallic gold flakes pressed into a work-boot heel print... the gnome was chipped on the way out.",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Motive,
                        StationLabel = "Study Desk Notes",
                        ShortTag = "Disqualified contest entry",
                        ForensicFinding = "A red-ink judge's memo on the desk disqualifies a rival sculptor from tomorrow's §10,000 prize.",
                        LogicHint = "Who held a fresh grudge over the garden sculpture trophy?",
                        DetectiveLine = "Disqualified from the ten-thousand-Simoleon grand prize! That's a classic motive.",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Alibi,
                        StationLabel = "Parlor Clock",
                        ShortTag = "Glass cracked at 7:50 PM",
                        ForensicFinding = "The gnome's pointy hat cracked the clock face at 7:50 PM as the thief hurried past.",
                        LogicHint = "Cross-check everyone's story for 7:50 PM.",
                        DetectiveLine = "A pointy dent right at the 7:50 mark on the clock face — the thief bumped it fleeing!",
                    },
                },
                CulpritTemplate = new CrimeSuspect
                {
                    RoleHint = "Disqualified Sculptor",
                    FootprintClaim = "Denies touching the plinth, yet metallic gold flakes coat their work boots.",
                    MotiveClaim = "Furious after being disqualified from the §10,000 sculpture contest.",
                    AlibiClaim = "Claims they were fixing a car at 7:50 PM — engine hood is ice cold.",
                    QuestionAnswerLine = "I was tuning my car at 7:50! Sure, the judges were unfair, but I never stepped near that plinth!",
                    CaughtLine = "Ugh, fine! My gnome was twice as artistic! You'll find the golden one behind the hedge!",
                    WronglyAccusedLine = "No proof!",
                    ContradictsFootprint = true,
                    ContradictsMotive = true,
                    ContradictsAlibi = true,
                    IsCulprit = true,
                },
                RedHerringTemplate = new CrimeSuspect
                {
                    RoleHint = "Antique Pawnbroker",
                    FootprintClaim = "Wearing polished dress oxfords — completely clean of gold flakes.",
                    MotiveClaim = "Offered to buy the golden gnome twice earlier this afternoon.",
                    AlibiClaim = "Confirmed dining at the Bistro at 7:50 PM with a timestamped receipt.",
                    QuestionAnswerLine = "Sure I offered to buy it — that's my job! But I have a 7:50 Bistro receipt and spotless oxfords!",
                    CaughtLine = "Ridiculous!",
                    WronglyAccusedLine = "Look at my timestamped 7:50 Bistro receipt, Detective! You arrested an innocent Sim!",
                    ContradictsFootprint = false,
                    ContradictsMotive = true,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
                InnocentTemplate = new CrimeSuspect
                {
                    RoleHint = "Pastry Chef",
                    FootprintClaim = "White kitchen clogs dusted only with powdered sugar.",
                    MotiveClaim = "Hired to cater the gala dessert buffet — paid up front.",
                    AlibiClaim = "Frosting the gala cake in the kitchen with two waiters at 7:50 PM.",
                    QuestionAnswerLine = "I was frosting eclairs at 7:50 with two waiters watching! That's powdered sugar on my clogs, not gold!",
                    CaughtLine = "Excuse me?!",
                    WronglyAccusedLine = "I was frosting cakes at 7:50! Check the clues again, Detective!",
                    ContradictsFootprint = false,
                    ContradictsMotive = false,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
            },
            new CrimeCaseDefinition
            {
                Id = "midnight_safe_cracker",
                CaseTitle = "The Midnight Safe Cracker",
                VenueName = "Downtown Penthouse Study",
                IncidentBrief = "The penthouse wall safe was cracked open without forcing the lock, and the rare Simoleon Bond certificates are gone!",
                Clues = new[]
                {
                    new CrimeClue
                    {
                        Category = ClueCategory.Footprint,
                        StationLabel = "Parlor Carpet",
                        ShortTag = "Left-heel rubber squeak scuff",
                        ForensicFinding = "Distinct black rubber-sole scuffs with a notched left heel circle the safe's carpet.",
                        LogicHint = "Watch for a suspect hiding a notched black rubber sole.",
                        DetectiveLine = "Black rubber scuffs with a chipped left heel right in front of the safe.",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Motive,
                        StationLabel = "Executive Study Desk",
                        ShortTag = "Fired locksmith notice",
                        ForensicFinding = "A termination memo on the desk shows the penthouse security contractor was fired this morning.",
                        LogicHint = "Who knew the safe model and had a score to settle?",
                        DetectiveLine = "Fired this very morning after installing the safe... textbook insider motive!",
                    },
                    new CrimeClue
                    {
                        Category = ClueCategory.Alibi,
                        StationLabel = "Mantel Clock",
                        ShortTag = "Power cut logged at 10:05 PM",
                        ForensicFinding = "The electric clock flickered and froze at 10:05 PM when someone tripped the alarm breaker.",
                        LogicHint = "Check who had no verified alibi at 10:05 PM.",
                        DetectiveLine = "Breaker tripped at 10:05 PM — whoever cut the alarm was in this room right then.",
                    },
                },
                CulpritTemplate = new CrimeSuspect
                {
                    RoleHint = "Former Locksmith",
                    FootprintClaim = "Denies owning rubber soles, yet their left boot heel has the exact chip.",
                    MotiveClaim = "Fired without severance this morning — furious at the penthouse owner.",
                    AlibiClaim = "Claims they were home asleep at 10:05 PM — elevator log shows their keycard.",
                    QuestionAnswerLine = "I went straight home to sleep before 10:05! Getting fired stung, sure, but I didn't touch that safe!",
                    CaughtLine = "Fine! They owed me three months of back pay — take the bond certificates!",
                    WronglyAccusedLine = "You can't prove that!",
                    ContradictsFootprint = true,
                    ContradictsMotive = true,
                    ContradictsAlibi = true,
                    IsCulprit = true,
                },
                RedHerringTemplate = new CrimeSuspect
                {
                    RoleHint = "Impatient Heir",
                    FootprintClaim = "Wearing leather-soled Italian dress shoes — zero rubber marks.",
                    MotiveClaim = "Recently argued about the bond inheritance over dinner.",
                    AlibiClaim = "Confirmed playing chess on the balcony with the butler at 10:05 PM.",
                    QuestionAnswerLine = "We argued about the bonds, fine! But I was playing chess with the butler at 10:05 in leather shoes!",
                    CaughtLine = "Absurd!",
                    WronglyAccusedLine = "Ask the butler — I was checkmating him at 10:05! Wrong Sim, Detective!",
                    ContradictsFootprint = false,
                    ContradictsMotive = true,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
                InnocentTemplate = new CrimeSuspect
                {
                    RoleHint = "Piano Tuner",
                    FootprintClaim = "Soft suede loafers with intact heels — no black rubber.",
                    MotiveClaim = "Never met the owner before tonight's tuning appointment.",
                    AlibiClaim = "Stopped at the downstairs lobby desk signing out at 10:05 PM.",
                    QuestionAnswerLine = "I signed out at the lobby desk at 10:05 in suede loafers! I only came here to tune a grand piano!",
                    CaughtLine = "Me?!",
                    WronglyAccusedLine = "Check the lobby sign-out sheet for 10:05, Detective! I'm completely innocent!",
                    ContradictsFootprint = false,
                    ContradictsMotive = false,
                    ContradictsAlibi = false,
                    IsCulprit = false,
                },
            },
        };

        readonly Random rng;
        readonly CrimeCaseDefinition activeCase;
        readonly CrimeClue[] clues;
        readonly CrimeSuspect[] suspects;

        public string DetectiveName { get; }
        public int LogicSkill { get; }
        public int CharismaSkill { get; }
        public string CaseId { get { return activeCase.Id; } }
        public string CaseTitle { get { return activeCase.CaseTitle; } }
        public string VenueName { get { return activeCase.VenueName; } }
        public string IncidentBrief { get { return activeCase.IncidentBrief; } }

        public DetectivePhase Phase { get; private set; } = DetectivePhase.Briefing;
        public bool IsFinished { get { return Phase == DetectivePhase.Finished; } }
        public bool LogicFlagsContradictions { get { return LogicSkill >= 2; } }
        public bool HasForensicsCrossCheck { get { return LogicSkill >= 5; } }
        public int ForensicsChargesLeft { get; private set; }
        public bool UsedForensics { get; private set; }
        public int Score { get; private set; }
        public int CluesFoundCount { get { return clues.Count(c => c.Discovered); } }
        public int SuspectsQuestionedCount { get { return suspects.Count(s => s.Questioned); } }
        public int CulpritIndex { get; }
        public int AccusedIndex { get; private set; } = -1;
        public bool SolvedCorrectly { get; private set; }
        public string LastFeedback { get; private set; } = "Inspect the 3 crime scene stations and question the parlor suspects.";
        public CrimeSceneOutcome Outcome { get; private set; }

        public IReadOnlyList<CrimeClue> Clues { get { return clues; } }
        public IReadOnlyList<CrimeSuspect> Suspects { get { return suspects; } }

        public CrimeSceneDeductionGame(
            string detectiveName,
            int logicSkill,
            int charismaSkill,
            string[] suspectNames = null,
            int seed = 42)
        {
            DetectiveName = string.IsNullOrWhiteSpace(detectiveName) ? "Detective" : detectiveName.Trim();
            LogicSkill = Math.Max(0, Math.Min(10, logicSkill));
            CharismaSkill = Math.Max(0, Math.Min(10, charismaSkill));
            ForensicsChargesLeft = LogicSkill >= 5 ? 1 : 0;

            rng = new Random(seed);
            activeCase = CaseBank[Math.Abs(seed) % CaseBank.Length];

            // Deep-copy clues so each shift has isolated discovery flags
            clues = new CrimeClue[3];
            for (int i = 0; i < 3; i++)
            {
                var src = activeCase.Clues[i];
                clues[i] = new CrimeClue
                {
                    Category = src.Category,
                    StationLabel = src.StationLabel,
                    ShortTag = src.ShortTag,
                    ForensicFinding = src.ForensicFinding,
                    LogicHint = src.LogicHint,
                    DetectiveLine = src.DetectiveLine,
                    Discovered = false,
                };
            }

            // Build & deterministically shuffle the 3 suspect roles
            var names = new[]
            {
                (suspectNames != null && suspectNames.Length > 0 && !string.IsNullOrWhiteSpace(suspectNames[0])) ? suspectNames[0].Trim() : "Mortimer",
                (suspectNames != null && suspectNames.Length > 1 && !string.IsNullOrWhiteSpace(suspectNames[1])) ? suspectNames[1].Trim() : "Bella",
                (suspectNames != null && suspectNames.Length > 2 && !string.IsNullOrWhiteSpace(suspectNames[2])) ? suspectNames[2].Trim() : "Betty",
            };

            var templates = new List<CrimeSuspect>
            {
                CloneSuspect(activeCase.CulpritTemplate),
                CloneSuspect(activeCase.RedHerringTemplate),
                CloneSuspect(activeCase.InnocentTemplate),
            };

            // Fisher-Yates shuffle so culprit slot varies cleanly by seed
            for (int i = templates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = templates[i];
                templates[i] = templates[j];
                templates[j] = tmp;
            }

            suspects = new CrimeSuspect[3];
            int culpritSlot = 0;
            for (int i = 0; i < 3; i++)
            {
                templates[i].SlotIndex = i;
                templates[i].Name = names[i];
                suspects[i] = templates[i];
                if (templates[i].IsCulprit) culpritSlot = i;
            }
            CulpritIndex = culpritSlot;
        }

        static CrimeSuspect CloneSuspect(CrimeSuspect s)
        {
            return new CrimeSuspect
            {
                RoleHint = s.RoleHint,
                FootprintClaim = s.FootprintClaim,
                MotiveClaim = s.MotiveClaim,
                AlibiClaim = s.AlibiClaim,
                QuestionAnswerLine = s.QuestionAnswerLine,
                CaughtLine = s.CaughtLine,
                WronglyAccusedLine = s.WronglyAccusedLine,
                ContradictsFootprint = s.ContradictsFootprint,
                ContradictsMotive = s.ContradictsMotive,
                ContradictsAlibi = s.ContradictsAlibi,
                IsCulprit = s.IsCulprit,
            };
        }

        public void BeginInvestigation()
        {
            if (Phase == DetectivePhase.Briefing)
            {
                Phase = DetectivePhase.Investigating;
                LastFeedback = "Click the 3 crime scene stations to collect clues, then question the suspects!";
            }
        }

        public int GetVisibleContradictions(int suspectIndex)
        {
            if (suspectIndex < 0 || suspectIndex >= suspects.Length) return 0;
            return suspects[suspectIndex].VisibleContradictions(clues[0].Discovered, clues[1].Discovered, clues[2].Discovered);
        }

        public ClueInspectResult InspectClue(int clueIndex)
        {
            var res = new ClueInspectResult();
            if (Phase == DetectivePhase.Briefing) BeginInvestigation();
            if (Phase != DetectivePhase.Investigating || clueIndex < 0 || clueIndex >= clues.Length)
                return res;

            var clue = clues[clueIndex];
            res.Accepted = true;
            res.Category = clue.Category;
            res.Clue = clue;
            res.DetectiveLine = clue.DetectiveLine;

            if (clue.Discovered)
            {
                res.AlreadyDiscovered = true;
                res.FeedbackBanner = "CLUE REVIEWED (" + clue.Category.ToString().ToUpperInvariant() + "): " + clue.ShortTag;
                LastFeedback = res.FeedbackBanner;
                return res;
            }

            clue.Discovered = true;
            res.PointsEarned = 15;
            Score += res.PointsEarned;

            int newContradictions = 0;
            if (LogicFlagsContradictions)
            {
                for (int i = 0; i < suspects.Length; i++)
                {
                    if (!suspects[i].Questioned) continue;
                    bool hit = (clueIndex == 0 && suspects[i].ContradictsFootprint)
                            || (clueIndex == 1 && suspects[i].ContradictsMotive)
                            || (clueIndex == 2 && suspects[i].ContradictsAlibi);
                    if (hit) newContradictions++;
                }
            }
            res.NewlyExposedContradictions = newContradictions;

            res.FeedbackBanner = "CLUE LOGGED [" + clue.Category.ToString().ToUpperInvariant() + "]: " + clue.ShortTag
                + (newContradictions > 0 ? "  (LOGIC FLAGGED " + newContradictions + " CONTRADICTION!)" : "");
            LastFeedback = res.FeedbackBanner;
            return res;
        }

        public SuspectQuestionResult QuestionSuspect(int suspectIndex)
        {
            var res = new SuspectQuestionResult();
            if (Phase == DetectivePhase.Briefing) BeginInvestigation();
            if (Phase != DetectivePhase.Investigating || suspectIndex < 0 || suspectIndex >= suspects.Length)
                return res;

            var s = suspects[suspectIndex];
            res.Accepted = true;
            res.SuspectIndex = suspectIndex;
            res.Suspect = s;
            res.DetectiveLine = "Tell me where you were during the incident, " + s.Name + " — and explain your shoes and motives.";
            res.SuspectLine = s.QuestionAnswerLine;

            if (s.Questioned)
            {
                res.AlreadyQuestioned = true;
                res.VisibleContradictions = LogicFlagsContradictions ? GetVisibleContradictions(suspectIndex) : 0;
                res.FeedbackBanner = "STATEMENT REVIEWED: " + s.Name + " (" + s.RoleHint + ")";
                LastFeedback = res.FeedbackBanner;
                return res;
            }

            s.Questioned = true;
            int charismaBonus = CharismaSkill >= 4 ? 2 : 0;
            res.PointsEarned = 10 + charismaBonus;
            Score += res.PointsEarned;
            res.VisibleContradictions = LogicFlagsContradictions ? GetVisibleContradictions(suspectIndex) : 0;

            if (res.VisibleContradictions > 0)
            {
                res.FeedbackBanner = s.Name + " QUESTIONED — LOGIC FLAGGED " + res.VisibleContradictions + " CONTRADICTION" + (res.VisibleContradictions == 1 ? "" : "S") + "!";
            }
            else
            {
                res.FeedbackBanner = s.Name + " QUESTIONED — Statement logged on Evidence Board.";
            }
            LastFeedback = res.FeedbackBanner;
            return res;
        }

        public ForensicsEliminateResult UseForensicsCrossCheck()
        {
            var res = new ForensicsEliminateResult();
            if (Phase == DetectivePhase.Briefing) BeginInvestigation();
            if (Phase != DetectivePhase.Investigating || ForensicsChargesLeft <= 0)
                return res;

            // Prefer eliminating the clean innocent first, then the red herring; never eliminate the true culprit!
            int target = -1;
            for (int i = 0; i < suspects.Length; i++)
            {
                if (!suspects[i].IsCulprit && !suspects[i].EliminatedByForensics &&
                    !suspects[i].ContradictsFootprint && !suspects[i].ContradictsMotive && !suspects[i].ContradictsAlibi)
                {
                    target = i;
                    break;
                }
            }
            if (target < 0)
            {
                for (int i = 0; i < suspects.Length; i++)
                {
                    if (!suspects[i].IsCulprit && !suspects[i].EliminatedByForensics)
                    {
                        target = i;
                        break;
                    }
                }
            }
            if (target < 0) return res;

            ForensicsChargesLeft--;
            UsedForensics = true;
            suspects[target].EliminatedByForensics = true;
            suspects[target].Questioned = true; // Forensics report pulls their full verified alibi
            Score += 10;

            res.Accepted = true;
            res.EliminatedIndex = target;
            res.EliminatedName = suspects[target].Name;
            res.FeedbackBanner = "FORENSICS LAB CLEARED " + suspects[target].Name.ToUpperInvariant() + " — 100% VERIFIED INNOCENT!";
            LastFeedback = res.FeedbackBanner;
            return res;
        }

        public CrimeAccuseResult AccuseSuspect(int suspectIndex)
        {
            var res = new CrimeAccuseResult();
            if (Phase == DetectivePhase.Briefing) BeginInvestigation();
            if (Phase != DetectivePhase.Investigating || suspectIndex < 0 || suspectIndex >= suspects.Length)
                return res;

            var accused = suspects[suspectIndex];
            var culprit = suspects[CulpritIndex];
            AccusedIndex = suspectIndex;
            SolvedCorrectly = accused.IsCulprit;
            Phase = DetectivePhase.AccusationClimax;

            if (SolvedCorrectly)
            {
                Score += 50;
                if (CluesFoundCount == 3 && SuspectsQuestionedCount >= 2)
                    Score += 25; // Thorough detective bonus!
            }
            else
            {
                Score = Math.Max(0, Score - 20);
            }

            res.Accepted = true;
            res.CorrectCulprit = SolvedCorrectly;
            res.AccusedIndex = suspectIndex;
            res.AccusedName = accused.Name;
            res.TrueCulpritName = culprit.Name;
            res.DetectiveAccuseLine = "Case closed! The footprint, motive, and alibi all point straight to YOU, " + accused.Name + "!";
            res.SuspectReactionLine = SolvedCorrectly ? accused.CaughtLine : accused.WronglyAccusedLine;
            res.FeedbackBanner = SolvedCorrectly
                ? "CASE SOLVED! " + accused.Name.ToUpperInvariant() + " CONFESSED TO " + CaseTitle.ToUpperInvariant() + "!"
                : "WRONG ACCUSATION! " + accused.Name + " was innocent — " + culprit.Name + " slipped away!";
            LastFeedback = res.FeedbackBanner;

            Outcome = BuildOutcome();
            res.Outcome = Outcome;
            return res;
        }

        public void CompleteClimax()
        {
            if (Phase == DetectivePhase.AccusationClimax)
                Phase = DetectivePhase.Finished;
        }

        public CrimeSceneOutcome ForceAbortAndFinish()
        {
            if (Outcome == null)
            {
                SolvedCorrectly = false;
                Outcome = BuildOutcome();
            }
            Phase = DetectivePhase.Finished;
            return Outcome;
        }

        CrimeSceneOutcome BuildOutcome()
        {
            string grade;
            int simoleons;
            short jobPerf;
            short logicXp;
            short charismaXp;
            short funDelta;
            short socialDelta;

            if (SolvedCorrectly && Score >= 110)
            {
                grade = "S";
                simoleons = 450;
                jobPerf = 15;
                logicXp = 125;
                charismaXp = 65;
                funDelta = 22;
                socialDelta = 14;
            }
            else if (SolvedCorrectly && Score >= 75)
            {
                grade = "A";
                simoleons = 320;
                jobPerf = 11;
                logicXp = 95;
                charismaXp = 45;
                funDelta = 15;
                socialDelta = 10;
            }
            else if (SolvedCorrectly)
            {
                grade = "B";
                simoleons = 200;
                jobPerf = 7;
                logicXp = 65;
                charismaXp = 25;
                funDelta = 8;
                socialDelta = 6;
            }
            else
            {
                grade = "C";
                simoleons = 90;
                jobPerf = 3;
                logicXp = 35;
                charismaXp = 10;
                funDelta = -4;
                socialDelta = -4;
            }

            string accusedName = AccusedIndex >= 0 && AccusedIndex < suspects.Length ? suspects[AccusedIndex].Name : "None";
            string culpritName = suspects[CulpritIndex].Name;

            string summary = SolvedCorrectly
                ? "Cracked \"" + CaseTitle + "\" — exposed " + culpritName + " with " + CluesFoundCount + "/3 clues & " + SuspectsQuestionedCount + "/3 interviews."
                : "Missed the true culprit (" + culpritName + ") on \"" + CaseTitle + "\" after accusing " + accusedName + ".";

            string memFact = SolvedCorrectly
                ? "Detective case solved: exposed " + culpritName + " in " + CaseTitle + "."
                : "Detective case note: investigated " + CaseTitle + " (wrong suspect accused).";

            return new CrimeSceneOutcome
            {
                DetectiveName = DetectiveName,
                CaseTitle = CaseTitle,
                VenueName = VenueName,
                AccusedName = accusedName,
                TrueCulpritName = culpritName,
                SolvedCorrectly = SolvedCorrectly,
                CluesFound = CluesFoundCount,
                SuspectsQuestioned = SuspectsQuestionedCount,
                UsedForensics = UsedForensics,
                FinalScore = Score,
                Grade = grade,
                Summary = summary,
                SuspectNames = suspects.Select(s => s.Name).ToList(),
                AccusedIndex = AccusedIndex,
                CulpritIndex = CulpritIndex,
                Reward = new CareerDispatchReward
                {
                    Grade = grade,
                    Summary = summary,
                    BonusSimoleons = simoleons,
                    JobPerformanceDelta = jobPerf,
                    LogicXpDelta = logicXp,
                    CharismaXpDelta = charismaXp,
                    EnergyDelta = -8,
                    FunDelta = funDelta,
                    SocialDelta = socialDelta,
                    MemoryFact = memFact,
                },
            };
        }
    }
}
