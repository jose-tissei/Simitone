using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public enum DispatchCallChannel
    {
        Phone911 = 0,
        WalkIn = 1
    }

    public enum DispatchRouteAction
    {
        None = 0,
        DispatchPatrol = 1,
        DispatchFire = 2,
        CommunityAid = 3,
        DismissCall = 4
    }

    public enum DispatchCaseRating
    {
        Pending = 0,
        Perfect = 1,
        Good = 2,
        WrongUnit = 3,
        Missed = 4
    }

    public enum DeskSergeantGesture
    {
        IdleWatch = 0,
        AnswerPhone = 1,
        RadioDispatch = 2,
        WaveDismiss = 3,
        CalmTalk = 4
    }

    public sealed class DispatchCallerInfo
    {
        public string Name = "SimCity Resident";
        public short NeighborId;
        public uint OutfitGuid;
        public string Address = "12 Sim Lane";
    }

    public sealed class DispatchCaseTemplate
    {
        public string Id;
        public DispatchCallChannel Channel;
        public DispatchRouteAction CorrectAction;
        public string SubjectTitle;
        public string TranscriptLine1;
        public string TranscriptLine2;
        public string LogicClue;
        public string GoodSergeantLine;
        public string WrongSergeantLine;
        public float BaseUrgencySec;
        public bool IsPrank;
    }

    public sealed class DispatchActiveCase
    {
        public int CaseIndex;
        public int SlotNumber; // 0..2 (0-1 = 911 Phone Lines, 2 = Walk-In Counter)
        public DispatchCallChannel Channel;
        public DispatchRouteAction CorrectAction;
        public DispatchRouteAction ChosenAction;
        public DispatchCaseRating Rating;

        public string CallerName;
        public short CallerNeighborId;
        public uint CallerOutfitGuid;
        public string Address;
        public string SubjectTitle;
        public string TranscriptLine1;
        public string TranscriptLine2;
        public string LogicClue;
        public string GoodSergeantLine;
        public string WrongSergeantLine;
        public bool IsPrank;

        public float TotalUrgencySec;
        public float RemainingUrgencySec;
        public bool CalmedOnce;
        public bool IsActive;
        public bool IsResolved;

        // 3D Walk-in choreography progress (0 = precinct door, 1 = standing at front counter)
        public float WalkInApproach;
        public bool WalkInLeaving;
        public float WalkInLeaveProgress;
        public bool WalkInHappyLeave;

        public float UrgencyFraction
        {
            get { return TotalUrgencySec > 0.01f ? Math.Max(0f, Math.Min(1f, RemainingUrgencySec / TotalUrgencySec)) : 0f; }
        }
    }

    public sealed class DispatchNeighborDelta
    {
        public string SimName;
        public short NeighborId;
        public int DailyDelta;
        public string MemoryFact;
    }

    public sealed class DispatchTriageOutcome
    {
        public string Grade = "C";
        public int Score;
        public int TotalCases;
        public int PerfectCount;
        public int GoodCount;
        public int WrongCount;
        public int MissedCount;
        public int WalkInsHandled;
        public int PranksBlocked;
        public string Summary = "";
        public CareerDispatchReward Reward = new CareerDispatchReward();
        public List<DispatchNeighborDelta> RelationshipDeltas = new List<DispatchNeighborDelta>();
    }

    public sealed class DispatchTriageEvent
    {
        public string Kind; // "call_arrived", "walkin_arrived", "routed_good", "routed_wrong", "calmed", "missed", "unit_busy"
        public DispatchRouteAction Action;
        public DispatchCallChannel Channel;
        public string Message;
    }

    // Pure-C# state machine for Law Enforcement Level 4 ("Desk Sergeant"):
    // 911 Dispatch Triage — 3D Precinct Front Desk with handcrafted 911 phone lines AND live 3D walk-in townies.
    public sealed class DispatchTriageGame
    {
        public const int DefaultTotalCases = 6;
        public const int MaxPatrolUnits = 2;
        public const int MaxFireUnits = 1;
        public const int MaxCommunityUnits = 2;

        const float PatrolCooldownSec = 7.5f;
        const float FireCooldownSec = 8.5f;
        const float CommunityCooldownSec = 6.0f;

        static readonly DispatchCaseTemplate[] HandcraftedBank =
        {
            // ---- PATROL (Real Crimes / Burglaries / Intruders) ----
            new DispatchCaseTemplate
            {
                Id = "phone_burglar_piano",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.DispatchPatrol,
                SubjectTitle = "Masked Burglar Inside!",
                TranscriptLine1 = "Someone in a striped shirt just pried open my patio door!",
                TranscriptLine2 = "They're stuffing my grandfather clock into a sack — hurry!",
                LogicClue = "Audio check: burglar alarm ringing + glass crunching.",
                GoodSergeantLine = "Copy that — rolling Patrol Unit to your address, lights & sirens!",
                WrongSergeantLine = "Whoa — that needed an armed Patrol response!",
                BaseUrgencySec = 18.0f,
                IsPrank = false
            },
            new DispatchCaseTemplate
            {
                Id = "walkin_stolen_car",
                Channel = DispatchCallChannel.WalkIn,
                CorrectAction = DispatchRouteAction.DispatchPatrol,
                SubjectTitle = "Walk-In: Prowler Breaking In",
                TranscriptLine1 = "Sergeant! I ran straight here — a masked prowler jumped my fence!",
                TranscriptLine2 = "He was trying the back lock with a crowbar two minutes ago!",
                LogicClue = "Visual check: genuinely out of breath, mud on shoes.",
                GoodSergeantLine = "Stay right here — I'm dispatching a Patrol Cruiser to sweep your yard!",
                WrongSergeantLine = "Hold on — a live break-in needs a Patrol Cruiser!",
                BaseUrgencySec = 20.0f,
                IsPrank = false
            },
            new DispatchCaseTemplate
            {
                Id = "phone_vandal_alley",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.DispatchPatrol,
                SubjectTitle = "Break-In at Downtown Shop",
                TranscriptLine1 = "Silent alarm just tripped at the Downtown jewelry counter!",
                TranscriptLine2 = "Two figures in black beanies are loading a getaway van!",
                LogicClue = "Verified commercial silent-alarm signal on Precinct Channel 2.",
                GoodSergeantLine = "All units in sector — intercept suspect van at Downtown Plaza!",
                WrongSergeantLine = "Wrong department — an active felony break-in is a Patrol call!",
                BaseUrgencySec = 17.5f,
                IsPrank = false
            },

            // ---- FIRE DEPT (Kitchen Fires / Grills / Sparks) ----
            new DispatchCaseTemplate
            {
                Id = "phone_kitchen_stove",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.DispatchFire,
                SubjectTitle = "Kitchen Stove Ablaze!",
                TranscriptLine1 = "I tried making Lobster Thermidor with zero Cooking skill!",
                TranscriptLine2 = "The stove, counters, and curtains are all in flames — SEND HELP!",
                LogicClue = "Smoke detector chirping rapidly in the background.",
                GoodSergeantLine = "Get outside right now — SimCity Fire Engine 4 is rolling out!",
                WrongSergeantLine = "Yikes — active flames need the Fire Department immediately!",
                BaseUrgencySec = 17.5f,
                IsPrank = false
            },
            new DispatchCaseTemplate
            {
                Id = "walkin_fireworks_grill",
                Channel = DispatchCallChannel.WalkIn,
                CorrectAction = DispatchRouteAction.DispatchFire,
                SubjectTitle = "Walk-In: Backyard Grill Blaze",
                TranscriptLine1 = "Sergeant, my neighbor lit fireworks right next to his patio grill!",
                TranscriptLine2 = "The whole cedar hedge is pouring black smoke across the lane!",
                LogicClue = "Smells strongly of singed cedar and charcoal smoke.",
                GoodSergeantLine = "Good catch — ringing Fire Station 1 to roll the pumper truck!",
                WrongSergeantLine = "A spreading hedge fire needs the Fire Engine right away!",
                BaseUrgencySec = 19.5f,
                IsPrank = false
            },
            new DispatchCaseTemplate
            {
                Id = "phone_tv_sparks",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.DispatchFire,
                SubjectTitle = "Electrical Fire in Den",
                TranscriptLine1 = "I tried fixing the Soma TV with a fork and it shot blue sparks!",
                TranscriptLine2 = "Now the shag rug is smoking and catching fire!",
                LogicClue = "Crackling electrical arc audible over the line.",
                GoodSergeantLine = "Step away from the breaker — dispatching Fire Rescue now!",
                WrongSergeantLine = "That's an active electrical blaze — Fire Dept was the call!",
                BaseUrgencySec = 18.0f,
                IsPrank = false
            },

            // ---- COMMUNITY / ANIMAL AID (Pets / Raccoons / Noise / Disputes) ----
            new DispatchCaseTemplate
            {
                Id = "phone_raccoon_trash",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.CommunityAid,
                SubjectTitle = "El Bandito Raccoon Siege",
                TranscriptLine1 = "That stripe-tailed raccoon knocked over both my curbside cans!",
                TranscriptLine2 = "He's hissing at my tabby cat and refusing to leave the porch!",
                LogicClue = "Non-violent nuisance wildlife — Animal/Community Aid specialty.",
                GoodSergeantLine = "Sending Animal & Community Aid with a humane trap right over!",
                WrongSergeantLine = "A stubborn raccoon is a job for Community & Animal Aid!",
                BaseUrgencySec = 19.0f,
                IsPrank = false
            },
            new DispatchCaseTemplate
            {
                Id = "walkin_lost_cat",
                Channel = DispatchCallChannel.WalkIn,
                CorrectAction = DispatchRouteAction.CommunityAid,
                SubjectTitle = "Walk-In: Kitten Stuck in Oak Tree",
                TranscriptLine1 = "Please Officer! Mr. Whiskers chased a squirrel up the huge oak!",
                TranscriptLine2 = "He's meowing from the top branch and won't come down for tuna!",
                LogicClue = "Friendly civic assist — Community Aid handles tree rescues cleanly.",
                GoodSergeantLine = "Don't worry — Community Aid is heading out with a ladder & treats!",
                WrongSergeantLine = "No need for sirens or hang-ups — Community Aid handles pet rescues!",
                BaseUrgencySec = 20.5f,
                IsPrank = false
            },
            new DispatchCaseTemplate
            {
                Id = "phone_boombox_noise",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.CommunityAid,
                SubjectTitle = "Midnight Disco Boombox",
                TranscriptLine1 = "The house next door is blasting disco music at full volume!",
                TranscriptLine2 = "Nobody is hurt, I just have a 7 AM carpool and need sleep!",
                LogicClue = "Peaceful noise ordinance issue — Community Liaison ideal.",
                GoodSergeantLine = "Sending a Community Liaison unit to turn that volume down gently.",
                WrongSergeantLine = "A neighbor noise complaint calls for Community Aid!",
                BaseUrgencySec = 19.5f,
                IsPrank = false
            },

            // ---- DISMISS / HANG UP (Pranks & Non-Emergencies) ----
            new DispatchCaseTemplate
            {
                Id = "phone_prank_fridge",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.DismissCall,
                SubjectTitle = "Urgent Appliance Report?",
                TranscriptLine1 = "Uh, hi police? Is your precinct refrigerator running right now?",
                TranscriptLine2 = "...Then you better go catch it! *snickering in background*",
                LogicClue = "Caller ID: payphone by Downtown Arcade. Classic prank call!",
                GoodSergeantLine = "Nice try, kids — keep 911 clear for real emergencies! *CLICK*",
                WrongSergeantLine = "Ugh, you just sent a live unit on a refrigerator prank call!",
                BaseUrgencySec = 16.5f,
                IsPrank = true
            },
            new DispatchCaseTemplate
            {
                Id = "walkin_blinking_gnome",
                Channel = DispatchCallChannel.WalkIn,
                CorrectAction = DispatchRouteAction.DismissCall,
                SubjectTitle = "Walk-In: Suspicious Lawn Gnome",
                TranscriptLine1 = "Sergeant, arrest my neighbor's wooden garden gnome immediately!",
                TranscriptLine2 = "I swear it winked at my flamingos twice before breakfast!",
                LogicClue = "Zero crime or hazard — polite dismissal keeps units free.",
                GoodSergeantLine = "We'll keep an eye on the lawn gnomes, citizen — move along now!",
                WrongSergeantLine = "Don't tie up emergency responders over a wooden lawn gnome!",
                BaseUrgencySec = 19.0f,
                IsPrank = true
            },
            new DispatchCaseTemplate
            {
                Id = "phone_wrong_pizza",
                Channel = DispatchCallChannel.Phone911,
                CorrectAction = DispatchRouteAction.DismissCall,
                SubjectTitle = "Extra-Cheese Delivery?",
                TranscriptLine1 = "Yeah, can I get a large pepperoni pizza with extra llamas?",
                TranscriptLine2 = "Wait... isn't this Mama Sim's Pizzeria? Send it anyway!",
                LogicClue = "Misdialed food order — dismiss immediately to free the line.",
                GoodSergeantLine = "This is SimCity PD 911, not a pizzeria — clearing the line!",
                WrongSergeantLine = "You just dispatched city responders to deliver a pizza!",
                BaseUrgencySec = 16.5f,
                IsPrank = true
            }
        };

        static readonly string[] FallbackCallerNames =
        {
            "Mortimer Goth",
            "Bella Goth",
            "Michael Bachelor",
            "Chris Roomies",
            "Melissa Roomies",
            "Betty Newbie",
            "Claire Charming",
            "Cornelia Goth"
        };

        static readonly string[] SimCityAddresses =
        {
            "5 Sim Lane",
            "7 Sim Lane",
            "21 Downtown Plaza",
            "55 Old Town Market",
            "10 Sim Lane",
            "3 Maple Street",
            "28 Oak Boulevard",
            "62 Riverside Walk"
        };

        readonly Random rng;
        readonly List<DispatchActiveCase> allCases = new List<DispatchActiveCase>();
        readonly Queue<int> pendingCaseOrder = new Queue<int>();
        readonly List<DispatchTriageEvent> eventQueue = new List<DispatchTriageEvent>();

        readonly float[] patrolCooldowns = new float[MaxPatrolUnits];
        readonly float[] fireCooldowns = new float[MaxFireUnits];
        readonly float[] communityCooldowns = new float[MaxCommunityUnits];

        float nextSpawnTimerSec;
        bool startedFirstWave;

        public string SergeantName { get; private set; }
        public int LogicSkillLevel { get; private set; }
        public int CharismaSkillLevel { get; private set; }
        public int TotalCases { get { return allCases.Count; } }
        public bool IsBriefing { get; private set; }
        public bool IsFinished { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public int Score { get; private set; }
        public int CalmChargesRemaining { get; private set; }
        public int SelectedCaseIndex { get; private set; }

        // Feedback & 3D scene choreography state
        public string LastFeedbackBanner { get; private set; }
        public bool LastFeedbackPositive { get; private set; }
        public float FeedbackBannerTimerSec { get; private set; }
        public string SergeantSpokenLine { get; private set; }
        public float SergeantSpeechTimerSec { get; private set; }

        public DeskSergeantGesture CurrentGesture { get; private set; }
        public float GestureTimerSec { get; private set; }
        public float PatrolRolloutTimerSec { get; private set; }
        public float FireRolloutTimerSec { get; private set; }
        public float CommunityRolloutTimerSec { get; private set; }

        // Keep recently departing 3D walk-in visible while walking back out the door
        public DispatchActiveCase ActiveWalkIn
        {
            get
            {
                return allCases.FirstOrDefault(c =>
                    c.Channel == DispatchCallChannel.WalkIn &&
                    (c.IsActive || (c.WalkInLeaving && c.WalkInLeaveProgress < 1f)));
            }
        }

        public IReadOnlyList<DispatchActiveCase> AllCases { get { return allCases; } }
        public bool LogicCluesUnlocked { get { return LogicSkillLevel >= 2; } }

        public DispatchTriageGame(
            string sergeantName,
            int logicSkillLevel,
            int charismaSkillLevel,
            IList<DispatchCallerInfo> neighborhoodCallers = null,
            int? seed = null,
            bool startInBriefing = true,
            int totalCases = DefaultTotalCases)
        {
            SergeantName = string.IsNullOrWhiteSpace(sergeantName) ? "Sim" : sergeantName.Trim();
            LogicSkillLevel = Math.Max(0, Math.Min(10, logicSkillLevel));
            CharismaSkillLevel = Math.Max(0, Math.Min(10, charismaSkillLevel));
            rng = new Random(seed ?? (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
            IsBriefing = startInBriefing;
            CalmChargesRemaining = 2 + (CharismaSkillLevel >= 5 ? 1 : 0);
            SelectedCaseIndex = -1;
            LastFeedbackBanner = "";
            SergeantSpokenLine = "";
            CurrentGesture = DeskSergeantGesture.IdleWatch;

            BuildShiftPlan(Math.Max(4, Math.Min(8, totalCases)), neighborhoodCallers);

            if (!startInBriefing)
                BeginShift();
        }

        void BuildShiftPlan(int count, IList<DispatchCallerInfo> callers)
        {
            // Guarantee a balanced, thrilling mix:
            // - At least 1 Patrol, 1 Fire, 1 CommunityAid, 1 Dismiss/Prank
            // - Exactly 2 live 3D Walk-in Citizens (cases #2 and #5) + 4 Phone 911 lines!
            var phonePatrol = HandcraftedBank.Where(t => t.Channel == DispatchCallChannel.Phone911 && t.CorrectAction == DispatchRouteAction.DispatchPatrol).ToList();
            var phoneFire = HandcraftedBank.Where(t => t.Channel == DispatchCallChannel.Phone911 && t.CorrectAction == DispatchRouteAction.DispatchFire).ToList();
            var phoneAid = HandcraftedBank.Where(t => t.Channel == DispatchCallChannel.Phone911 && t.CorrectAction == DispatchRouteAction.CommunityAid).ToList();
            var phonePrank = HandcraftedBank.Where(t => t.Channel == DispatchCallChannel.Phone911 && t.CorrectAction == DispatchRouteAction.DismissCall).ToList();
            var walkIns = HandcraftedBank.Where(t => t.Channel == DispatchCallChannel.WalkIn).ToList();

            var chosen = new List<DispatchCaseTemplate>(count);
            // Case 0: Dramatic 911 Phone Burglar / Fire opener
            chosen.Add(PickOne(phonePatrol));
            // Case 1: Live 3D Walk-in #1 (Fire or Community Aid or Patrol!)
            var w1 = PickOne(walkIns);
            chosen.Add(w1);
            // Case 2: Phone Fire (or Patrol if w1 was Fire)
            chosen.Add(w1.CorrectAction == DispatchRouteAction.DispatchFire ? PickOne(phoneAid) : PickOne(phoneFire));
            // Case 3: Phone Prank Call (tests dismissing without wasting units!)
            chosen.Add(PickOne(phonePrank));
            // Case 4: Live 3D Walk-in #2 (distinct from w1!)
            var w2Pool = walkIns.Where(w => w.Id != w1.Id).ToList();
            var w2 = w2Pool.Count > 0 ? PickOne(w2Pool) : PickOne(walkIns);
            chosen.Add(w2);
            // Case 5: Final busy phone call covering whichever real category needs spotlight
            if (chosen.All(c => c.CorrectAction != DispatchRouteAction.CommunityAid))
                chosen.Add(PickOne(phoneAid));
            else if (chosen.All(c => c.CorrectAction != DispatchRouteAction.DispatchFire))
                chosen.Add(PickOne(phoneFire));
            else
                chosen.Add(PickOne(phoneAid));

            while (chosen.Count > count) chosen.RemoveAt(chosen.Count - 1);

            float charismaBonusSec = Math.Min(6.0f, CharismaSkillLevel * 0.75f);

            for (int i = 0; i < chosen.Count; i++)
            {
                var tpl = chosen[i];
                string cName;
                short cNid = 0;
                uint cGuid = 0;
                string cAddr = SimCityAddresses[i % SimCityAddresses.Length];

                if (callers != null && callers.Count > 0)
                {
                    var info = callers[i % callers.Count];
                    cName = !string.IsNullOrWhiteSpace(info.Name) ? info.Name : FallbackCallerNames[i % FallbackCallerNames.Length];
                    cNid = info.NeighborId;
                    cGuid = info.OutfitGuid;
                    if (!string.IsNullOrWhiteSpace(info.Address)) cAddr = info.Address;
                }
                else
                {
                    cName = FallbackCallerNames[i % FallbackCallerNames.Length];
                }

                float dur = tpl.BaseUrgencySec + charismaBonusSec;
                allCases.Add(new DispatchActiveCase
                {
                    CaseIndex = i,
                    SlotNumber = tpl.Channel == DispatchCallChannel.WalkIn ? 2 : (i % 2),
                    Channel = tpl.Channel,
                    CorrectAction = tpl.CorrectAction,
                    ChosenAction = DispatchRouteAction.None,
                    Rating = DispatchCaseRating.Pending,
                    CallerName = cName,
                    CallerNeighborId = cNid,
                    CallerOutfitGuid = cGuid,
                    Address = cAddr,
                    SubjectTitle = tpl.SubjectTitle,
                    TranscriptLine1 = tpl.TranscriptLine1,
                    TranscriptLine2 = tpl.TranscriptLine2,
                    LogicClue = tpl.LogicClue,
                    GoodSergeantLine = tpl.GoodSergeantLine,
                    WrongSergeantLine = tpl.WrongSergeantLine,
                    IsPrank = tpl.IsPrank,
                    TotalUrgencySec = dur,
                    RemainingUrgencySec = dur,
                    WalkInApproach = tpl.Channel == DispatchCallChannel.WalkIn ? 0f : 1f
                });
                pendingCaseOrder.Enqueue(i);
            }
        }

        DispatchCaseTemplate PickOne(List<DispatchCaseTemplate> list)
        {
            return list[rng.Next(list.Count)];
        }

        public void BeginShift()
        {
            if (!IsBriefing && startedFirstWave) return;
            IsBriefing = false;
            startedFirstWave = true;
            // Activate Case #0 immediately and schedule Case #1 (Walk-in!) 2.6s later so the desk comes alive fast
            TryActivateNextCase();
            nextSpawnTimerSec = 2.6f;
        }

        public List<DispatchTriageEvent> DrainEvents()
        {
            if (eventQueue.Count == 0) return null;
            var copy = new List<DispatchTriageEvent>(eventQueue);
            eventQueue.Clear();
            return copy;
        }

        public List<DispatchActiveCase> GetActiveCases()
        {
            return allCases.Where(c => c.IsActive && !c.IsResolved).ToList();
        }

        public DispatchActiveCase SelectedCase
        {
            get
            {
                if (SelectedCaseIndex >= 0 && SelectedCaseIndex < allCases.Count)
                {
                    var c = allCases[SelectedCaseIndex];
                    if (c.IsActive && !c.IsResolved) return c;
                }
                return GetActiveCases().FirstOrDefault();
            }
        }

        public int ResolvedCount { get { return allCases.Count(c => c.IsResolved); } }

        public int AvailablePatrolUnits { get { return patrolCooldowns.Count(v => v <= 0.001f); } }
        public int AvailableFireUnits { get { return fireCooldowns.Count(v => v <= 0.001f); } }
        public int AvailableCommunityUnits { get { return communityCooldowns.Count(v => v <= 0.001f); } }

        public float NextPatrolReadySec { get { return AvailablePatrolUnits > 0 ? 0f : patrolCooldowns.Min(); } }
        public float NextFireReadySec { get { return AvailableFireUnits > 0 ? 0f : fireCooldowns.Min(); } }
        public float NextCommunityReadySec { get { return AvailableCommunityUnits > 0 ? 0f : communityCooldowns.Min(); } }

        public bool SelectCase(int caseIndex)
        {
            if (caseIndex < 0 || caseIndex >= allCases.Count) return false;
            var c = allCases[caseIndex];
            if (!c.IsActive || c.IsResolved) return false;
            SelectedCaseIndex = caseIndex;
            if (c.Channel == DispatchCallChannel.Phone911 && GestureTimerSec <= 0.01f)
                CurrentGesture = DeskSergeantGesture.AnswerPhone;
            return true;
        }

        public bool CycleSelectedCase()
        {
            var active = GetActiveCases();
            if (active.Count == 0) return false;
            int idx = active.FindIndex(c => c.CaseIndex == SelectedCaseIndex);
            var next = active[(idx + 1) % active.Count];
            return SelectCase(next.CaseIndex);
        }

        public bool CalmSelectedCaller()
        {
            if (IsBriefing || IsFinished) return false;
            if (CalmChargesRemaining <= 0)
            {
                ShowFeedback("No Calm Caller charges left!", false);
                return false;
            }
            var c = SelectedCase;
            if (c == null) return false;

            CalmChargesRemaining--;
            c.CalmedOnce = true;
            c.RemainingUrgencySec = Math.Min(c.TotalUrgencySec, c.RemainingUrgencySec + 6.0f);
            CurrentGesture = DeskSergeantGesture.CalmTalk;
            GestureTimerSec = 1.4f;
            SergeantSpokenLine = "Take a deep breath, " + FirstNameOf(c.CallerName) + " — I've got you covered!";
            SergeantSpeechTimerSec = 3.2f;
            ShowFeedback("CALMED " + c.CallerName.ToUpperInvariant() + " (+6s timer)", true);
            eventQueue.Add(new DispatchTriageEvent
            {
                Kind = "calmed",
                Channel = c.Channel,
                Message = c.CallerName
            });
            return true;
        }

        public bool RouteSelectedCase(DispatchRouteAction action)
        {
            if (IsBriefing || IsFinished || action == DispatchRouteAction.None) return false;
            var c = SelectedCase;
            if (c == null) return false;

            // Check unit availability (DismissCall is always instant!)
            if (action == DispatchRouteAction.DispatchPatrol && !TryConsumeUnit(patrolCooldowns, PatrolCooldownSec))
            {
                ShowFeedback("ALL PATROL UNITS BUSY! Ready in " + NextPatrolReadySec.ToString("0.0") + "s", false);
                eventQueue.Add(new DispatchTriageEvent { Kind = "unit_busy", Action = action, Channel = c.Channel });
                return false;
            }
            if (action == DispatchRouteAction.DispatchFire && !TryConsumeUnit(fireCooldowns, FireCooldownSec))
            {
                ShowFeedback("FIRE ENGINE BUSY! Ready in " + NextFireReadySec.ToString("0.0") + "s", false);
                eventQueue.Add(new DispatchTriageEvent { Kind = "unit_busy", Action = action, Channel = c.Channel });
                return false;
            }
            if (action == DispatchRouteAction.CommunityAid && !TryConsumeUnit(communityCooldowns, CommunityCooldownSec))
            {
                ShowFeedback("COMMUNITY AID BUSY! Ready in " + NextCommunityReadySec.ToString("0.0") + "s", false);
                eventQueue.Add(new DispatchTriageEvent { Kind = "unit_busy", Action = action, Channel = c.Channel });
                return false;
            }

            // Trigger 3D outdoor vehicle roll-out effect!
            if (action == DispatchRouteAction.DispatchPatrol) PatrolRolloutTimerSec = 3.2f;
            else if (action == DispatchRouteAction.DispatchFire) FireRolloutTimerSec = 3.6f;
            else if (action == DispatchRouteAction.CommunityAid) CommunityRolloutTimerSec = 2.8f;

            c.ChosenAction = action;
            c.IsActive = false;
            c.IsResolved = true;

            bool correct = (action == c.CorrectAction);
            if (correct)
            {
                bool fast = c.UrgencyFraction >= 0.35f;
                c.Rating = fast ? DispatchCaseRating.Perfect : DispatchCaseRating.Good;
                int speedBonus = fast ? (int)Math.Round(c.UrgencyFraction * 30f) : 0;
                int pts = (fast ? 100 : 75) + speedBonus;
                Score += pts;
                SergeantSpokenLine = c.GoodSergeantLine;
                SergeantSpeechTimerSec = 3.6f;
                string tag = c.IsPrank ? "PRANK BLOCKED!" : (fast ? "PERFECT DISPATCH!" : "GOOD DISPATCH!");
                ShowFeedback(tag + " (+" + pts + " pts)", true);
                eventQueue.Add(new DispatchTriageEvent
                {
                    Kind = "routed_good",
                    Action = action,
                    Channel = c.Channel,
                    Message = c.SubjectTitle
                });
            }
            else
            {
                c.Rating = DispatchCaseRating.WrongUnit;
                Score = Math.Max(0, Score - 25);
                SergeantSpokenLine = c.WrongSergeantLine;
                SergeantSpeechTimerSec = 3.6f;
                ShowFeedback("WRONG UNIT! (-25 pts • needed " + ActionShortName(c.CorrectAction) + ")", false);
                eventQueue.Add(new DispatchTriageEvent
                {
                    Kind = "routed_wrong",
                    Action = action,
                    Channel = c.Channel,
                    Message = c.SubjectTitle
                });
            }

            // Trigger Desk Sergeant custom 3D animation!
            CurrentGesture = action == DispatchRouteAction.DismissCall
                ? DeskSergeantGesture.WaveDismiss
                : DeskSergeantGesture.RadioDispatch;
            GestureTimerSec = 1.35f;

            if (c.Channel == DispatchCallChannel.WalkIn)
            {
                c.WalkInLeaving = true;
                c.WalkInLeaveProgress = 0f;
                c.WalkInHappyLeave = correct;
            }

            // Immediately focus another waiting call if any, or nudge next case arrival sooner
            AutoSelectNextActive();
            if (GetActiveCases().Count == 0 && pendingCaseOrder.Count > 0)
                nextSpawnTimerSec = Math.Min(nextSpawnTimerSec, 0.9f);

            CheckFinished();
            return true;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f || IsBriefing || IsFinished) return;
            ElapsedSeconds += dt;

            // Tick unit cooldowns
            TickArray(patrolCooldowns, dt);
            TickArray(fireCooldowns, dt);
            TickArray(communityCooldowns, dt);

            if (FeedbackBannerTimerSec > 0f) FeedbackBannerTimerSec = Math.Max(0f, FeedbackBannerTimerSec - dt);
            if (SergeantSpeechTimerSec > 0f) SergeantSpeechTimerSec = Math.Max(0f, SergeantSpeechTimerSec - dt);
            if (PatrolRolloutTimerSec > 0f) PatrolRolloutTimerSec = Math.Max(0f, PatrolRolloutTimerSec - dt);
            if (FireRolloutTimerSec > 0f) FireRolloutTimerSec = Math.Max(0f, FireRolloutTimerSec - dt);
            if (CommunityRolloutTimerSec > 0f) CommunityRolloutTimerSec = Math.Max(0f, CommunityRolloutTimerSec - dt);

            if (GestureTimerSec > 0f)
            {
                GestureTimerSec = Math.Max(0f, GestureTimerSec - dt);
                if (GestureTimerSec <= 0.001f)
                {
                    var sel = SelectedCase;
                    CurrentGesture = (sel != null && sel.Channel == DispatchCallChannel.Phone911)
                        ? DeskSergeantGesture.AnswerPhone
                        : DeskSergeantGesture.IdleWatch;
                }
            }
            else
            {
                var sel = SelectedCase;
                CurrentGesture = (sel != null && sel.Channel == DispatchCallChannel.Phone911)
                    ? DeskSergeantGesture.AnswerPhone
                    : DeskSergeantGesture.IdleWatch;
            }

            // Tick 3D Walk-in approach & departure animation progress
            for (int i = 0; i < allCases.Count; i++)
            {
                var c = allCases[i];
                if (c.Channel != DispatchCallChannel.WalkIn) continue;
                if (c.IsActive && !c.IsResolved && c.WalkInApproach < 1f)
                {
                    c.WalkInApproach = Math.Min(1f, c.WalkInApproach + dt * 0.65f);
                }
                else if (c.WalkInLeaving && c.WalkInLeaveProgress < 1f)
                {
                    c.WalkInLeaveProgress = Math.Min(1f, c.WalkInLeaveProgress + dt * 0.60f);
                }
            }

            // Tick active call urgency countdowns
            for (int i = 0; i < allCases.Count; i++)
            {
                var c = allCases[i];
                if (!c.IsActive || c.IsResolved) continue;

                // Don't penalize walk-in timer until they actually reach the counter!
                if (c.Channel == DispatchCallChannel.WalkIn && c.WalkInApproach < 0.92f)
                    continue;

                c.RemainingUrgencySec -= dt;
                if (c.RemainingUrgencySec <= 0f)
                {
                    c.RemainingUrgencySec = 0f;
                    c.IsActive = false;
                    c.IsResolved = true;
                    c.Rating = DispatchCaseRating.Missed;
                    Score = Math.Max(0, Score - 40);
                    ShowFeedback("MISSED CALL! " + c.CallerName + " hung up (-40 pts)", false);
                    SergeantSpokenLine = "Lost the line — we've got to keep up with the board!";
                    SergeantSpeechTimerSec = 3.0f;
                    if (c.Channel == DispatchCallChannel.WalkIn)
                    {
                        c.WalkInLeaving = true;
                        c.WalkInLeaveProgress = 0f;
                        c.WalkInHappyLeave = false;
                    }
                    eventQueue.Add(new DispatchTriageEvent
                    {
                        Kind = "missed",
                        Channel = c.Channel,
                        Message = c.CallerName
                    });
                    AutoSelectNextActive();
                }
            }

            // Staggered incoming call & walk-in arrivals
            if (pendingCaseOrder.Count > 0)
            {
                nextSpawnTimerSec -= dt;
                if (nextSpawnTimerSec <= 0f)
                {
                    if (TryActivateNextCase())
                    {
                        // Escalate gently: 5.5s -> 4.2s between overlapping calls
                        nextSpawnTimerSec = Math.Max(3.6f, 5.4f - ResolvedCount * 0.3f);
                    }
                    else
                    {
                        // Slot full (e.g., another walk-in still at counter) — retry soon
                        nextSpawnTimerSec = 0.8f;
                    }
                }
            }

            CheckFinished();
        }

        bool TryActivateNextCase()
        {
            if (pendingCaseOrder.Count == 0) return false;
            int nextIdx = pendingCaseOrder.Peek();
            var c = allCases[nextIdx];

            // Keep max 3 simultaneous active cases, and max 1 active/leaving Walk-in at the counter at once!
            var active = GetActiveCases();
            if (active.Count >= 3) return false;
            if (c.Channel == DispatchCallChannel.WalkIn)
            {
                bool walkInBusy = allCases.Any(x =>
                    x.Channel == DispatchCallChannel.WalkIn &&
                    (x.IsActive || (x.WalkInLeaving && x.WalkInLeaveProgress < 0.85f)));
                if (walkInBusy) return false;
                c.SlotNumber = 2;
            }
            else
            {
                // Pick free phone slot 0 or 1 (or 2 if counter is empty and both phone lines are active)
                bool s0 = active.Any(x => x.SlotNumber == 0);
                bool s1 = active.Any(x => x.SlotNumber == 1);
                c.SlotNumber = !s0 ? 0 : (!s1 ? 1 : 0);
            }

            pendingCaseOrder.Dequeue();
            c.IsActive = true;
            c.RemainingUrgencySec = c.TotalUrgencySec;
            if (c.Channel == DispatchCallChannel.WalkIn)
            {
                c.WalkInApproach = 0f;
                c.WalkInLeaving = false;
                c.WalkInLeaveProgress = 0f;
            }

            if (SelectedCase == null)
                SelectedCaseIndex = c.CaseIndex;

            eventQueue.Add(new DispatchTriageEvent
            {
                Kind = c.Channel == DispatchCallChannel.WalkIn ? "walkin_arrived" : "call_arrived",
                Channel = c.Channel,
                Message = c.CallerName + " — " + c.SubjectTitle
            });
            return true;
        }

        void AutoSelectNextActive()
        {
            var active = GetActiveCases();
            if (active.Count > 0)
            {
                // Focus the most urgent active call automatically!
                var mostUrgent = active.OrderBy(a => a.RemainingUrgencySec).First();
                SelectedCaseIndex = mostUrgent.CaseIndex;
            }
            else
            {
                SelectedCaseIndex = -1;
            }
        }

        void CheckFinished()
        {
            if (pendingCaseOrder.Count == 0 && allCases.All(c => c.IsResolved))
            {
                IsFinished = true;
            }
        }

        static bool TryConsumeUnit(float[] pool, float cooldownSec)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] <= 0.001f)
                {
                    pool[i] = cooldownSec;
                    return true;
                }
            }
            return false;
        }

        static void TickArray(float[] arr, float dt)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] > 0f) arr[i] = Math.Max(0f, arr[i] - dt);
        }

        void ShowFeedback(string text, bool positive)
        {
            LastFeedbackBanner = text;
            LastFeedbackPositive = positive;
            FeedbackBannerTimerSec = 3.2f;
        }

        public DispatchTriageOutcome ComputeOutcome()
        {
            int perfect = allCases.Count(c => c.Rating == DispatchCaseRating.Perfect);
            int good = allCases.Count(c => c.Rating == DispatchCaseRating.Good);
            int wrong = allCases.Count(c => c.Rating == DispatchCaseRating.WrongUnit);
            int missed = allCases.Count(c => c.Rating == DispatchCaseRating.Missed);
            int walkIns = allCases.Count(c => c.Channel == DispatchCallChannel.WalkIn && (c.Rating == DispatchCaseRating.Perfect || c.Rating == DispatchCaseRating.Good));
            int pranks = allCases.Count(c => c.IsPrank && (c.Rating == DispatchCaseRating.Perfect || c.Rating == DispatchCaseRating.Good));

            string grade;
            int simoleons;
            short perf;
            short logicXp;
            short charismaXp;
            short funDelta;
            short energyDelta;

            if (Score >= 520 && missed == 0 && wrong == 0)
            {
                grade = "S";
                simoleons = 175;
                perf = 28;
                logicXp = 60;
                charismaXp = 55;
                funDelta = 12;
                energyDelta = -8;
            }
            else if (Score >= 380 && missed <= 1)
            {
                grade = "A";
                simoleons = 125;
                perf = 18;
                logicXp = 42;
                charismaXp = 38;
                funDelta = 8;
                energyDelta = -10;
            }
            else if (Score >= 220)
            {
                grade = "B";
                simoleons = 80;
                perf = 10;
                logicXp = 25;
                charismaXp = 22;
                funDelta = 4;
                energyDelta = -12;
            }
            else
            {
                grade = "C";
                simoleons = 35;
                perf = 2;
                logicXp = 12;
                charismaXp = 10;
                funDelta = -2;
                energyDelta = -14;
            }

            var rels = new List<DispatchNeighborDelta>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in allCases)
            {
                if (c.IsPrank || string.IsNullOrWhiteSpace(c.CallerName)) continue;
                if (!seenNames.Add(c.CallerName)) continue;

                if (c.Rating == DispatchCaseRating.Perfect || c.Rating == DispatchCaseRating.Good)
                {
                    rels.Add(new DispatchNeighborDelta
                    {
                        SimName = c.CallerName,
                        NeighborId = c.CallerNeighborId,
                        DailyDelta = 3,
                        MemoryFact = "Dispatched rapid help for " + c.CallerName + " (" + c.SubjectTitle + ")"
                    });
                }
                else if (c.Rating == DispatchCaseRating.WrongUnit || c.Rating == DispatchCaseRating.Missed)
                {
                    rels.Add(new DispatchNeighborDelta
                    {
                        SimName = c.CallerName,
                        NeighborId = c.CallerNeighborId,
                        DailyDelta = -2,
                        MemoryFact = "Mishandled emergency call from " + c.CallerName + " at the 911 desk"
                    });
                }
            }

            string summary = (perfect + good) + "/" + allCases.Count + " routed cleanly"
                           + (walkIns > 0 ? " • " + walkIns + " walk-in" + (walkIns > 1 ? "s" : "") + " assisted" : "")
                           + (pranks > 0 ? " • " + pranks + " prank" + (pranks > 1 ? "s" : "") + " blocked" : "");

            return new DispatchTriageOutcome
            {
                Grade = grade,
                Score = Score,
                TotalCases = allCases.Count,
                PerfectCount = perfect,
                GoodCount = good,
                WrongCount = wrong,
                MissedCount = missed,
                WalkInsHandled = walkIns,
                PranksBlocked = pranks,
                Summary = summary,
                RelationshipDeltas = rels,
                Reward = new CareerDispatchReward
                {
                    Grade = grade,
                    Summary = summary,
                    BonusSimoleons = simoleons,
                    JobPerformanceDelta = perf,
                    LogicXpDelta = logicXp,
                    CharismaXpDelta = charismaXp,
                    FunDelta = funDelta,
                    EnergyDelta = energyDelta,
                    MemoryFact = "Grade " + grade + " Desk Sergeant shift at SimCity PD 911 Switchboard (" + summary + ")"
                }
            };
        }

        public static string ActionShortName(DispatchRouteAction action)
        {
            switch (action)
            {
                case DispatchRouteAction.DispatchPatrol: return "Patrol";
                case DispatchRouteAction.DispatchFire: return "Fire Dept";
                case DispatchRouteAction.CommunityAid: return "Community Aid";
                case DispatchRouteAction.DismissCall: return "Dismiss";
                default: return "None";
            }
        }

        static string FirstNameOf(string full)
        {
            if (string.IsNullOrWhiteSpace(full)) return "citizen";
            int sp = full.IndexOf(' ');
            return sp > 0 ? full.Substring(0, sp) : full;
        }
    }
}
