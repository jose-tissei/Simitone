using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    public enum StingTone
    {
        Gentle = 0,
        Playful = 1,
        Bold = 2,
    }

    public enum StingTemperament
    {
        GreedyCocky = 0,
        VainFlattered = 1,
        ParanoidJumpy = 2,
        StreetwiseBold = 3,
    }

    public enum StingEndingKind
    {
        None = 0,
        SuspectBusted = 1,
        CoverBlown = 2,
        Aborted = 3,
    }

    public sealed class StingLineOption
    {
        public StingTone Tone;
        public string Label = "";
        public string OfficerLine = "";
        public string SuspectReply = "";
        public string SlipUpClue = "";       // Recorded on the wiretap when this line lands cleanly
        public int BaseSuspicionDelta;       // Negative cools, positive spikes
        public int BaseConfessionDelta;      // Positive draws out incriminating slip-up
        public bool IsBestForTurn;
        public bool IsRiskyTrap;
    }

    public sealed class StingTurnScript
    {
        public int TurnNumber;               // 1..3
        public string StageTitle = "";
        public string SuspectPrompt = "";
        public string WiretapTell = "";      // Revealed by Check Wire / Charisma read
        public StingLineOption[] Options = new StingLineOption[3];
    }

    public sealed class StingCaseProfile
    {
        public string CaseId = "";
        public string CaseTitle = "";
        public string OperationCodename = "";
        public string BriefingSummary = "";
        public string SuspectBio = "";
        public StingTemperament Temperament;
        public string TemperamentLabel = "";
        public string TemperamentHint = "";
        public StingTurnScript[] Turns = new StingTurnScript[3];
    }

    public sealed class StingTranscriptEntry
    {
        public int TurnNumber;
        public StingTone Tone;
        public string OfficerLine = "";
        public string SuspectReply = "";
        public string SlipUpCaptured = "";
        public int SuspicionDelta;
        public int ConfessionDelta;
        public bool UsedLaya;
        public string LayaVerdict = "";
    }

    public sealed class StingEvent
    {
        public string Kind = "";             // "case_start", "dialogue_good", "dialogue_mixed", "dialogue_risky", "wire_check", "confession_ready", "bust_success", "cover_blown", "shift_complete"
        public string Detail = "";
        public StingTone Tone;
        public int Delta;
    }

    public sealed class UndercoverStingOutcome
    {
        public string Grade = "C";
        public StingEndingKind Ending;
        public string CaseTitle = "";
        public string SuspectName = "";
        public string InformantName = "";
        public int FinalSuspicion;
        public int FinalConfession;
        public int SlipUpsRecorded;
        public int TurnsTaken;
        public bool UsedLayaAnyTurn;
        public int Score;
        public int BonusSimoleons;
        public short JobPerformanceDelta;
        public short CharismaXpDelta;
        public short LogicXpDelta;
        public short EnergyDelta;
        public short FunDelta;
        public short SuspectDailyRelDelta;
        public short InformantDailyRelDelta;
        public string Summary = "";
        public string MemoryFact = "";
    }

    // Pure-C# state machine for Law Enforcement Level 5 — Vice Squad: "Undercover Sting".
    // Combines 4 handcrafted speakeasy sting cases, Charisma & Logic skill depth, Suspicion vs. Confession
    // tension, Wiretap Check cooldowns, and optional Laya AI reaction classification (with instant offline fallback).
    public sealed class UndercoverStingGame
    {
        public const int MaxTurns = 3;
        public const int StartSuspicion = 20;
        public const int MaxSuspicion = 100;
        public const int BustConfessionThreshold = 75;
        public const int WireCoolSuspicionDelta = -18;
        public const int WireSteadyConfessionBonus = 6;
        public const int LogicProfileUnlockLevel = 3;

        public static readonly string[] LayaStingCriteria =
        {
            "opens_up|suspect relaxes, trusts the undercover cover, and reveals incriminating details",
            "guarded|suspect stays cautious and non-committal without fully blowing cover",
            "suspicious|suspect senses a police trap, grows alarmed, and becomes hostile or defensive",
        };

        public const string LayaStingQuestion =
            "How does this criminal suspect react to the undercover officer's line at a speakeasy lounge?";

        readonly List<StingTranscriptEntry> transcript = new List<StingTranscriptEntry>();
        readonly List<string> recordedSlips = new List<string>();
        readonly List<StingEvent> eventQueue = new List<StingEvent>();

        public string OfficerName { get; private set; }
        public string SuspectName { get; private set; }
        public uint SuspectGuid { get; private set; }
        public string InformantName { get; private set; }
        public uint InformantGuid { get; private set; }
        public int CharismaSkill { get; private set; }
        public int LogicSkill { get; private set; }
        public int Seed { get; private set; }

        public StingCaseProfile CaseProfile { get; private set; }
        public bool IsBriefing { get; private set; }
        public bool IsFinished { get; private set; }
        public StingEndingKind Ending { get; private set; }

        public int CurrentTurnIndex { get; private set; }       // 0..2 (3 = all 3 turns played, ready for Bust)
        public int Suspicion { get; private set; }              // 0..100
        public int Confession { get; private set; }             // 0..100
        public int WireChargesRemaining { get; private set; }
        public int MaxWireCharges { get; private set; }
        public bool WireTellRevealedThisTurn { get; private set; }
        public bool LogicProfileUnlocked { get { return LogicSkill >= LogicProfileUnlockLevel; } }
        public bool CanFlashBadgeBust { get { return !IsBriefing && !IsFinished && (Confession >= BustConfessionThreshold || CurrentTurnIndex >= MaxTurns); } }

        public string LastOfficerBubble { get; private set; }
        public string LastSuspectBubble { get; private set; }
        public string LastStatusBanner { get; private set; }
        public bool UsedLayaAnyTurn { get; private set; }
        public string LastLayaVerdict { get; private set; }

        public IReadOnlyList<StingTranscriptEntry> Transcript { get { return transcript; } }
        public IReadOnlyList<string> RecordedSlips { get { return recordedSlips; } }

        public StingTurnScript CurrentTurn
        {
            get
            {
                if (CaseProfile == null || CaseProfile.Turns == null) return null;
                if (CurrentTurnIndex < 0 || CurrentTurnIndex >= CaseProfile.Turns.Length) return null;
                return CaseProfile.Turns[CurrentTurnIndex];
            }
        }

        public UndercoverStingGame(
            string officerName,
            int charismaSkillLevel,
            int logicSkillLevel,
            string suspectName = null,
            uint suspectGuid = 0,
            string informantName = null,
            uint informantGuid = 0,
            int seed = 42,
            bool startInBriefing = true)
        {
            OfficerName = string.IsNullOrWhiteSpace(officerName) ? "Officer" : officerName.Trim();
            CharismaSkill = Math.Max(0, Math.Min(10, charismaSkillLevel));
            LogicSkill = Math.Max(0, Math.Min(10, logicSkillLevel));
            Seed = seed;

            SuspectName = string.IsNullOrWhiteSpace(suspectName) ? "Slick Eddie" : suspectName.Trim();
            SuspectGuid = suspectGuid;
            InformantName = string.IsNullOrWhiteSpace(informantName) ? "Louie" : informantName.Trim();
            InformantGuid = informantGuid;

            var catalog = BuildCaseCatalog();
            int idx = Math.Abs(seed) % catalog.Length;
            CaseProfile = catalog[idx];

            IsBriefing = startInBriefing;
            IsFinished = false;
            Ending = StingEndingKind.None;
            CurrentTurnIndex = 0;
            Suspicion = StartSuspicion;
            Confession = 0;
            MaxWireCharges = CharismaSkill >= 3 ? 2 : 1;
            WireChargesRemaining = MaxWireCharges;
            WireTellRevealedThisTurn = false;

            LastOfficerBubble = "";
            LastSuspectBubble = CaseProfile.Turns[0].SuspectPrompt;
            LastStatusBanner = "Undercover cover active. Talk to " + SuspectName + " to draw out a confession.";
            LastLayaVerdict = "";
        }

        public List<StingEvent> DrainEvents()
        {
            var copy = new List<StingEvent>(eventQueue);
            eventQueue.Clear();
            return copy;
        }

        public void BeginShift()
        {
            if (!IsBriefing) return;
            IsBriefing = false;
            LastStatusBanner = "Wiretap live! Click " + SuspectName + " to open undercover dialogue, or check your wire at the bar.";
            eventQueue.Add(new StingEvent { Kind = "case_start", Detail = CaseProfile.CaseTitle });
        }

        // Optional Laya AI classification helper (safe, fast, returns null on offline/low-confidence).
        public static string TryClassifyWithLaya(
            LayaClient laya,
            StingCaseProfile profile,
            StingTurnScript turn,
            StingLineOption option)
        {
            if (laya == null || !laya.Available || profile == null || turn == null || option == null)
                return null;
            try
            {
                string context =
                    "Case: " + profile.CaseTitle + ". " +
                    "Suspect profile: " + profile.SuspectBio + " Temperament: " + profile.TemperamentLabel + ". " +
                    "Suspect just said: \"" + turn.SuspectPrompt + "\". " +
                    "Undercover officer replies (" + option.Tone + "): \"" + option.OfficerLine + "\".";
                return laya.ClassifyCustom(context, LayaStingQuestion, LayaStingCriteria);
            }
            catch
            {
                return null;
            }
        }

        public bool TryCheckWire(out string message)
        {
            message = "";
            if (IsBriefing || IsFinished)
            {
                message = "Shift not active.";
                return false;
            }
            if (WireChargesRemaining <= 0)
            {
                message = "No wire checks left this shift.";
                return false;
            }

            WireChargesRemaining--;
            int prevSusp = Suspicion;
            Suspicion = Math.Max(0, Suspicion + WireCoolSuspicionDelta);
            int cooled = prevSusp - Suspicion;
            Confession = Math.Min(100, Confession + WireSteadyConfessionBonus);
            WireTellRevealedThisTurn = true;

            var turn = CurrentTurn;
            string tell = turn != null ? turn.WiretapTell : "Tape has enough to bust them now!";
            LastOfficerBubble = "*casually adjusts collar wire & sips drink*";
            LastStatusBanner = "WIRE CHECK: Suspicion -" + cooled + "% | " + tell;
            message = LastStatusBanner;

            eventQueue.Add(new StingEvent
            {
                Kind = "wire_check",
                Detail = tell,
                Delta = -cooled,
            });

            if (Confession >= BustConfessionThreshold && CurrentTurnIndex < MaxTurns)
            {
                eventQueue.Add(new StingEvent
                {
                    Kind = "confession_ready",
                    Detail = "Enough incriminating proof on tape — flash your badge to bust!",
                });
            }
            return true;
        }

        public StingTranscriptEntry ChooseTone(StingTone tone, string layaVerdict = null)
        {
            if (IsBriefing) BeginShift();
            if (IsFinished) return null;

            var turn = CurrentTurn;
            if (turn == null) return null;

            StingLineOption opt = null;
            for (int i = 0; i < turn.Options.Length; i++)
            {
                if (turn.Options[i].Tone == tone)
                {
                    opt = turn.Options[i];
                    break;
                }
            }
            if (opt == null) opt = turn.Options[0];

            // Compute Charisma modifiers: smooth Vice officers blunt suspicion spikes and pull more confession!
            int suspDelta = opt.BaseSuspicionDelta;
            int confDelta = opt.BaseConfessionDelta;

            int charismaSuspMitigation = CharismaSkill / 2; // 0..5
            int charismaConfBoost = (CharismaSkill / 2) * 2; // 0..10

            if (suspDelta > 0)
                suspDelta = Math.Max(2, suspDelta - charismaSuspMitigation);
            else if (suspDelta < 0)
                suspDelta -= (CharismaSkill >= 6 ? 3 : 0);

            if (confDelta > 0)
                confDelta += charismaConfBoost;

            // Optional Laya AI modifier
            bool usedLaya = false;
            string cleanVerdict = "";
            if (!string.IsNullOrWhiteSpace(layaVerdict))
            {
                cleanVerdict = layaVerdict.Trim().ToLowerInvariant();
                if (cleanVerdict == "opens_up")
                {
                    usedLaya = true;
                    suspDelta -= 6;
                    confDelta += 10;
                }
                else if (cleanVerdict == "guarded")
                {
                    usedLaya = true;
                }
                else if (cleanVerdict == "suspicious")
                {
                    usedLaya = true;
                    suspDelta += 8;
                    confDelta = Math.Max(0, confDelta - 6);
                }
            }

            if (usedLaya)
            {
                UsedLayaAnyTurn = true;
                LastLayaVerdict = cleanVerdict;
            }

            Suspicion = Math.Max(0, Math.Min(MaxSuspicion, Suspicion + suspDelta));
            Confession = Math.Max(0, Math.Min(100, Confession + confDelta));

            string slip = "";
            if (confDelta >= 22 && !string.IsNullOrWhiteSpace(opt.SlipUpClue))
            {
                slip = opt.SlipUpClue;
                if (!recordedSlips.Contains(slip))
                    recordedSlips.Add(slip);
            }

            var entry = new StingTranscriptEntry
            {
                TurnNumber = turn.TurnNumber,
                Tone = tone,
                OfficerLine = opt.OfficerLine,
                SuspectReply = opt.SuspectReply,
                SlipUpCaptured = slip,
                SuspicionDelta = suspDelta,
                ConfessionDelta = confDelta,
                UsedLaya = usedLaya,
                LayaVerdict = cleanVerdict,
            };
            transcript.Add(entry);

            LastOfficerBubble = opt.OfficerLine;
            LastSuspectBubble = opt.SuspectReply;

            // Emit reaction audio/animation event
            if (suspDelta >= 18)
            {
                eventQueue.Add(new StingEvent { Kind = "dialogue_risky", Tone = tone, Delta = suspDelta, Detail = opt.SuspectReply });
            }
            else if (confDelta >= 26)
            {
                eventQueue.Add(new StingEvent { Kind = "dialogue_good", Tone = tone, Delta = confDelta, Detail = slip });
            }
            else
            {
                eventQueue.Add(new StingEvent { Kind = "dialogue_mixed", Tone = tone, Delta = confDelta, Detail = opt.SuspectReply });
            }

            // Check blown cover!
            if (Suspicion >= MaxSuspicion)
            {
                IsFinished = true;
                Ending = StingEndingKind.CoverBlown;
                LastStatusBanner = "COVER BLOWN! " + SuspectName + " spotted the wire and bolted out the back door!";
                eventQueue.Add(new StingEvent { Kind = "cover_blown", Detail = SuspectName + " fled the lounge!" });
                eventQueue.Add(new StingEvent { Kind = "shift_complete", Detail = "CoverBlown" });
                return entry;
            }

            CurrentTurnIndex++;
            WireTellRevealedThisTurn = false;

            if (CanFlashBadgeBust)
            {
                LastStatusBanner = "CONFESSION ON TAPE (" + Confession + "%)! Click " + SuspectName + " or press [B] to FLASH BADGE & BUST!";
                eventQueue.Add(new StingEvent { Kind = "confession_ready", Detail = LastStatusBanner });
            }
            else
            {
                LastStatusBanner = "Turn " + (CurrentTurnIndex + 1) + "/" + MaxTurns + " — " + SuspectName + " is talking. Keep Suspicion under 100%!";
            }

            return entry;
        }

        public bool TryFlashBadgeBust(out string message)
        {
            message = "";
            if (IsBriefing || IsFinished)
            {
                message = "Cannot bust right now.";
                return false;
            }
            if (!CanFlashBadgeBust)
            {
                message = "Need " + BustConfessionThreshold + "% Confession (currently " + Confession + "%) or finish Turn 3 before flashing your badge!";
                return false;
            }

            IsFinished = true;
            if (Confession >= 50)
            {
                Ending = StingEndingKind.SuspectBusted;
                LastOfficerBubble = "SimCity PD Vice Squad! Hands up — we got the whole thing on tape!";
                LastSuspectBubble = "No way... you're a cop?! Alright, alright, don't shoot — I'm coming quietly!";
                LastStatusBanner = "SUSPECT BUSTED! " + SuspectName + " caught red-handed on the wire!";
                message = LastStatusBanner;
                eventQueue.Add(new StingEvent { Kind = "bust_success", Detail = SuspectName + " arrested!" });
            }
            else
            {
                // Tried to bust after Turn 3 with weak evidence -> suspect walks on technicality
                Ending = StingEndingKind.CoverBlown;
                LastOfficerBubble = "SimCity PD Vice Squad — freeze!";
                LastSuspectBubble = "You got nothing on that tape, officer! Talk to my lawyer!";
                LastStatusBanner = "WEAK EVIDENCE — " + SuspectName + " walked out before backup could book them!";
                message = LastStatusBanner;
                eventQueue.Add(new StingEvent { Kind = "cover_blown", Detail = "Insufficient confession evidence." });
            }

            eventQueue.Add(new StingEvent { Kind = "shift_complete", Detail = Ending.ToString() });
            return true;
        }

        public void Abort()
        {
            if (IsFinished) return;
            IsFinished = true;
            Ending = StingEndingKind.Aborted;
            eventQueue.Add(new StingEvent { Kind = "shift_complete", Detail = "Aborted" });
        }

        public UndercoverStingOutcome ComputeOutcome()
        {
            bool busted = Ending == StingEndingKind.SuspectBusted;
            int score = 0;
            if (busted)
            {
                score = 300 + (Confession * 4) + ((100 - Suspicion) * 3) + (recordedSlips.Count * 45);
                if (Suspicion <= 40) score += 60;
            }
            else
            {
                score = Math.Max(50, (Confession * 2) + (recordedSlips.Count * 25));
            }

            string grade;
            int cash;
            short perf;
            short chaXp;
            short logicXp;
            short funDelta;
            short energyDelta = -10;
            short suspectRel = 0;
            short informantRel = 0;

            if (busted && (Confession >= 85 || score >= 820) && Suspicion <= 55)
            {
                grade = "S";
                cash = 220;
                perf = 32;
                chaXp = 85;
                logicXp = 35;
                funDelta = 16;
                suspectRel = -12;
                informantRel = 8;
            }
            else if (busted && Confession >= 65 && Suspicion <= 80)
            {
                grade = "A";
                cash = 155;
                perf = 22;
                chaXp = 60;
                logicXp = 20;
                funDelta = 10;
                suspectRel = -10;
                informantRel = 5;
            }
            else if (busted)
            {
                grade = "B";
                cash = 95;
                perf = 12;
                chaXp = 35;
                logicXp = 10;
                funDelta = 4;
                suspectRel = -8;
                informantRel = 2;
            }
            else
            {
                grade = "C";
                cash = 40;
                perf = -8;
                chaXp = 15;
                logicXp = 0;
                funDelta = -6;
                suspectRel = -6;
                informantRel = 0;
            }

            string summary = busted
                ? ("Busted " + SuspectName + " on \"" + CaseProfile.CaseTitle + "\" with " + Confession + "% confession and " + Suspicion + "% suspicion.")
                : ("Cover blown on \"" + CaseProfile.CaseTitle + "\" — " + SuspectName + " slipped away.");

            string memoryFact = busted
                ? (OfficerName + " pulled off an " + grade + "-Rank Vice Squad undercover sting, busting " + SuspectName + " in " + CaseProfile.CaseTitle + ".")
                : (OfficerName + " went undercover on " + CaseProfile.CaseTitle + ", but " + SuspectName + " spotted the wire and escaped.");

            return new UndercoverStingOutcome
            {
                Grade = grade,
                Ending = Ending,
                CaseTitle = CaseProfile.CaseTitle,
                SuspectName = SuspectName,
                InformantName = InformantName,
                FinalSuspicion = Suspicion,
                FinalConfession = Confession,
                SlipUpsRecorded = recordedSlips.Count,
                TurnsTaken = transcript.Count,
                UsedLayaAnyTurn = UsedLayaAnyTurn,
                Score = score,
                BonusSimoleons = cash,
                JobPerformanceDelta = perf,
                CharismaXpDelta = chaXp,
                LogicXpDelta = logicXp,
                EnergyDelta = energyDelta,
                FunDelta = funDelta,
                SuspectDailyRelDelta = suspectRel,
                InformantDailyRelDelta = informantRel,
                Summary = summary,
                MemoryFact = memoryFact,
            };
        }

        public static StingCaseProfile[] BuildCaseCatalog()
        {
            return new[]
            {
                // CASE 0: Greedy / Cocky — Playful -> Bold -> Gentle
                new StingCaseProfile
                {
                    CaseId = "counterfeit_ring",
                    CaseTitle = "The Counterfeit Simoleon Ring",
                    OperationCodename = "OP: VELVET INK",
                    BriefingSummary = "Fresh §100 bills with smudged llama watermarks flooded Downtown. Meet the printing ring's broker in the back lounge and get the press location on tape.",
                    SuspectBio = "Cocky money launderer who loves bragging about clever hustles and hates timid amateurs.",
                    Temperament = StingTemperament.GreedyCocky,
                    TemperamentLabel = "Greedy & Cocky",
                    TemperamentHint = "Responds to witty banter and bold high-roller talk; dislikes nervous small talk early.",
                    Turns = new[]
                    {
                        new StingTurnScript
                        {
                            TurnNumber = 1,
                            StageTitle = "OPENING FEEL-OUT",
                            SuspectPrompt = "You're late. Louie said you've got buyers for 'fresh paper' — prove you're not a rookie.",
                            WiretapTell = "WIRETAP READ: Ego is sky-high — a PLAYFUL insider joke breaks the ice fast.",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Apologize politely and ask nicely",
                                    OfficerLine = "Sorry for the wait! I'm just hoping we can work out a quiet, safe arrangement.",
                                    SuspectReply = "Safe? Nobody in this lounge talks like an insurance clerk... who sent you?",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 16,
                                    BaseConfessionDelta = 12,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Joke about the smudged llama bills",
                                    OfficerLine = "Relax — traffic was slower than your old ink press. Got any crisp green that passes the UV lamp?",
                                    SuspectReply = "Hah! Okay, you've got nerve. Our new basement plates pass every machine on Sim Lane!",
                                    SlipUpClue = "Admitted running basement counterfeit plates",
                                    BaseSuspicionDelta = -4,
                                    BaseConfessionDelta = 34,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Demand to see the plates immediately",
                                    OfficerLine = "Skip the drinks — show me the printing press address right now or I walk.",
                                    SuspectReply = "Whoa, slow down hotshot. Asking for an address in five seconds smells like badge work.",
                                    SlipUpClue = "Hinted press is nearby",
                                    BaseSuspicionDelta = 24,
                                    BaseConfessionDelta = 14,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 2,
                            StageTitle = "PRESS THE LEAD",
                            SuspectPrompt = "Maybe I run the plates, maybe I don't. Why should I trust you with a §50,000 drop?",
                            WiretapTell = "WIRETAP READ: Challenge their pride with a BOLD high-roller wager!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Promise you're completely trustworthy",
                                    OfficerLine = "You have my word — I never rat on business partners, honest.",
                                    SuspectReply = "'Never rat'? Funny word to bring up out of nowhere...",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 20,
                                    BaseConfessionDelta = 10,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Tease them about small-time bills",
                                    OfficerLine = "Fifty grand? Come on, I thought you were the kingpin of Downtown, not running lemonade change.",
                                    SuspectReply = "Tch, watch your tongue. We moved triple that out of Warehouse 4 last Tuesday!",
                                    SlipUpClue = "Named Warehouse 4 drop-off",
                                    BaseSuspicionDelta = 6,
                                    BaseConfessionDelta = 28,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Flex big buyer clout & name the stakes",
                                    OfficerLine = "Because my syndicate buys in crates, not envelopes. Tell me where the ink press runs so my trucks roll tonight.",
                                    SuspectReply = "Now you're talking! Dockside Printing, Pier 9 — we run three shifts till 4 AM!",
                                    SlipUpClue = "Confessed exact press location: Pier 9",
                                    BaseSuspicionDelta = 2,
                                    BaseConfessionDelta = 36,
                                    IsBestForTurn = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 3,
                            StageTitle = "SEAL THE CONFESSION",
                            SuspectPrompt = "Hold on... why is your collar blinking under this lamp? Tell me straight — are we good?",
                            WiretapTell = "WIRETAP READ: They're spooked! A GENTLE calm tone disarms them and locks the final name!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Stay calm & flatter their carefulness",
                                    OfficerLine = "Just a cheap watch reflection. Honestly, I respect how careful you are — who signs off on the Pier 9 crates?",
                                    SuspectReply = "Phew... alright, I like your style. It's all under my own signature — I personally engraved every plate!",
                                    SlipUpClue = "Full confession: personally engraved & signed plates",
                                    BaseSuspicionDelta = -8,
                                    BaseConfessionDelta = 35,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Laugh it off as nightclub neon",
                                    OfficerLine = "Relax, it's just the jukebox glare! Unless you're scared of a little disco light?",
                                    SuspectReply = "Don't laugh at me... fine, yeah, it's my press, just stop staring at my jacket!",
                                    SlipUpClue = "Confirmed ownership of the press",
                                    BaseSuspicionDelta = 10,
                                    BaseConfessionDelta = 24,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Snap back and accuse them of cold feet",
                                    OfficerLine = "Don't stall me now — admit you're the boss or this deal is dead!",
                                    SuspectReply = "Nobody pushes me like a precinct detective — YOU'RE WEARING A WIRE!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 38,
                                    BaseConfessionDelta = 8,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                    },
                },

                // CASE 1: Vain / Flattered — Gentle -> Playful -> Bold
                new StingCaseProfile
                {
                    CaseId = "fenced_antiques",
                    CaseTitle = "The Fenced Downtown Antiques",
                    OperationCodename = "OP: GILDED FRAME",
                    BriefingSummary = "Three priceless Goth Manor paintings vanished overnight. Pose as an art collector at the speakeasy and coax the fence into admitting possession.",
                    SuspectBio = "Vain high-society art fence who craves admiration and bristles at blunt interrogation.",
                    Temperament = StingTemperament.VainFlattered,
                    TemperamentLabel = "Vain & Flattered",
                    TemperamentHint = "Melts under refined compliments and witty charm; hates crude ultimatums early.",
                    Turns = new[]
                    {
                        new StingTurnScript
                        {
                            TurnNumber = 1,
                            StageTitle = "OPENING FEEL-OUT",
                            SuspectPrompt = "Darling, this booth is reserved for discerning patrons. Do you even know a Baroque frame from plywood?",
                            WiretapTell = "WIRETAP READ: Stroke their ego with a GENTLE, cultured compliment!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Praise their legendary curatorial eye",
                                    OfficerLine = "Everyone in SimCity whispers that only one collector has the taste to acquire the Goth triptych.",
                                    SuspectReply = "Oh, finally someone with refinement! Yes, those three canvases look simply divine in my private vault.",
                                    SlipUpClue = "Admitted holding the stolen Goth canvases",
                                    BaseSuspicionDelta = -6,
                                    BaseConfessionDelta = 36,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Wink about 'midnight museum discounts'",
                                    OfficerLine = "I know a bargain when the museum alarm takes a coffee break!",
                                    SuspectReply = "Hmph, how gauche... though I suppose you have a tiny bit of wit.",
                                    SlipUpClue = "Hinted at alarm bypass",
                                    BaseSuspicionDelta = 8,
                                    BaseConfessionDelta = 20,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Grill them on where they hid the loot",
                                    OfficerLine = "Cut the act — where did you stash the stolen paintings last night?",
                                    SuspectReply = "'Stolen'?! Excuse me, are you auditioning for Vice Squad?!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 28,
                                    BaseConfessionDelta = 8,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 2,
                            StageTitle = "PRESS THE LEAD",
                            SuspectPrompt = "Suppose I did 'liberate' a masterpiece or two. How do I know you can afford my clientele fee?",
                            WiretapTell = "WIRETAP READ: Keep it sparkling — a PLAYFUL high-society tease makes them boast!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Politely promise a cashier's check",
                                    OfficerLine = "I brought a polite bank note if you'd kindly share your delivery method.",
                                    SuspectReply = "A bank note? With paper trails?! How terribly suspicious...",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 22,
                                    BaseConfessionDelta = 10,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Tease that a real mastermind handles delivery",
                                    OfficerLine = "Please — I'll pay double if you tell me how you slipped past the manor lasers without smudging your shoes!",
                                    SuspectReply = "Hah! Child's play! I bribed the night courier and wheeled them out in a catering crate at 2 AM!",
                                    SlipUpClue = "Confessed bribing courier & catering-crate heist",
                                    BaseSuspicionDelta = -2,
                                    BaseConfessionDelta = 34,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Slam cash on the table and demand receipts",
                                    OfficerLine = "Name your price and sign over the manifest right here.",
                                    SuspectReply = "Sign a manifest? Nobody in the underground signs receipts!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 24,
                                    BaseConfessionDelta = 14,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 3,
                            StageTitle = "SEAL THE CONFESSION",
                            SuspectPrompt = "Wait a moment... you ask awfully specific questions for a collector. Prove you're ready to buy!",
                            WiretapTell = "WIRETAP READ: Close the deal with BOLD decisive collector energy!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Back off shyly and apologize",
                                    OfficerLine = "Oh, I didn't mean to pry! Let's forget the whole painting thing.",
                                    SuspectReply = "Wait — why are you suddenly nervous? Something's wrong here...",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 26,
                                    BaseConfessionDelta = 8,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Toast to SimCity's finest art thief",
                                    OfficerLine = "To SimCity's boldest curator — tell me the locker number and I'll toast your genius!",
                                    SuspectReply = "Flatterer! Locker 14B at Downtown Storage — all three Goth frames are sitting pretty!",
                                    SlipUpClue = "Revealed Locker 14B stash",
                                    BaseSuspicionDelta = 4,
                                    BaseConfessionDelta = 28,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Make a decisive final bid for all three",
                                    OfficerLine = "I'm wiring §80,000 right now — confirm all three Goth Manor pieces are in your locker and we shake.",
                                    SuspectReply = "Done! All three stolen Goth pieces are locked in my vault 14B — I lifted them myself and they're yours!",
                                    SlipUpClue = "Full confession: stole & stashed all 3 paintings in 14B",
                                    BaseSuspicionDelta = -4,
                                    BaseConfessionDelta = 38,
                                    IsBestForTurn = true,
                                },
                            },
                        },
                    },
                },

                // CASE 2: Paranoid / Jumpy — Gentle -> Gentle -> Playful
                new StingCaseProfile
                {
                    CaseId = "midnight_llamas",
                    CaseTitle = "Midnight Llama Smuggling",
                    OperationCodename = "OP: WOOL OVER EYES",
                    BriefingSummary = "Exotic golden show-llamas are being smuggled through SimCity Harbor after midnight. Calm a jittery lookout at the speakeasy until they spill the harbor gate code.",
                    SuspectBio = "Ultra-paranoid harbor lookout who panics at aggressive questioning and trusts soft-spoken allies.",
                    Temperament = StingTemperament.ParanoidJumpy,
                    TemperamentLabel = "Paranoid & Jumpy",
                    TemperamentHint = "Needs GENTLE reassurance early; BOLD pressure spikes suspicion fast!",
                    Turns = new[]
                    {
                        new StingTurnScript
                        {
                            TurnNumber = 1,
                            StageTitle = "OPENING FEEL-OUT",
                            SuspectPrompt = "Keep your voice down! Did you see that sedan parked outside? Are we being watched?!",
                            WiretapTell = "WIRETAP READ: Keep your voice low — a GENTLE calming line drops their guard!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Lower voice & reassure them smoothly",
                                    OfficerLine = "Easy, friend — that's just a late cab. Take a breath; I'm just here to help move the midnight wool shipment safely.",
                                    SuspectReply = "Okay... okay, good. My nerves are shot. Yeah, the golden llamas arrive at Gate 7 at 1 AM.",
                                    SlipUpClue = "Revealed Gate 7 llama smuggling drop at 1 AM",
                                    BaseSuspicionDelta = -8,
                                    BaseConfessionDelta = 35,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Crack a joke about police cruisers",
                                    OfficerLine = "Relax, if Vice Squad were here, they'd already have ordered a double espresso!",
                                    SuspectReply = "Why are you even saying 'Vice Squad' out loud?! Shhh!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 16,
                                    BaseConfessionDelta = 14,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Tell them to quit whining and talk",
                                    OfficerLine = "Stop sweating and give me the harbor manifest before I lose my patience.",
                                    SuspectReply = "You sound just like an interrogator! Who are you really?!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 32,
                                    BaseConfessionDelta = 8,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 2,
                            StageTitle = "PRESS THE LEAD",
                            SuspectPrompt = "I shouldn't have said Gate 7... if the boss finds out I blabbed about the bypass code, I'm toast.",
                            WiretapTell = "WIRETAP READ: Stay GENTLE and empathetic — they're one nudge from spilling the code!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Sympathize & offer to handle the gate",
                                    OfficerLine = "You're carrying way too much stress alone. Give me the gate code and I'll handle the dock patrol myself.",
                                    SuspectReply = "You'd really cover for me? Bless you... it's 4491, and I disabled the harbor floodlights myself.",
                                    SlipUpClue = "Confessed disabling floodlights & gave code 4491",
                                    BaseSuspicionDelta = -4,
                                    BaseConfessionDelta = 36,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Joke that llamas can't snitch",
                                    OfficerLine = "Hey, at least llamas don't testify in court — what's the keypad combo?",
                                    SuspectReply = "Heh... okay, fair point. It's 4491, just don't tell anyone I told you!",
                                    SlipUpClue = "Shared harbor keypad combo 4491",
                                    BaseSuspicionDelta = 6,
                                    BaseConfessionDelta = 26,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Threaten to cut them out of the payout",
                                    OfficerLine = "Hand over the code right now or you get zero Simoleons tonight.",
                                    SuspectReply = "Wait — my boss never talks like that! Are you setting me up?!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 30,
                                    BaseConfessionDelta = 10,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 3,
                            StageTitle = "SEAL THE CONFESSION",
                            SuspectPrompt = "Wait... you didn't touch your drink. Why are you so calm in here?",
                            WiretapTell = "WIRETAP READ: Lighten the mood with a PLAYFUL quip to seal the full confession!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Reassure them softly once more",
                                    OfficerLine = "Just pacing myself for the harbor run. Tell me who owns the smuggling boat so I don't greet the wrong captain.",
                                    SuspectReply = "Right, right — it's my cousin's tugboat, the 'Lucky Fleece', registered under my own fake alias.",
                                    SlipUpClue = "Named smuggling tugboat & fake alias",
                                    BaseSuspicionDelta = 0,
                                    BaseConfessionDelta = 28,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Wink that you never drink before herding llamas",
                                    OfficerLine = "Rule number one of midnight llama smuggling: stay sharp or they spit in your espresso!",
                                    SuspectReply = "Hahaha! True, the golden prize one bit me while I was loading all six crates onto my boat!",
                                    SlipUpClue = "Full confession: personally loaded 6 stolen llamas onto boat",
                                    BaseSuspicionDelta = -8,
                                    BaseConfessionDelta = 36,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Bark back that they're wasting time",
                                    OfficerLine = "Stop analyzing my glass and tell me where the llamas are right now!",
                                    SuspectReply = "That's a cop's voice! I KNEW IT!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 36,
                                    BaseConfessionDelta = 6,
                                    IsRiskyTrap = true,
                                },
                            },
                        },
                    },
                },

                // CASE 3: Streetwise / Bold — Playful -> Bold -> Bold
                new StingCaseProfile
                {
                    CaseId = "rigged_arcade",
                    CaseTitle = "The Rigged Arcade Jackpot",
                    OperationCodename = "OP: QUARTER DROP",
                    BriefingSummary = "Downtown's prize arcade machines have been rigged with magnetic cheat chips skimming thousands of Simoleons. Corner the fixer in the lounge and get the chip confession on wire.",
                    SuspectBio = "Tough streetwise fixer who respects brass nerve and laughs off polite small talk.",
                    Temperament = StingTemperament.StreetwiseBold,
                    TemperamentLabel = "Streetwise & Tough",
                    TemperamentHint = "Respects Playful street banter and Bold directness; smells weakness on Gentle lines.",
                    Turns = new[]
                    {
                        new StingTurnScript
                        {
                            TurnNumber = 1,
                            StageTitle = "OPENING FEEL-OUT",
                            SuspectPrompt = "You're sitting in my favorite booth, pal. Give me one reason not to toss you onto Sim Lane.",
                            WiretapTell = "WIRETAP READ: Don't flinch — a PLAYFUL streetwise comeback earns instant respect!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Apologize and offer to move seats",
                                    OfficerLine = "Oh, terribly sorry! I didn't realize this table was spoken for.",
                                    SuspectReply = "What are you, a hall monitor? Nobody in this lounge apologizes like that.",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 20,
                                    BaseConfessionDelta = 10,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Smirk about magnetic claw machines",
                                    OfficerLine = "Relax, boss — I'm here because your arcade cabinets pay out slower than a rigged slot! Need a silent partner?",
                                    SuspectReply = "Heh, sharp eyes! Yeah, my custom bypass chips skim 60% right off the top!",
                                    SlipUpClue = "Admitted installing 60% skim bypass chips",
                                    BaseSuspicionDelta = -6,
                                    BaseConfessionDelta = 35,
                                    IsBestForTurn = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Stare them down and demand a cut",
                                    OfficerLine = "Sit down and listen — I'm taking 30% of your arcade skim starting tonight.",
                                    SuspectReply = "Bold move, kid... a little too bold. You sure you're not working for the precinct?",
                                    SlipUpClue = "Implied arcade skim exists",
                                    BaseSuspicionDelta = 14,
                                    BaseConfessionDelta = 22,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 2,
                            StageTitle = "PRESS THE LEAD",
                            SuspectPrompt = "Alright, you talk tough. If you want in on the chip business, prove you understand the risk.",
                            WiretapTell = "WIRETAP READ: Meet strength with strength — go BOLD and talk hardware!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Say you hope nobody gets hurt",
                                    OfficerLine = "I just want to make sure the arcade kids aren't too upset by the losses.",
                                    SuspectReply = "'Upset kids'?! Are you a youth counselor or a cop?!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 28,
                                    BaseConfessionDelta = 6,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Joke about soldering at midnight",
                                    OfficerLine = "Hey, I've soldered hotter boards than this — how many cabinets did you rig?",
                                    SuspectReply = "Twelve machines across Downtown Plaza — took me one night with a master key!",
                                    SlipUpClue = "Confessed rigging 12 Downtown Plaza cabinets",
                                    BaseSuspicionDelta = 4,
                                    BaseConfessionDelta = 28,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Call out the skim numbers directly",
                                    OfficerLine = "I know the numbers: twelve rigged cabinets pulling §4,000 a week. Tell me where the skimmed cash box sits.",
                                    SuspectReply = "Whoa, you did your homework! Back utility closet behind Pinball Row — I empty it every Friday myself!",
                                    SlipUpClue = "Confessed hiding skimmed cash behind Pinball Row",
                                    BaseSuspicionDelta = -2,
                                    BaseConfessionDelta = 36,
                                    IsBestForTurn = true,
                                },
                            },
                        },
                        new StingTurnScript
                        {
                            TurnNumber = 3,
                            StageTitle = "SEAL THE CONFESSION",
                            SuspectPrompt = "Hold up — Louie just gave you a weird look from the bar. Give me the straight truth right now!",
                            WiretapTell = "WIRETAP READ: Don't blink! A BOLD alpha power-play seals their signature confession!",
                            Options = new[]
                            {
                                new StingLineOption
                                {
                                    Tone = StingTone.Gentle,
                                    Label = "Stammer and try to change the subject",
                                    OfficerLine = "Louie? Oh, I barely know him — lovely weather tonight, isn't it?",
                                    SuspectReply = "Weather?! Nobody talks weather in a sting! YOU'RE A COP!",
                                    SlipUpClue = "",
                                    BaseSuspicionDelta = 34,
                                    BaseConfessionDelta = 6,
                                    IsRiskyTrap = true,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Playful,
                                    Label = "Laugh that Louie owes you a drink",
                                    OfficerLine = "Louie's just mad I beat his high score! Come on, confirm you programmed the cheat chips and let's shake.",
                                    SuspectReply = "Haha, classic Louie. Sure, I wired every single cheat chip myself — deal?",
                                    SlipUpClue = "Confirmed wiring every cheat chip",
                                    BaseSuspicionDelta = 6,
                                    BaseConfessionDelta = 26,
                                },
                                new StingLineOption
                                {
                                    Tone = StingTone.Bold,
                                    Label = "Lock eyes and clinch the final admission",
                                    OfficerLine = "Louie's jealous I'm cutting a deal with the real mastermind. Say it plain — those twelve rigged machines are all yours.",
                                    SuspectReply = "Damn right they're mine! I built the cheat chips, cracked the locks, and pocketed every last Simoleon!",
                                    SlipUpClue = "Full confession: built chips, cracked locks & pocketed cash",
                                    BaseSuspicionDelta = -6,
                                    BaseConfessionDelta = 38,
                                    IsBestForTurn = true,
                                },
                            },
                        },
                    },
                },
            };
        }
    }
}
