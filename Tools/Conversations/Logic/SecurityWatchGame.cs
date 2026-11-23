using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    public enum SecurityEventKind
    {
        None = 0,
        // Real threats -> Sound Alarm!
        Burglar = 1,
        Vandal = 2,
        Prowler = 3,
        // False alarms -> All Clear!
        Raccoon = 4,
        StrayCat = 5,
        TreeShadow = 6,
        PizzaDelivery = 7,
    }

    public enum SecurityRoundResult
    {
        None = 0,
        ThreatCaught = 1,
        FalseAlarmCleared = 2,
        MissedThreat = 3,
        FalseAlarmTriggered = 4,
        WrongCamera = 5,
        DozedOff = 6,
        QuietPatrolCleared = 7,
    }

    public class SecurityEvent
    {
        public int RoundNumber;
        public int CameraIndex;          // 0..3
        public SecurityEventKind Kind;
        public bool IsThreat;
        public string Headline;
        public string ClueText;
        public float DurationSeconds;
        public float RemainingSeconds;
        public float MotionPhase;
    }

    public class SecurityWatchOutcome
    {
        public string Grade = "C";       // S, A, B, C
        public string Summary = "";
        public bool WasQuietShift;
        public int CorrectCount;
        public int TotalRounds;
        public int ThreatsCaught;
        public int FalseAlarmsCleared;
        public int Mistakes;
        public int BonusSimoleons;
        public short JobPerformanceDelta;
        public short EnergyDelta;
        public short FunDelta;
    }

    // Pure eligibility and trigger rules for Radio Dispatch shift mini-games.
    public static class CareerDispatchRules
    {
        public static bool IsSecurityGuard(string careerName, string jobTitle, int promotionLevel)
        {
            if (!string.IsNullOrWhiteSpace(jobTitle) &&
                jobTitle.IndexOf("Security Guard", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrWhiteSpace(careerName) &&
                (careerName.IndexOf("Law", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 careerName.IndexOf("Police", StringComparison.OrdinalIgnoreCase) >= 0) &&
                promotionLevel == 0)
                return true;

            return false;
        }

        public static bool IsWithinShiftHours(int currentHour, int startHour, int endHour)
        {
            int h = ((currentHour % 24) + 24) % 24;
            int s = ((startHour % 24) + 24) % 24;
            int e = ((endHour % 24) + 24) % 24;
            if (s == e) return true;
            if (s < e) return h >= s && h < e;
            return h >= s || h < e;
        }

        public static bool ShouldTriggerDispatch(
            bool isEligibleRank,
            bool isAtWork,
            int currentDay,
            int lastDispatchedDay,
            int currentHour,
            int shiftStartHour,
            int shiftEndHour)
        {
            if (!isEligibleRank || !isAtWork) return false;
            if (currentDay == lastDispatchedDay) return false;
            return IsWithinShiftHours(currentHour, shiftStartHour, shiftEndHour);
        }

        // Roughly half of workdays are completely quiet nights with zero radio interruption,
        // so the player never feels forced to play the CCTV mini-game every single Sim day.
        public static bool IsActiveDispatchDay(string simKey, int currentDay)
        {
            int h = StableDayHash(simKey, currentDay, 17);
            return (h % 100) < 52;
        }

        // On active dispatch days, ~25% of answered shifts are tense "Quiet Night Patrols"
        // where nothing actually breaks in, keeping real suspense when watching the monitors.
        public static bool IsQuietNightShift(string simKey, int currentDay)
        {
            int h = StableDayHash(simKey, currentDay, 53);
            return (h % 100) < 25;
        }

        static int StableDayHash(string simKey, int currentDay, int salt)
        {
            unchecked
            {
                int hash = 216613626;
                string key = simKey ?? "sim";
                for (int i = 0; i < key.Length; i++)
                    hash = (hash * 16777619) ^ key[i];
                hash = (hash * 16777619) ^ (currentDay * 31 + salt);
                hash ^= (hash >> 16);
                hash *= 73244475;
                hash ^= (hash >> 16);
                return hash & 0x7FFFFFFF;
            }
        }
    }

    // Pure state machine for Law Enforcement Level 1 ("Security Guard"): CCTV Night Watch.
    // Short punchy shifts (default 3 rounds, ~20-25s) on a 4-camera grid: spot real threats
    // (Burglar, Vandal, Prowler) and Sound Alarm, dismiss false alarms (Raccoon, Stray Cat,
    // Tree Shadow, Pizza Delivery) with All Clear, and sip coffee to stay awake.
    // Also supports "Quiet Night Patrol" shifts where all 4 monitors remain calm.
    public class SecurityWatchGame
    {
        public const int CameraCount = 4;
        public const int DefaultTotalRounds = 3;
        public const float DefaultQuietShiftSeconds = 8.5f;

        public static readonly string[] CameraLabels =
        {
            "CAM-01 FRONT GATE",
            "CAM-02 LOADING DOCK",
            "CAM-03 VAULT HALL",
            "CAM-04 BACK ALLEY",
        };

        readonly Random rng;
        readonly List<SecurityEventKind> schedule = new List<SecurityEventKind>();
        readonly float scanPauseSeconds;

        public string SimName { get; private set; }
        public int TotalRounds { get; private set; }
        public int CurrentRoundIndex { get; private set; }   // 0..TotalRounds-1
        public int SelectedCamera { get; private set; }
        public SecurityEvent ActiveEvent { get; private set; }
        public bool IsFinished { get; private set; }

        public bool IsQuietShift { get; private set; }
        public float QuietShiftDurationSeconds { get; private set; }
        public float QuietShiftRemainingSeconds { get; private set; }

        public float ScanRemainingSeconds { get; private set; }
        public bool IsScanning { get { return !IsFinished && !IsQuietShift && ActiveEvent == null && ScanRemainingSeconds > 0f; } }
        public int EventTriggerSequence { get; private set; }

        public float Alertness { get; private set; }         // 0..100
        public int CoffeeSipsLeft { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float FeedbackTimer { get; private set; }
        public string LastFeedback { get; private set; }
        public SecurityRoundResult LastResult { get; private set; }

        public int ThreatsCaught { get; private set; }
        public int FalseAlarmsCleared { get; private set; }
        public int MissedThreats { get; private set; }
        public int FalseAlarmMistakes { get; private set; }
        public int WrongCameraMistakes { get; private set; }
        public int DozedOffMistakes { get; private set; }

        public int CorrectCount { get { return ThreatsCaught + FalseAlarmsCleared; } }
        public int MistakeCount { get { return MissedThreats + FalseAlarmMistakes + WrongCameraMistakes + DozedOffMistakes; } }

        public SecurityWatchGame(
            string simName,
            int initialEnergyMotive = 50,
            int totalRounds = DefaultTotalRounds,
            int? seed = null,
            float scanPauseSeconds = 0f,
            bool quietNight = false)
        {
            SimName = string.IsNullOrWhiteSpace(simName) ? "Sim" : simName.Trim();
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
            this.scanPauseSeconds = Math.Max(0f, Math.Min(4.0f, scanPauseSeconds));
            IsQuietShift = quietNight;

            // Higher Sim Energy gives a stronger starting Alertness on the graveyard shift
            float energyNorm = Math.Max(0f, Math.Min(1f, (initialEnergyMotive + 100f) / 200f));
            Alertness = 58f + energyNorm * 34f; // 58..92
            CoffeeSipsLeft = 3;

            if (quietNight)
            {
                TotalRounds = 0;
                QuietShiftDurationSeconds = DefaultQuietShiftSeconds;
                QuietShiftRemainingSeconds = DefaultQuietShiftSeconds;
                LastFeedback = "Dead quiet across all 4 feeds... watch closely or sign off All Clear.";
                return;
            }

            TotalRounds = Math.Max(1, Math.Min(8, totalRounds));
            LastFeedback = "Night shift started. Watch all 4 feeds for intruders!";
            BuildSchedule();
            BeginRoundOrScan();
        }

        void BuildSchedule()
        {
            schedule.Clear();
            var threats = new[] { SecurityEventKind.Burglar, SecurityEventKind.Vandal, SecurityEventKind.Prowler };
            var falseAlarms = new[]
            {
                SecurityEventKind.Raccoon,
                SecurityEventKind.StrayCat,
                SecurityEventKind.TreeShadow,
                SecurityEventKind.PizzaDelivery,
            };

            // Balanced mix: short 3-round shifts get 1-2 threats & 1-2 false alarms; 5+ rounds guarantee >=2 each
            int threatCount = TotalRounds <= 3
                ? Math.Max(1, Math.Min(TotalRounds - 1, rng.Next(2) == 0 ? 1 : 2))
                : Math.Max(2, (TotalRounds + 1) / 2);
            int falseCount = Math.Max(0, TotalRounds - threatCount);

            for (int i = 0; i < threatCount; i++)
                schedule.Add(threats[rng.Next(threats.Length)]);

            // Avoid duplicate false alarms within the same short shift so Pizza Delivery, Cat, Raccoon feel varied
            var falsePool = new List<SecurityEventKind>(falseAlarms);
            for (int i = 0; i < falseCount; i++)
            {
                if (falsePool.Count == 0) falsePool.AddRange(falseAlarms);
                int pick = rng.Next(falsePool.Count);
                schedule.Add(falsePool[pick]);
                falsePool.RemoveAt(pick);
            }

            // Fisher-Yates shuffle
            for (int i = schedule.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = schedule[i];
                schedule[i] = schedule[j];
                schedule[j] = tmp;
            }

            // Guarantee round 1 starts with an unmistakable real threat so the stakes are immediately clear
            if (schedule.Count > 0 && !IsThreatKind(schedule[0]))
            {
                int swapIdx = schedule.FindIndex(IsThreatKind);
                if (swapIdx > 0)
                {
                    var tmp = schedule[0];
                    schedule[0] = schedule[swapIdx];
                    schedule[swapIdx] = tmp;
                }
            }
        }

        public static bool IsThreatKind(SecurityEventKind kind)
        {
            return kind == SecurityEventKind.Burglar ||
                   kind == SecurityEventKind.Vandal ||
                   kind == SecurityEventKind.Prowler;
        }

        void BeginRoundOrScan()
        {
            if (CurrentRoundIndex >= TotalRounds)
            {
                ActiveEvent = null;
                ScanRemainingSeconds = 0f;
                IsFinished = true;
                return;
            }

            if (scanPauseSeconds > 0.01f)
            {
                ActiveEvent = null;
                // First scan is full suspense beat; subsequent gaps are snappy ~0.85s beats
                ScanRemainingSeconds = CurrentRoundIndex == 0
                    ? scanPauseSeconds
                    : Math.Max(0.65f, scanPauseSeconds * 0.62f);
                return;
            }

            SpawnActiveEvent();
        }

        void SpawnActiveEvent()
        {
            if (CurrentRoundIndex >= TotalRounds)
            {
                ActiveEvent = null;
                IsFinished = true;
                return;
            }

            var kind = schedule[CurrentRoundIndex];
            int cam = rng.Next(CameraCount);
            // Slightly tighter window each round (8.2s -> 6.2s) so shifts stay punchy (~20-25s total)
            float dur = Math.Max(6.0f, 8.2f - CurrentRoundIndex * 0.50f);

            ActiveEvent = new SecurityEvent
            {
                RoundNumber = CurrentRoundIndex + 1,
                CameraIndex = cam,
                Kind = kind,
                IsThreat = IsThreatKind(kind),
                Headline = HeadlineOf(kind),
                ClueText = ClueOf(kind),
                DurationSeconds = dur,
                RemainingSeconds = dur,
                MotionPhase = 0f,
            };
            SelectedCamera = cam; // Auto-focus the motion-triggered camera so player can immediately inspect & decide
            EventTriggerSequence++;
        }

        public void SelectCamera(int cameraIndex)
        {
            if (cameraIndex >= 0 && cameraIndex < CameraCount)
                SelectedCamera = cameraIndex;
        }

        public bool SipCoffee()
        {
            if (IsFinished || CoffeeSipsLeft <= 0) return false;
            CoffeeSipsLeft--;
            Alertness = Math.Min(100f, Alertness + 36f);
            LastFeedback = "Sipped hot station coffee! Alertness " + (int)Math.Round(Alertness) + "%";
            FeedbackTimer = 1.6f;
            return true;
        }

        public SecurityRoundResult SoundAlarm()
        {
            if (IsFinished) return SecurityRoundResult.None;

            if (IsQuietShift)
            {
                FalseAlarmMistakes++;
                LastResult = SecurityRoundResult.FalseAlarmTriggered;
                LastFeedback = "False alarm! All feeds were empty — just graveyard-shift nerves.";
                FeedbackTimer = 2.4f;
                IsFinished = true;
                return LastResult;
            }

            if (ActiveEvent == null) return SecurityRoundResult.None;
            var ev = ActiveEvent;

            if (SelectedCamera != ev.CameraIndex)
            {
                WrongCameraMistakes++;
                return FinishRound(SecurityRoundResult.WrongCamera,
                    "Wrong feed! Motion was on " + CameraLabels[ev.CameraIndex] + ".");
            }

            if (ev.IsThreat)
            {
                ThreatsCaught++;
                return FinishRound(SecurityRoundResult.ThreatCaught,
                    "GOT 'EM! " + LabelOf(ev.Kind) + " scared off at " + CameraLabels[ev.CameraIndex] + "!");
            }

            FalseAlarmMistakes++;
            return FinishRound(SecurityRoundResult.FalseAlarmTriggered,
                "False alarm! Squad woke the block over a " + LabelOf(ev.Kind).ToLowerInvariant() + ".");
        }

        public SecurityRoundResult MarkAllClear()
        {
            if (IsFinished) return SecurityRoundResult.None;

            if (IsQuietShift)
            {
                LastResult = SecurityRoundResult.QuietPatrolCleared;
                LastFeedback = "Quiet patrol logged — all 4 perimeter feeds calm.";
                FeedbackTimer = 2.4f;
                IsFinished = true;
                return LastResult;
            }

            if (ActiveEvent == null) return SecurityRoundResult.None;
            var ev = ActiveEvent;

            if (!ev.IsThreat)
            {
                FalseAlarmsCleared++;
                return FinishRound(SecurityRoundResult.FalseAlarmCleared,
                    "Good eye! Ignored false alarm (" + LabelOf(ev.Kind) + ").");
            }

            MissedThreats++;
            return FinishRound(SecurityRoundResult.MissedThreat,
                "Missed! A " + LabelOf(ev.Kind).ToLowerInvariant() + " slipped past " + CameraLabels[ev.CameraIndex] + "!");
        }

        public void Tick(float deltaSeconds)
        {
            if (IsFinished || deltaSeconds <= 0f) return;
            ElapsedSeconds += deltaSeconds;
            if (FeedbackTimer > 0f) FeedbackTimer = Math.Max(0f, FeedbackTimer - deltaSeconds);

            // Alertness drains slowly during the midnight shift (~4.2% per second)
            Alertness = Math.Max(0f, Alertness - deltaSeconds * 4.2f);

            if (IsQuietShift)
            {
                QuietShiftRemainingSeconds = Math.Max(0f, QuietShiftRemainingSeconds - deltaSeconds);
                if (QuietShiftRemainingSeconds <= 0f)
                {
                    LastResult = SecurityRoundResult.QuietPatrolCleared;
                    LastFeedback = "Uneventful night watch complete — zero incidents reported.";
                    FeedbackTimer = 2.4f;
                    IsFinished = true;
                }
                return;
            }

            if (ActiveEvent == null)
            {
                if (ScanRemainingSeconds > 0f)
                {
                    ScanRemainingSeconds -= deltaSeconds;
                    if (ScanRemainingSeconds <= 0f)
                    {
                        ScanRemainingSeconds = 0f;
                        SpawnActiveEvent();
                    }
                }
                return;
            }

            ActiveEvent.MotionPhase += deltaSeconds;
            ActiveEvent.RemainingSeconds -= deltaSeconds;

            if (Alertness <= 0.01f)
            {
                DozedOffMistakes++;
                Alertness = 35f; // Jolt awake after the buzzer
                FinishRound(SecurityRoundResult.DozedOff,
                    "Dozed off at the desk! Sip coffee to stay awake.");
                return;
            }

            if (ActiveEvent.RemainingSeconds <= 0f)
            {
                if (ActiveEvent.IsThreat)
                {
                    MissedThreats++;
                    FinishRound(SecurityRoundResult.MissedThreat,
                        "Too slow! " + LabelOf(ActiveEvent.Kind) + " slipped out of camera view.");
                }
                else
                {
                    // Letting a harmless false alarm pass quietly also counts as staying calm
                    FalseAlarmsCleared++;
                    FinishRound(SecurityRoundResult.FalseAlarmCleared,
                        "All quiet — " + LabelOf(ActiveEvent.Kind).ToLowerInvariant() + " wandered off.");
                }
            }
        }

        // Helper for instant summary preview / automated tests
        public void AutoCompleteRemainingPerfect()
        {
            if (IsQuietShift)
            {
                if (!IsFinished) MarkAllClear();
                return;
            }

            int safety = 32;
            while (!IsFinished && safety-- > 0)
            {
                if (ActiveEvent == null)
                {
                    ScanRemainingSeconds = 0f;
                    SpawnActiveEvent();
                    if (ActiveEvent == null) break;
                }
                SelectCamera(ActiveEvent.CameraIndex);
                if (ActiveEvent.IsThreat) SoundAlarm();
                else MarkAllClear();
            }
        }

        SecurityRoundResult FinishRound(SecurityRoundResult result, string message)
        {
            LastResult = result;
            LastFeedback = message;
            FeedbackTimer = 2.4f;
            CurrentRoundIndex++;
            BeginRoundOrScan();
            return result;
        }

        public SecurityWatchOutcome ComputeOutcome()
        {
            if (IsQuietShift)
            {
                bool panicked = MistakeCount > 0;
                return new SecurityWatchOutcome
                {
                    WasQuietShift = true,
                    Grade = panicked ? "B" : "A",
                    Summary = panicked
                        ? "False alarm on an empty night shift! Dispatch laughed it off, but the precinct stayed safe."
                        : "Quiet night on the monitors — zero incidents logged. " + SimName + " earned a calm patrol bonus.",
                    CorrectCount = panicked ? 0 : 1,
                    TotalRounds = 1,
                    ThreatsCaught = 0,
                    FalseAlarmsCleared = panicked ? 0 : 1,
                    Mistakes = MistakeCount,
                    BonusSimoleons = panicked ? 15 : 35,
                    JobPerformanceDelta = (short)(panicked ? 2 : 8),
                    EnergyDelta = -2,
                    FunDelta = (short)(panicked ? -1 : 3),
                };
            }

            int correct = CorrectCount;
            int total = TotalRounds;
            double ratio = total > 0 ? (double)correct / total : 0.0;

            var o = new SecurityWatchOutcome
            {
                CorrectCount = correct,
                TotalRounds = total,
                ThreatsCaught = ThreatsCaught,
                FalseAlarmsCleared = FalseAlarmsCleared,
                Mistakes = MistakeCount,
            };

            if (correct >= total && MistakeCount == 0)
            {
                o.Grade = "S";
                o.Summary = "Spotless graveyard watch! Precinct commended " + SimName + " with overtime bonus pay.";
                o.BonusSimoleons = 90;
                o.JobPerformanceDelta = 25;
                o.EnergyDelta = -4;
                o.FunDelta = 6;
            }
            else if (ratio >= 0.75)
            {
                o.Grade = "A";
                o.Summary = "Sharp eyes on the monitors! The sergeant logged a strong shift report.";
                o.BonusSimoleons = 65;
                o.JobPerformanceDelta = 18;
                o.EnergyDelta = -5;
                o.FunDelta = 3;
            }
            else if (ratio >= 0.55)
            {
                o.Grade = "B";
                o.Summary = "Decent night watch with a couple hiccups, but the lot stayed intact.";
                o.BonusSimoleons = 40;
                o.JobPerformanceDelta = 10;
                o.EnergyDelta = -7;
                o.FunDelta = 0;
            }
            else
            {
                o.Grade = "C";
                o.Summary = "Rough shift on the cameras. Better grab an extra mug of coffee tomorrow!";
                o.BonusSimoleons = 15;
                o.JobPerformanceDelta = -5;
                o.EnergyDelta = -10;
                o.FunDelta = -4;
            }
            return o;
        }

        public static string LabelOf(SecurityEventKind kind)
        {
            switch (kind)
            {
                case SecurityEventKind.Burglar: return "Burglar";
                case SecurityEventKind.Vandal: return "Vandal";
                case SecurityEventKind.Prowler: return "Prowler";
                case SecurityEventKind.Raccoon: return "Raccoon";
                case SecurityEventKind.StrayCat: return "Stray Cat";
                case SecurityEventKind.TreeShadow: return "Tree Shadow";
                case SecurityEventKind.PizzaDelivery: return "Pizza Delivery";
                default: return "Quiet";
            }
        }

        static string HeadlineOf(SecurityEventKind kind)
        {
            switch (kind)
            {
                case SecurityEventKind.Burglar: return "MOTION: Striped figure near the entry!";
                case SecurityEventKind.Vandal: return "MOTION: Hooded figure spraying the wall!";
                case SecurityEventKind.Prowler: return "MOTION: Crouching figure sneaking by crates!";
                case SecurityEventKind.Raccoon: return "MOTION: Small masked critter by the trash bin!";
                case SecurityEventKind.StrayCat: return "MOTION: Pair of glowing eyes trotting past!";
                case SecurityEventKind.TreeShadow: return "MOTION: Swaying branches in the night breeze!";
                case SecurityEventKind.PizzaDelivery: return "MOTION: Uniformed delivery Sim walking up!";
                default: return "All feeds quiet.";
            }
        }

        static string ClueOf(SecurityEventKind kind)
        {
            switch (kind)
            {
                case SecurityEventKind.Burglar: return "Carrying a crowbar and loot sack — real intruder?";
                case SecurityEventKind.Vandal: return "Fresh graffiti paint hitting the precinct fence!";
                case SecurityEventKind.Prowler: return "Shining a flashlight into locked storage!";
                case SecurityEventKind.Raccoon: return "Ringed tail rummaging through leftover donuts.";
                case SecurityEventKind.StrayCat: return "Pointy ears and curved tail, totally harmless.";
                case SecurityEventKind.TreeShadow: return "Gusty wind tripping the infrared motion sensor.";
                case SecurityEventKind.PizzaDelivery: return "Red-and-white delivery cap — late-night pizza run?";
                default: return "";
            }
        }
    }
}
