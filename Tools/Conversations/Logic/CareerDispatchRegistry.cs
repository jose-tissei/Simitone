using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    public enum CareerMiniGameKind
    {
        None = 0,
        SecurityWatch = 1,       // Lvl 1: Security Guard
        CadetAcademy = 2,        // Lvl 2: Cadet
        BeatPatrol = 3,          // Lvl 3: Patrol Officer (planned)
        DispatchTriage = 4,      // Lvl 4: Desk Sergeant (planned)
        UndercoverSting = 5,     // Lvl 5: Vice Squad (planned)
        CrimeSceneDeduction = 6, // Lvl 6: Detective (planned)
        PrecinctBriefing = 7,    // Lvl 7: Lieutenant (planned)
        TacticalBreach = 8,      // Lvl 8: SWAT Team Leader (planned)
        PressConference = 9,     // Lvl 9: Police Chief (planned)
        CityCrisisRescue = 10,   // Lvl 10: Captain Hero (planned)
    }

    public sealed class LawEnforcementRankDescriptor
    {
        public int PromotionLevel;          // 0..9 (Lvl 1..10)
        public string RankTitle;            // "Security Guard", "Cadet", ...
        public string MiniGameTitle;        // "CCTV Night Watch", "Academy Obstacle & Drill", ...
        public string DispatchTeaser;       // Short toast subtitle
        public string AssignmentButtonText; // Short label for Career Tab button
        public int DefaultShiftStartHour;
        public int DefaultShiftEndHour;
        public CareerMiniGameKind Kind;
        public bool IsImplemented;
    }

    public sealed class CareerDispatchReward
    {
        public string Grade = "C";
        public string Summary = "";
        public int BonusSimoleons;
        public short JobPerformanceDelta;
        public short BodyXpDelta;        // TS1 scale: 100 = 1 full skill point (0..1000)
        public short LogicXpDelta;
        public short CharismaXpDelta;
        public short MechanicalXpDelta;
        public short EnergyDelta;
        public short FunDelta;
        public short SocialDelta;
        public string MemoryFact;
    }

    // Multi-Rank Dispatch Registry for Law Enforcement Levels 1-10 (DISP-04).
    public static class CareerDispatchRegistry
    {
        static readonly LawEnforcementRankDescriptor[] Ranks =
        {
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 0,
                RankTitle = "Security Guard",
                MiniGameTitle = "CCTV Night Watch",
                DispatchTeaser = "Night watch on 4 feeds — spot intruders vs. false alarms.",
                AssignmentButtonText = "WORK ASSIGNMENT: CCTV WATCH",
                DefaultShiftStartHour = 0,
                DefaultShiftEndHour = 6,
                Kind = CareerMiniGameKind.SecurityWatch,
                IsImplemented = true,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 1,
                RankTitle = "Cadet",
                MiniGameTitle = "Academy Obstacle & Drill",
                DispatchTeaser = "3D academy run — Sprint, Vault & Target Drill for Body XP.",
                AssignmentButtonText = "WORK ASSIGNMENT: ACADEMY DRILL",
                DefaultShiftStartHour = 9,
                DefaultShiftEndHour = 15,
                Kind = CareerMiniGameKind.CadetAcademy,
                IsImplemented = true,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 2,
                RankTitle = "Patrol Officer",
                MiniGameTitle = "Beat Patrol",
                DispatchTeaser = "Handle street-patrol calls across the neighborhood.",
                AssignmentButtonText = "WORK ASSIGNMENT: BEAT PATROL",
                DefaultShiftStartHour = 17,
                DefaultShiftEndHour = 1,
                Kind = CareerMiniGameKind.BeatPatrol,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 3,
                RankTitle = "Desk Sergeant",
                MiniGameTitle = "911 Dispatch Triage",
                DispatchTeaser = "Route incoming emergency calls before the queue overflows.",
                AssignmentButtonText = "WORK ASSIGNMENT: 911 TRIAGE",
                DefaultShiftStartHour = 9,
                DefaultShiftEndHour = 15,
                Kind = CareerMiniGameKind.DispatchTriage,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 4,
                RankTitle = "Vice Squad",
                MiniGameTitle = "Undercover Sting",
                DispatchTeaser = "Keep your cover intact and draw out a confession.",
                AssignmentButtonText = "WORK ASSIGNMENT: STING OP",
                DefaultShiftStartHour = 22,
                DefaultShiftEndHour = 4,
                Kind = CareerMiniGameKind.UndercoverSting,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 5,
                RankTitle = "Detective",
                MiniGameTitle = "Crime Scene Deduction",
                DispatchTeaser = "Compare clues and accuse the true neighborhood culprit.",
                AssignmentButtonText = "WORK ASSIGNMENT: CASE FILE",
                DefaultShiftStartHour = 9,
                DefaultShiftEndHour = 15,
                Kind = CareerMiniGameKind.CrimeSceneDeduction,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 6,
                RankTitle = "Lieutenant",
                MiniGameTitle = "Precinct Squad Briefing",
                DispatchTeaser = "Assign rookie officers to matching city incidents.",
                AssignmentButtonText = "WORK ASSIGNMENT: BRIEFING",
                DefaultShiftStartHour = 9,
                DefaultShiftEndHour = 15,
                Kind = CareerMiniGameKind.PrecinctBriefing,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 7,
                RankTitle = "SWAT Team Leader",
                MiniGameTitle = "Tactical Breach Plan",
                DispatchTeaser = "Plan a 3-step room clear with zero collateral damage.",
                AssignmentButtonText = "WORK ASSIGNMENT: SWAT BREACH",
                DefaultShiftStartHour = 9,
                DefaultShiftEndHour = 15,
                Kind = CareerMiniGameKind.TacticalBreach,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 8,
                RankTitle = "Police Chief",
                MiniGameTitle = "City Hall Press Conference",
                DispatchTeaser = "Allocate precinct budget and handle tough reporters.",
                AssignmentButtonText = "WORK ASSIGNMENT: PRESS BRIEF",
                DefaultShiftStartHour = 9,
                DefaultShiftEndHour = 17,
                Kind = CareerMiniGameKind.PressConference,
                IsImplemented = false,
            },
            new LawEnforcementRankDescriptor
            {
                PromotionLevel = 9,
                RankTitle = "Captain Hero",
                MiniGameTitle = "City Crisis Rescue",
                DispatchTeaser = "Save SimCity from simultaneous high-stakes emergencies.",
                AssignmentButtonText = "WORK ASSIGNMENT: HERO RESCUE",
                DefaultShiftStartHour = 10,
                DefaultShiftEndHour = 17,
                Kind = CareerMiniGameKind.CityCrisisRescue,
                IsImplemented = false,
            },
        };

        public static IReadOnlyList<LawEnforcementRankDescriptor> AllRanks { get { return Ranks; } }

        public static bool IsLawEnforcementCareer(string careerName, string jobTitle)
        {
            if (!string.IsNullOrWhiteSpace(careerName) &&
                (careerName.IndexOf("Law", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 careerName.IndexOf("Police", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                for (int i = 0; i < Ranks.Length; i++)
                {
                    if (jobTitle.IndexOf(Ranks[i].RankTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        public static LawEnforcementRankDescriptor Resolve(string careerName, string jobTitle, int promotionLevel)
        {
            if (!IsLawEnforcementCareer(careerName, jobTitle))
                return null;

            // If title explicitly names a known rank and level looks unset/mismatched, prefer exact level when in range
            if (promotionLevel >= 0 && promotionLevel < Ranks.Length)
                return Ranks[promotionLevel];

            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                for (int i = 0; i < Ranks.Length; i++)
                {
                    if (jobTitle.IndexOf(Ranks[i].RankTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        return Ranks[i];
                }
            }
            return null;
        }

        public static LawEnforcementRankDescriptor GetByLevel(int promotionLevel)
        {
            if (promotionLevel < 0 || promotionLevel >= Ranks.Length) return null;
            return Ranks[promotionLevel];
        }
    }

    // Shared once-per-day quota tracker across both mid-shift Radio Dispatch toasts
    // and the Career Tab "Work Assignment" button so rewards cannot be double-farmed.
    public sealed class CareerDispatchQuota
    {
        readonly Dictionary<string, int> lastUsedDayBySim = new Dictionary<string, int>(StringComparer.Ordinal);

        public bool HasUsedToday(string simKey, int currentDay)
        {
            if (string.IsNullOrWhiteSpace(simKey)) return false;
            return lastUsedDayBySim.TryGetValue(simKey, out int lastDay) && lastDay == currentDay;
        }

        public bool TryConsume(string simKey, int currentDay)
        {
            if (string.IsNullOrWhiteSpace(simKey)) return false;
            if (HasUsedToday(simKey, currentDay)) return false;
            lastUsedDayBySim[simKey] = currentDay;
            return true;
        }

        public void MarkUsed(string simKey, int currentDay)
        {
            if (!string.IsNullOrWhiteSpace(simKey))
                lastUsedDayBySim[simKey] = currentDay;
        }

        public int GetLastUsedDay(string simKey)
        {
            if (string.IsNullOrWhiteSpace(simKey)) return -1;
            return lastUsedDayBySim.TryGetValue(simKey, out int lastDay) ? lastDay : -1;
        }

        public void Clear()
        {
            lastUsedDayBySim.Clear();
        }
    }
}
