using System;
using System.Collections.Generic;
using System.Linq;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Multi-rank Radio Dispatch router & Career Tab Work Assignment driver for Law Enforcement (DISP-04).
    // Routes active shifts and manual daily assignments to rank-specific mini-games (Lvl 1 Security Guard,
    // Lvl 2 Cadet Academy, and future Levels 3-10) while sharing a single daily quota and VM reward pipeline.
    public static class CareerDispatchDriver
    {
        static readonly CareerDispatchQuota dailyQuota = new CareerDispatchQuota();
        static int tickCounter;

        public static bool ToastVisible { get { return CareerDispatchToast.IsOpen; } }
        public static bool GameOpen { get { return SecurityWatchOverlay.IsOpen || CadetAcademyOverlay.IsOpen || BeatPatrolDriver.IsActive || DispatchTriageOverlay.IsOpen; } }

        public static void OnTick(VM vm)
        {
            if (vm == null || !vm.Ready || !Boot.Enabled) return;
            tickCounter++;

            // Keep Career Tab "Work Assignment" button attached whenever UIJobSubpanel is open
            if (tickCounter % 15 == 0)
                CareerAssignmentButton.SyncWithCareerTab(vm);

            if (tickCounter % 60 != 0) return;
            if (ToastVisible || GameOpen) return;

            try
            {
                var fam = vm.TS1State?.CurrentFamily;
                if (fam == null || fam.FamilyGUIDs == null) return;
                int day = vm.Context?.Clock?.DayOfMonth ?? 1;
                int hour = vm.Context?.Clock?.Hours ?? 0;

                foreach (var av in vm.Entities.OfType<VMAvatar>())
                {
                    if (av == null || av.Dead || av.IsPet) continue;
                    if (!IsInActiveFamily(av, fam)) continue;

                    var rank = ResolveRankForAvatar(av, out string jobTitle, out int startHour, out int endHour);
                    if (rank == null || !rank.IsImplemented) continue;

                    bool isAtWork = av.Position == LotTilePos.OUT_OF_WORLD;
                    string simKey = KeyOf(av);
                    int lastDay = dailyQuota.GetLastUsedDay(simKey);

                    if (CareerDispatchRules.ShouldTriggerDispatch(true, isAtWork, day, lastDay, hour, startHour, endHour))
                    {
                        dailyQuota.MarkUsed(simKey, day);
                        if (!CareerDispatchRules.IsActiveDispatchDay(simKey, day))
                        {
                            Boot.Log("dispatch: quiet shift (no radio interruption) for " + av.Name + " (" + rank.RankTitle + ") day=" + day);
                            break;
                        }
                        bool quietNight = rank.Kind == CareerMiniGameKind.SecurityWatch &&
                                          CareerDispatchRules.IsQuietNightShift(simKey, day);
                        CareerDispatchToast.Show(av, rank, string.IsNullOrWhiteSpace(jobTitle) ? rank.RankTitle : jobTitle, quietNight);
                        Boot.Log("dispatch: triggered radio call for " + av.Name + " (" + rank.RankTitle + ") day=" + day + " hour=" + hour + " quiet=" + quietNight);
                        break;
                    }
                }
            }
            catch (Exception e) { Boot.Log("dispatch tick error: " + e.Message); }
        }

        public static LawEnforcementRankDescriptor ResolveRankForAvatar(
            VMAvatar av,
            out string jobTitle,
            out int startHour,
            out int endHour)
        {
            jobTitle = "";
            startHour = 0;
            endHour = 6;
            if (av == null) return null;

            var jobType = av.GetPersonData(VMPersonDataVariable.JobType);
            if (jobType <= 0) return null;
            var level = av.GetPersonData(VMPersonDataVariable.JobPromotionLevel);

            string careerName = "";
            try
            {
                var job = Content.Get().Jobs.GetJob((ushort)jobType);
                if (job != null)
                {
                    careerName = job.Name ?? "";
                    if (job.JobLevels != null && level >= 0 && level < job.JobLevels.Length)
                    {
                        jobTitle = job.JobLevels[level].JobName ?? "";
                        startHour = job.JobLevels[level].StartTime;
                        endHour = job.JobLevels[level].EndTime;
                    }
                }
            }
            catch { }

            var desc = CareerDispatchRegistry.Resolve(careerName, jobTitle, level);
            if (desc != null && startHour == 0 && endHour == 6 && desc.PromotionLevel > 0)
            {
                startHour = desc.DefaultShiftStartHour;
                endHour = desc.DefaultShiftEndHour;
            }
            return desc;
        }

        public static bool HasUsedDailyQuota(VMAvatar av, VM vm)
        {
            if (av == null) return false;
            int day = vm?.Context?.Clock?.DayOfMonth ?? 1;
            return dailyQuota.HasUsedToday(KeyOf(av), day);
        }

        public static bool TryConsumeDailyQuota(VMAvatar av, VM vm)
        {
            if (av == null) return false;
            int day = vm?.Context?.Clock?.DayOfMonth ?? 1;
            return dailyQuota.TryConsume(KeyOf(av), day);
        }

        public static string LaunchRankMiniGame(
            VMAvatar av,
            LawEnforcementRankDescriptor rank,
            bool quietNight = false,
            int? bodyLevelOverride = null,
            int? seed = null)
        {
            CareerDispatchToast.Close();
            if (rank == null) rank = CareerDispatchRegistry.GetByLevel(0);

            switch (rank.Kind)
            {
                case CareerMiniGameKind.DispatchTriage:
                    SecurityWatchOverlay.Close(false);
                    CadetAcademyOverlay.Close(false);
                    return DispatchTriageOverlay.Open(av, logicLevelOverride: null, charismaLevelOverride: null, seed: seed);
                case CareerMiniGameKind.BeatPatrol:
                    SecurityWatchOverlay.Close(false);
                    CadetAcademyOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    return BeatPatrolDriver.BeginPatrol(av, lotIdOverride: null, seed: seed);
                case CareerMiniGameKind.CadetAcademy:
                    SecurityWatchOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    return CadetAcademyOverlay.Open(av, bodyLevelOverride, seed);
                case CareerMiniGameKind.SecurityWatch:
                default:
                    CadetAcademyOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    return SecurityWatchOverlay.Open(av, seed, SecurityWatchGame.DefaultTotalRounds, 3.6f, quietNight);
            }
        }

        static bool IsInActiveFamily(VMAvatar av, FSO.Files.Formats.IFF.Chunks.FAMI fam)
        {
            if (fam == null) return false;
            try
            {
                if (fam.RuntimeSubset != null && fam.RuntimeSubset.Contains(av.Object.OBJ.GUID)) return true;
                if (fam.FamilyGUIDs != null && fam.FamilyGUIDs.Contains(av.Object.OBJ.GUID)) return true;
            }
            catch { }
            return false;
        }

        public static VMAvatar ResolveTargetAvatar(string query)
        {
            try
            {
                var screen = UIScreen.Current;
                var vm = screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;
                if (vm != null && vm.Ready && !string.IsNullOrWhiteSpace(query))
                {
                    var match = vm.Entities.OfType<VMAvatar>().FirstOrDefault(a =>
                        !a.Dead && !a.IsPet &&
                        (a.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return match;
                }
                var sel = screen?.GetType().GetProperty("SelectedAvatar")?.GetValue(screen, null) as VMAvatar;
                if (sel != null) return sel;
                var lot = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                var active = lot?.GetType().GetProperty("ActiveEntity")?.GetValue(lot, null) as VMAvatar;
                if (active != null) return active;
                if (vm != null && vm.Ready)
                    return vm.Entities.OfType<VMAvatar>().FirstOrDefault(a => !a.Dead && !a.IsPet);
            }
            catch { }
            return null;
        }

        public static VM CurrentVM()
        {
            try
            {
                var screen = UIScreen.Current;
                return screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;
            }
            catch { return null; }
        }

        public static string ApplyRewards(VMAvatar sim, SecurityWatchOutcome outcome)
        {
            if (outcome == null) return "no outcome";
            return ApplyReward(sim, new CareerDispatchReward
            {
                Grade = outcome.Grade,
                Summary = outcome.Summary,
                BonusSimoleons = outcome.BonusSimoleons,
                JobPerformanceDelta = outcome.JobPerformanceDelta,
                EnergyDelta = outcome.EnergyDelta,
                FunDelta = outcome.FunDelta,
            });
        }

        // Unified reward pipeline for all Law Enforcement ranks (Simoleons, Performance, Skills, Motives).
        public static string ApplyReward(VMAvatar sim, CareerDispatchReward reward)
        {
            if (reward == null) return "no reward";
            var applied = new List<string>();
            try
            {
                var vm = CurrentVM();
                var fam = vm?.TS1State?.CurrentFamily;
                if (fam != null && reward.BonusSimoleons != 0)
                {
                    fam.Budget = Math.Max(0, fam.Budget + reward.BonusSimoleons);
                    applied.Add("+$" + reward.BonusSimoleons);
                }

                if (sim != null)
                {
                    if (reward.JobPerformanceDelta != 0)
                    {
                        short cur = sim.GetPersonData(VMPersonDataVariable.JobPerformance);
                        short next = (short)Math.Max(-100, Math.Min(100, cur + reward.JobPerformanceDelta));
                        sim.SetPersonData(VMPersonDataVariable.JobPerformance, next);
                        applied.Add("Perf " + Signed(reward.JobPerformanceDelta));
                    }

                    ApplySkillDelta(sim, VMPersonDataVariable.BodySkill, reward.BodyXpDelta, "Body", applied);
                    ApplySkillDelta(sim, VMPersonDataVariable.LogicSkill, reward.LogicXpDelta, "Logic", applied);
                    ApplySkillDelta(sim, VMPersonDataVariable.CharismaSkill, reward.CharismaXpDelta, "Charisma", applied);
                    ApplySkillDelta(sim, VMPersonDataVariable.MechanicalSkill, reward.MechanicalXpDelta, "Mech", applied);

                    ApplyMotiveDelta(sim, VMMotive.Energy, reward.EnergyDelta);
                    ApplyMotiveDelta(sim, VMMotive.Fun, reward.FunDelta);
                    ApplyMotiveDelta(sim, VMMotive.Social, reward.SocialDelta);
                }
            }
            catch (Exception e) { Boot.Log("dispatch reward apply error: " + e.Message); }

            string summary = applied.Count > 0 ? string.Join(", ", applied) : "recorded";
            Boot.Log("dispatch: shift reward grade=" + reward.Grade + " (" + summary + ")");
            return summary;
        }

        static void ApplySkillDelta(VMAvatar sim, VMPersonDataVariable varId, short delta, string label, List<string> applied)
        {
            if (delta == 0) return;
            short cur = sim.GetPersonData(varId);
            short next = (short)Math.Max(0, Math.Min(1000, cur + delta));
            sim.SetPersonData(varId, next);
            applied.Add(label + " " + Signed(delta));
        }

        static void ApplyMotiveDelta(VMAvatar sim, VMMotive motive, short delta)
        {
            if (delta == 0) return;
            short cur = sim.GetMotiveData(motive);
            short next = (short)Math.Max(-100, Math.Min(100, cur + delta));
            sim.SetMotiveData(motive, next);
        }

        public static string Command(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            var arg = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

            switch (sub)
            {
                case "status":
                {
                    string activeDetail = "";
                    if (DispatchTriageOverlay.IsOpen) activeDetail = " [" + DispatchTriageOverlay.StatusSummary() + "]";
                    else if (BeatPatrolDriver.IsActive) activeDetail = " [patrol: " + BeatPatrolDriver.StatusSummary() + "]";
                    else if (CadetAcademyOverlay.IsOpen) activeDetail = " [" + CadetAcademyOverlay.StatusSummary() + "]";
                    else if (SecurityWatchOverlay.IsOpen) activeDetail = " [" + SecurityWatchOverlay.Status() + "]";
                    return "toast=" + ToastVisible + " game=" + GameOpen + activeDetail;
                }
                case "toast":
                {
                    LawEnforcementRankDescriptor rank = null;
                    string simQuery = arg;
                    if (args.Length > 1 && (args[args.Length - 1].Equals("triage", StringComparison.OrdinalIgnoreCase) ||
                                            args[args.Length - 1].Equals("desk", StringComparison.OrdinalIgnoreCase)))
                    {
                        rank = CareerDispatchRegistry.GetByLevel(3);
                        simQuery = args.Length > 2 ? string.Join(" ", args.Skip(1).Take(args.Length - 2)) : null;
                    }
                    else if (args.Length > 1 && args[args.Length - 1].Equals("patrol", StringComparison.OrdinalIgnoreCase))
                    {
                        rank = CareerDispatchRegistry.GetByLevel(2);
                        simQuery = args.Length > 2 ? string.Join(" ", args.Skip(1).Take(args.Length - 2)) : null;
                    }
                    else if (args.Length > 1 && args[args.Length - 1].Equals("cadet", StringComparison.OrdinalIgnoreCase))
                    {
                        rank = CareerDispatchRegistry.GetByLevel(1);
                        simQuery = args.Length > 2 ? string.Join(" ", args.Skip(1).Take(args.Length - 2)) : null;
                    }
                    else if (args.Length > 1 && args[args.Length - 1].Equals("guard", StringComparison.OrdinalIgnoreCase))
                    {
                        rank = CareerDispatchRegistry.GetByLevel(0);
                        simQuery = args.Length > 2 ? string.Join(" ", args.Skip(1).Take(args.Length - 2)) : null;
                    }
                    var av = ResolveTargetAvatar(simQuery);
                    if (rank == null)
                        rank = ResolveRankForAvatar(av, out _, out _, out _) ?? CareerDispatchRegistry.GetByLevel(0);
                    return CareerDispatchToast.Show(av, rank, rank.RankTitle, false);
                }
                case "triage":
                case "desk":
                {
                    CareerDispatchToast.Close();
                    SecurityWatchOverlay.Close(false);
                    CadetAcademyOverlay.Close(false);
                    int? logicLvl = 4;
                    int? charismaLvl = 4;
                    int? seedVal = 42;
                    string simQuery = null;
                    if (args.Length > 1)
                    {
                        if (int.TryParse(args[1].Trim(), out var parsedLogic))
                        {
                            logicLvl = parsedLogic;
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedCha))
                                charismaLvl = parsedCha;
                            if (args.Length > 3 && int.TryParse(args[3].Trim(), out var parsedSeed))
                                seedVal = parsedSeed;
                        }
                        else
                        {
                            simQuery = args[1].Trim();
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedLogic2))
                                logicLvl = parsedLogic2;
                            if (args.Length > 3 && int.TryParse(args[3].Trim(), out var parsedCha2))
                                charismaLvl = parsedCha2;
                            if (args.Length > 4 && int.TryParse(args[4].Trim(), out var parsedSeed2))
                                seedVal = parsedSeed2;
                        }
                    }
                    var av = ResolveTargetAvatar(simQuery);
                    return DispatchTriageOverlay.Open(av, logicLvl, charismaLvl, seedVal);
                }
                case "patrol":
                {
                    CareerDispatchToast.Close();
                    SecurityWatchOverlay.Close(false);
                    CadetAcademyOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    int? lotId = null;
                    int? seedVal = 42;
                    string simQuery = null;
                    if (args.Length > 1)
                    {
                        if (int.TryParse(args[1].Trim(), out var parsedLot))
                        {
                            lotId = parsedLot;
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedSeed))
                                seedVal = parsedSeed;
                        }
                        else
                        {
                            simQuery = args[1].Trim();
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedLot2))
                                lotId = parsedLot2;
                            if (args.Length > 3 && int.TryParse(args[3].Trim(), out var parsedSeed2))
                                seedVal = parsedSeed2;
                        }
                    }
                    var av = ResolveTargetAvatar(simQuery);
                    return BeatPatrolDriver.BeginPatrol(av, lotId, seedVal);
                }
                case "cadet":
                {
                    CareerDispatchToast.Close();
                    SecurityWatchOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    int? bodyLvl = null;
                    int? seedVal = 42;
                    string simQuery = null;
                    if (args.Length > 1)
                    {
                        if (int.TryParse(args[1].Trim(), out var parsedBody))
                        {
                            bodyLvl = parsedBody;
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedSeed))
                                seedVal = parsedSeed;
                        }
                        else
                        {
                            simQuery = args[1].Trim();
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedBody2))
                                bodyLvl = parsedBody2;
                            if (args.Length > 3 && int.TryParse(args[3].Trim(), out var parsedSeed2))
                                seedVal = parsedSeed2;
                        }
                    }
                    var av = ResolveTargetAvatar(simQuery);
                    return CadetAcademyOverlay.Open(av, bodyLvl, seedVal);
                }
                case "start":
                case "open":
                {
                    CareerDispatchToast.Close();
                    CadetAcademyOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    int seedVal = 42;
                    int rounds = SecurityWatchGame.DefaultTotalRounds;
                    float scanPause = 3.6f;
                    string simArg = null;

                    if (args.Length > 1)
                    {
                        if (int.TryParse(args[1].Trim(), out var parsedSeed))
                        {
                            seedVal = parsedSeed;
                            if (args.Length > 2 && int.TryParse(args[2].Trim(), out var parsedRounds))
                                rounds = parsedRounds;
                            if (args.Length > 3 && float.TryParse(args[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedScan))
                                scanPause = parsedScan;
                        }
                        else
                        {
                            simArg = arg;
                        }
                    }

                    var av = ResolveTargetAvatar(simArg);
                    return SecurityWatchOverlay.Open(av, seedVal, rounds, scanPause, false);
                }
                case "quiet":
                {
                    CareerDispatchToast.Close();
                    CadetAcademyOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    var av = ResolveTargetAvatar(arg);
                    return SecurityWatchOverlay.Open(av, null, 0, 0f, true);
                }
                case "answer":
                {
                    var av = CareerDispatchToast.CurrentAvatar ?? ResolveTargetAvatar(arg);
                    var rank = CareerDispatchToast.CurrentRank ?? ResolveRankForAvatar(av, out _, out _, out _) ?? CareerDispatchRegistry.GetByLevel(0);
                    bool quiet = CareerDispatchToast.IsQuietShift;
                    CareerDispatchToast.Close();
                    return LaunchRankMiniGame(av, rank, quiet);
                }
                case "action":
                {
                    if (DispatchTriageOverlay.IsOpen)
                        return DispatchTriageOverlay.HandleTestAction(args.Length > 1 ? string.Join(" ", args.Skip(1)) : "");
                    if (BeatPatrolDriver.IsActive)
                        return BeatPatrolDriver.HandleScriptedAction(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "");
                    if (CadetAcademyOverlay.IsOpen)
                        return CadetAcademyOverlay.HandleScriptedAction(args.Length > 1 ? args[1] : "");
                    return SecurityWatchOverlay.PerformCommandAction(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "");
                }
                case "finish":
                {
                    if (DispatchTriageOverlay.IsOpen) return DispatchTriageOverlay.HandleTestAction("claim");
                    if (BeatPatrolDriver.IsActive) return BeatPatrolDriver.HandleScriptedAction("end");
                    if (CadetAcademyOverlay.IsOpen) return CadetAcademyOverlay.FinishToSummary();
                    return SecurityWatchOverlay.FinishToSummary();
                }
                case "quota":
                {
                    if (args.Length > 1 && args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
                    {
                        dailyQuota.Clear();
                        return "ok quota cleared";
                    }
                    var av = ResolveTargetAvatar(arg);
                    return "ok usedToday=" + HasUsedDailyQuota(av, CurrentVM());
                }
                case "exportwav":
                {
                    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                        return "err usage: convo dispatch exportwav <path> [leadSec] [tailSec]";
                    float lead = 0f;
                    float tail = 1.0f;
                    if (args.Length > 2) float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lead);
                    if (args.Length > 3) float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tail);
                    if (DispatchTriageOverlay.IsOpen)
                        return DispatchTriageOverlay.ExportLastSessionWav(args[1], lead, tail);
                    if (BeatPatrolDriver.IsActive || BeatPatrolDriver.Audio.SessionEventCount > 0)
                        return BeatPatrolDriver.ExportLastSessionWav(args[1], lead, tail);
                    string triageRes = DispatchTriageOverlay.ExportLastSessionWav(args[1], lead, tail);
                    if (triageRes.StartsWith("ok ", StringComparison.Ordinal))
                        return triageRes;
                    return CadetAcademyOverlay.ExportLastSessionWav(args[1], lead, tail);
                }
                case "close":
                case "dismiss":
                    CareerDispatchToast.Close();
                    SecurityWatchOverlay.Close(false);
                    CadetAcademyOverlay.Close(false);
                    DispatchTriageOverlay.Close(false);
                    if (BeatPatrolDriver.IsActive) BeatPatrolDriver.RequestReturnHome(false);
                    return "closed";
                default:
                    return "usage: convo dispatch status|toast [Sim] [triage|patrol|cadet|guard]|triage [Sim] [logic] [cha] [seed]|patrol [Sim|lotId] [lotId] [seed]|start [seed] [rounds] [scanSec]|cadet [Sim|bodyLvl] [bodyLvl] [seed]|quiet [Sim]|answer|action <cmd>|finish|quota [clear]|exportwav <path> [leadSec] [tailSec]|close";
            }
        }

        static string KeyOf(VMAvatar a)
        {
            short nid = a.GetPersonData(VMPersonDataVariable.NeighborId);
            if (nid > 0) return "n:" + nid;
            return "name:" + (a.Name ?? "sim").Trim().ToLowerInvariant();
        }

        static string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }
    }

    // Optional once-per-day "Work Assignment" button injected cleanly into Simitone's Career tab (UIJobSubpanel).
    // Shares CareerDispatchDriver's daily quota with Radio Dispatch toasts so rewards cannot be double-farmed.
    internal sealed class CareerAssignmentButton : SimUiElement
    {
        static CareerAssignmentButton instance;
        static WeakReference<UIContainer> attachedSubpanel;

        readonly TextStyle labelStyle;
        Rectangle btnScreenRect;
        bool hoverBtn;
        bool prevLeftDown;
        LawEnforcementRankDescriptor currentRank;
        VMAvatar currentSim;
        bool usedToday;

        CareerAssignmentButton()
        {
            labelStyle = SimUiTokens.MakeStyle(11, Color.White, true);
        }

        public static void SyncWithCareerTab(VM vm)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                var frontend = screen.GetType().GetProperty("Frontend")?.GetValue(screen, null);
                var mainPanel = frontend?.GetType().GetProperty("MainPanel")?.GetValue(frontend, null)
                             ?? frontend?.GetType().GetField("MainPanel")?.GetValue(frontend);
                var subPanel = mainPanel?.GetType().GetField("SubPanel")?.GetValue(mainPanel) as UIContainer;

                if (subPanel == null || subPanel.GetType().Name != "UIJobSubpanel")
                {
                    if (instance != null && instance.Parent != null)
                        instance.Parent.Remove(instance);
                    attachedSubpanel = null;
                    return;
                }

                if (instance == null) instance = new CareerAssignmentButton();

                UIContainer prev = null;
                if (attachedSubpanel != null) attachedSubpanel.TryGetTarget(out prev);
                if (!ReferenceEquals(prev, subPanel) || !ReferenceEquals(instance.Parent, subPanel))
                {
                    if (instance.Parent != null) instance.Parent.Remove(instance);
                    subPanel.Add(instance);
                    attachedSubpanel = new WeakReference<UIContainer>(subPanel);
                }

                var sel = screen.GetType().GetProperty("SelectedAvatar")?.GetValue(screen, null) as VMAvatar;
                instance.currentSim = sel;
                instance.currentRank = sel != null ? CareerDispatchDriver.ResolveRankForAvatar(sel, out _, out _, out _) : null;
                instance.usedToday = sel != null && CareerDispatchDriver.HasUsedDailyQuota(sel, vm);
                instance.Visible = instance.currentRank != null && instance.currentRank.IsImplemented && !CareerDispatchDriver.GameOpen;
            }
            catch { }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!Visible || currentRank == null || currentSim == null) return;

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);
            hoverBtn = btnScreenRect.Contains(mx, my);

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown && hoverBtn && !usedToday && !CareerDispatchDriver.GameOpen)
            {
                var vm = CareerDispatchDriver.CurrentVM();
                if (CareerDispatchDriver.TryConsumeDailyQuota(currentSim, vm))
                {
                    usedToday = true;
                    CareerDispatchDriver.LaunchRankMiniGame(currentSim, currentRank, false);
                }
            }
            prevLeftDown = left;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || currentRank == null) return;

            // Sits neatly below SalaryTitle (18, 94) and cleanly left of the Skills grid (x = 334)
            var localRect = new SimRect(18f, 120f, 294f, 30f);
            var globalOrigin = LocalPoint(Vector2.Zero);
            btnScreenRect = new Rectangle(
                (int)Math.Round(globalOrigin.X + localRect.X),
                (int)Math.Round(globalOrigin.Y + localRect.Y),
                (int)Math.Round(localRect.Width),
                (int)Math.Round(localRect.Height));

            if (usedToday)
            {
                FillRoundedRect(batch, localRect, SimUiTokens.RadiusControl, new Color(18, 34, 46, 200));
                DrawRoundedBorder(batch, localRect, SimUiTokens.RadiusControl, SimUiTokens.InnerBorder, 1);
                DrawFittedLine(batch, "DAILY ASSIGNMENT COMPLETE", new SimRect(localRect.X + 8f, localRect.Y + 7f, localRect.Width - 16f, 16f), labelStyle, SimTextAlign.Center);
            }
            else
            {
                DrawPillButton(batch, localRect, currentRank.AssignmentButtonText, SimButtonVariant.Primary, hoverBtn, true);
            }
        }
    }

    // Native Simitone-styled top-right Radio Dispatch toast for any Law Enforcement rank (Levels 1-10).
    public class CareerDispatchToast : SimUiElement
    {
        const float ToastTimeoutSeconds = 30f;

        static CareerDispatchToast instance;
        static bool isOpen;

        public static bool IsOpen { get { return isOpen && instance != null; } }
        public static VMAvatar CurrentAvatar { get { return instance != null ? instance.avatar : null; } }
        public static LawEnforcementRankDescriptor CurrentRank { get { return instance != null ? instance.rankDescriptor : null; } }
        public static bool IsQuietShift { get { return instance != null && instance.quietShift; } }

        readonly TextStyle channelStyle, titleStyle, bodyStyle, timerStyle;
        VMAvatar avatar;
        LawEnforcementRankDescriptor rankDescriptor;
        string simName = "Sim";
        string rankTitle = "Security Guard";
        bool quietShift;
        float remaining = ToastTimeoutSeconds;
        float pulseTime;
        bool prevLeftDown;
        bool hoverAnswer, hoverIgnore;
        Rectangle answerRect, ignoreRect;

        CareerDispatchToast()
        {
            channelStyle = SimUiTokens.MakeStyle(12, SimUiTokens.DialogTitle, false);
            titleStyle = SimUiTokens.MakeStyle(15, SimUiTokens.TextPrimary, true);
            bodyStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            timerStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
        }

        public static string Show(VMAvatar av, LawEnforcementRankDescriptor rank, string rankTitle, bool quietNightShift = false)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new CareerDispatchToast();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                instance.avatar = av;
                instance.rankDescriptor = rank ?? CareerDispatchRegistry.GetByLevel(0);
                instance.simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                instance.rankTitle = string.IsNullOrWhiteSpace(rankTitle) ? instance.rankDescriptor.RankTitle : rankTitle.Trim();
                instance.quietShift = quietNightShift;
                instance.remaining = ToastTimeoutSeconds;
                instance.pulseTime = 0f;
                instance.prevLeftDown = true;
                instance.Visible = true;
                isOpen = true;
                return "toast=open sim=" + instance.simName + " rank=" + instance.rankTitle + " lvl=" + (instance.rankDescriptor.PromotionLevel + 1) + " quiet=" + quietNightShift;
            }
            catch (Exception e) { return "error: " + e.Message; }
        }

        public static void Close()
        {
            isOpen = false;
            if (instance != null)
            {
                instance.Visible = false;
                if (instance.Parent != null) instance.Parent.Remove(instance);
            }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen || !Visible) return;

            float dt = 1f / 60f;
            pulseTime += dt;
            remaining -= dt;
            if (remaining <= 0f)
            {
                Close();
                return;
            }

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);
            hoverAnswer = answerRect.Contains(mx, my);
            hoverIgnore = ignoreRect.Contains(mx, my);

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverAnswer)
                {
                    var av = avatar;
                    var rank = rankDescriptor;
                    bool quiet = quietShift;
                    Close();
                    CareerDispatchDriver.LaunchRankMiniGame(av, rank, quiet);
                    return;
                }
                if (hoverIgnore)
                {
                    Close();
                    return;
                }
            }
            prevLeftDown = left;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen || !Visible) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float screenW, screenH;
            GetScreenSize(batch, out screenW, out screenH);

            var layout = DispatchToastLayout.Compute(screenW, screenH);
            answerRect = ToXnaRect(layout.AnswerBtn);
            ignoreRect = ToXnaRect(layout.IgnoreBtn);

            float pulse = 0.5f + 0.5f * (float)Math.Sin(pulseTime * 6.0f);

            // 1. Translucent rounded Simitone dark glass toast shell + top-rounded gradient header bar
            DrawDialogShell(batch, layout.Panel, false, screenW, screenH, SimUiTokens.RadiusToast);
            DrawTitleBar(batch, layout.HeaderBar, layout.ChannelTextRect, "RADIO DISPATCH • CH-04", channelStyle, SimUiTokens.RadiusToast);

            // Pulsing circular radio LED inside reserved left header slot
            Color ledBg = Color.Black * 0.55f;
            FillRoundedRect(batch, new SimRect(layout.LedRect.X - 1f, layout.LedRect.Y - 1f, layout.LedRect.Width + 2f, layout.LedRect.Height + 2f), 6, ledBg);
            FillRoundedRect(batch, layout.LedRect, 5, pulse > 0.45f ? SimUiTokens.Mint : new Color(0, 110, 68, 220));

            // Animated signal bars inside rounded right header slot (never overlaps title!)
            FillRoundedRect(batch, layout.SignalBarsRect, SimUiTokens.RadiusSmall, Color.Black * 0.48f);
            for (int b = 0; b < 4; b++)
            {
                float bh = 4f + b * 3f;
                bool lit = b < 2 || pulse > 0.35f;
                FillRect(batch, px,
                    layout.SignalBarsRect.X + 4f + b * 7f,
                    layout.SignalBarsRect.Bottom - 2f - bh,
                    4f, bh,
                    lit ? SimUiTokens.Mint : new Color(40, 82, 74, 190));
            }

            // 2. Rank-aware Sim & alert copy
            int lvlNum = (rankDescriptor != null ? rankDescriptor.PromotionLevel : 0) + 1;
            string teaser = rankDescriptor != null
                ? rankDescriptor.DispatchTeaser
                : "Motion alert on precinct CCTV feeds!";
            DrawFittedLine(batch, simName + "  •  " + rankTitle + " (Lvl " + lvlNum + ")", layout.TitleRect, titleStyle, SimTextAlign.Left);
            DrawFittedLine(batch, teaser, layout.BodyRect, bodyStyle, SimTextAlign.Left);

            // 3. Native Simitone pill buttons + countdown seconds
            DrawPillButton(batch, layout.AnswerBtn, "ANSWER", SimButtonVariant.Primary, hoverAnswer);
            DrawPillButton(batch, layout.IgnoreBtn, "IGNORE", SimButtonVariant.Secondary, hoverIgnore);

            string secLabel = Math.Max(0, (int)Math.Ceiling(remaining)) + "s";
            DrawFittedLine(batch, secLabel, layout.TimerTextRect, timerStyle, SimTextAlign.Right);

            // 4. Native motive-style countdown bar
            float frac = SimLayout.Clamp(remaining / ToastTimeoutSeconds, 0f, 1f);
            DrawMotiveBar(batch, layout.TimerBarRect, frac, frac < 0.25f);
        }
    }

    // Native Simitone-styled floating CCTV Night Watch mini-game for Law Enforcement Level 1 (Security Guard).
    public class SecurityWatchOverlay : SimUiElement
    {
        static SecurityWatchOverlay instance;
        static bool isOpen;

        public static bool IsOpen { get { return isOpen && instance != null; } }

        readonly TextStyle headerTitleStyle, subStyle, cyanStyle, mintStyle, bodyStyle, mutedStyle, camStyle, quietStyle, alertStyle, redStyle, amberStyle, badgeTextStyle, statValMintStyle, statValCyanStyle, statValRedStyle, gradeStyle;
        SecurityWatchGame game;
        SecurityWatchOutcome finalOutcome;
        VMAvatar avatar;
        int savedSpeed = 1;
        bool rewardsApplied;
        int lastSeenEventSeq;
        readonly CctvSceneRenderer cctvRenderer = new CctvSceneRenderer();
        readonly CctvAudioEngine audio = new CctvAudioEngine();

        readonly Rectangle[] camRects = new Rectangle[4];
        Rectangle panelRect, closeRect, alarmRect, clearRect, coffeeRect, claimRect;
        bool prevLeftDown, prevEscDown, prevADown, prevDDown, prevSpaceDown, prevEnterDown;
        readonly bool[] prevNumDown = new bool[4];
        int hoveredCam = -1;
        bool hoverClose, hoverAlarm, hoverClear, hoverCoffee, hoverClaim;

        SecurityWatchOverlay()
        {
            headerTitleStyle = SimUiTokens.MakeStyle(20, SimUiTokens.DialogTitle, false);
            subStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            cyanStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
            bodyStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            mutedStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            camStyle = SimUiTokens.MakeStyle(12, SimUiTokens.MintSoft, true);
            quietStyle = SimUiTokens.MakeStyle(12, new Color(78, 148, 108, 170), false);
            alertStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Danger, true);
            redStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Danger, true);
            amberStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Amber, true);
            badgeTextStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            statValMintStyle = SimUiTokens.MakeStyle(20, SimUiTokens.Mint, true);
            statValCyanStyle = SimUiTokens.MakeStyle(20, SimUiTokens.Cyan, true);
            statValRedStyle = SimUiTokens.MakeStyle(20, SimUiTokens.Danger, true);
            gradeStyle = SimUiTokens.MakeStyle(42, Color.White, true);
        }

        public static string Open(
            VMAvatar av,
            int? seed,
            int totalRounds = SecurityWatchGame.DefaultTotalRounds,
            float scanPauseSeconds = 3.6f,
            bool quietNight = false)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new SecurityWatchOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);

                instance.avatar = av;
                string simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                int energy = av != null ? av.GetMotiveData(VMMotive.Energy) : 50;
                instance.game = new SecurityWatchGame(simName, energy, totalRounds, seed, scanPauseSeconds, quietNight);
                instance.finalOutcome = null;
                instance.rewardsApplied = false;
                instance.lastSeenEventSeq = instance.game.EventTriggerSequence;

                // Pause live simulation while player focuses on the CCTV monitor
                var vm = CareerDispatchDriver.CurrentVM();
                if (vm != null)
                {
                    instance.savedSpeed = vm.SpeedMultiplier > 0 ? vm.SpeedMultiplier : 1;
                    vm.SpeedMultiplier = 0;
                }

                // Start eerie late-night security-booth CRT hum & ambient drone
                instance.audio.StartAmbient();
                if (instance.game.EventTriggerSequence > 0)
                    instance.audio.PlayMotionAlert();

                instance.prevLeftDown = true;
                instance.prevEscDown = true;
                instance.Visible = true;
                isOpen = true;
                return Status();
            }
            catch (Exception e) { return "error: " + e.Message; }
        }

        public static void Close(bool applyIfFinished)
        {
            if (instance != null && applyIfFinished && instance.game != null && instance.game.IsFinished && !instance.rewardsApplied)
                instance.ClaimRewards();

            isOpen = false;
            if (instance != null)
            {
                instance.audio.StopAmbient();
                instance.Visible = false;
                if (instance.Parent != null) instance.Parent.Remove(instance);
                try
                {
                    var vm = CareerDispatchDriver.CurrentVM();
                    if (vm != null && vm.SpeedMultiplier == 0) vm.SpeedMultiplier = instance.savedSpeed;
                }
                catch { }
            }
        }

        public static string Status()
        {
            if (!IsOpen || instance == null || instance.game == null) return "open=False";
            var g = instance.game;
            if (g.IsFinished)
            {
                var o = instance.finalOutcome ?? g.ComputeOutcome();
                return "open=True finished=True quiet=" + g.IsQuietShift + " grade=" + o.Grade +
                       " score=" + o.CorrectCount + "/" + o.TotalRounds +
                       " bonus=$" + o.BonusSimoleons + " perf=" + o.JobPerformanceDelta;
            }
            var ev = g.ActiveEvent;
            return "open=True quiet=" + g.IsQuietShift + " scanning=" + g.IsScanning +
                   " round=" + (g.IsQuietShift ? "0/0" : ((g.CurrentRoundIndex + 1) + "/" + g.TotalRounds)) +
                   " cam=" + (ev != null ? ev.CameraIndex + 1 : 0) +
                   " kind=" + (ev != null ? ev.Kind.ToString() : "None") +
                   " threat=" + (ev != null && ev.IsThreat) +
                   " score=" + g.CorrectCount + " awake=" + (int)g.Alertness + "%" +
                   " audio=" + instance.audio.IsReady + " ambient=" + instance.audio.IsAmbientPlaying +
                   " " + instance.cctvRenderer.DiagnosticSummary();
        }

        public static string PerformCommandAction(string action, string arg)
        {
            if (!IsOpen || instance == null || instance.game == null) return "not open";
            var a = (action ?? "").ToLowerInvariant();
            if (a == "alarm") instance.HandleSoundAlarm();
            else if (a == "clear") instance.HandleMarkAllClear();
            else if (a == "coffee") instance.HandleSipCoffee();
            else if (a == "select" && int.TryParse(arg, out var idx)) instance.game.SelectCamera(idx - 1);
            else if (a == "claim" || a == "close") { Close(true); return "closed"; }
            else return "unknown action: " + action;

            return Status();
        }

        public static string FinishToSummary()
        {
            if (!IsOpen || instance == null || instance.game == null) return "not open";
            instance.game.AutoCompleteRemainingPerfect();
            instance.OnShiftFinished();
            return Status();
        }

        void HandleSoundAlarm()
        {
            if (game == null || game.IsFinished) return;
            var res = game.SoundAlarm();
            if (res == SecurityRoundResult.None) return;
            audio.PlayAlarmSiren();
            audio.PlayFeedback(res == SecurityRoundResult.ThreatCaught);
            if (game.IsFinished) OnShiftFinished();
        }

        void HandleMarkAllClear()
        {
            if (game == null || game.IsFinished) return;
            var res = game.MarkAllClear();
            if (res == SecurityRoundResult.None) return;
            audio.PlayAllClear();
            audio.PlayFeedback(res == SecurityRoundResult.FalseAlarmCleared || res == SecurityRoundResult.QuietPatrolCleared);
            if (game.IsFinished) OnShiftFinished();
        }

        void HandleSipCoffee()
        {
            if (game == null || game.IsFinished) return;
            if (game.SipCoffee()) audio.PlayCoffeeSip();
        }

        void OnShiftFinished()
        {
            if (finalOutcome == null && game != null)
                finalOutcome = game.ComputeOutcome();
            audio.StopAmbient();
            audio.PlayShiftComplete();
        }

        void ClaimRewards()
        {
            if (rewardsApplied || game == null) return;
            if (finalOutcome == null) finalOutcome = game.ComputeOutcome();
            CareerDispatchDriver.ApplyRewards(avatar, finalOutcome);
            rewardsApplied = true;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen || !Visible || game == null) return;

            if (!game.IsFinished)
            {
                bool wasFinished = game.IsFinished;
                game.Tick(1f / 60f);

                if (game.EventTriggerSequence != lastSeenEventSeq)
                {
                    lastSeenEventSeq = game.EventTriggerSequence;
                    audio.PlayMotionAlert();
                }

                if (!wasFinished && game.IsFinished)
                    OnShiftFinished();
            }

            var kb = state.KeyboardState;
            bool esc = kb.IsKeyDown(Keys.Escape);
            if (esc && !prevEscDown)
            {
                prevEscDown = true;
                Close(game.IsFinished);
                return;
            }
            prevEscDown = esc;

            if (game.IsFinished)
            {
                bool enter = kb.IsKeyDown(Keys.Enter);
                if (enter && !prevEnterDown)
                {
                    prevEnterDown = true;
                    Close(true);
                    return;
                }
                prevEnterDown = enter;
            }
            else
            {
                bool aKey = kb.IsKeyDown(Keys.A);
                if (aKey && !prevADown) HandleSoundAlarm();
                prevADown = aKey;

                bool dKey = kb.IsKeyDown(Keys.D);
                if (dKey && !prevDDown) HandleMarkAllClear();
                prevDDown = dKey;

                bool spKey = kb.IsKeyDown(Keys.Space);
                if (spKey && !prevSpaceDown) HandleSipCoffee();
                prevSpaceDown = spKey;

                var numKeys = new[] { Keys.D1, Keys.D2, Keys.D3, Keys.D4 };
                for (int i = 0; i < 4; i++)
                {
                    bool down = kb.IsKeyDown(numKeys[i]);
                    if (down && !prevNumDown[i]) game.SelectCamera(i);
                    prevNumDown[i] = down;
                }
            }

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);

            hoverClose = closeRect.Contains(mx, my);
            hoveredCam = -1;
            for (int i = 0; i < 4; i++)
                if (camRects[i].Contains(mx, my)) { hoveredCam = i; break; }
            hoverAlarm = alarmRect.Contains(mx, my);
            hoverClear = clearRect.Contains(mx, my);
            hoverCoffee = coffeeRect.Contains(mx, my);
            hoverClaim = claimRect.Contains(mx, my);

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverClose)
                {
                    prevLeftDown = true;
                    Close(game.IsFinished);
                    return;
                }

                if (game.IsFinished)
                {
                    if (hoverClaim)
                    {
                        prevLeftDown = true;
                        Close(true);
                        return;
                    }
                }
                else
                {
                    if (hoveredCam >= 0) game.SelectCamera(hoveredCam);
                    else if (hoverAlarm) HandleSoundAlarm();
                    else if (hoverClear) HandleMarkAllClear();
                    else if (hoverCoffee) HandleSipCoffee();
                }
            }
            prevLeftDown = left;
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            if (!isOpen || !Visible || game == null || game.IsFinished) return;
            var gd = batch != null && batch.GraphicsDevice != null
                ? batch.GraphicsDevice
                : FSO.Client.GameFacade.GraphicsDevice;
            cctvRenderer.PreDraw(gd, game);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen || !Visible || game == null) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float screenW, screenH;
            GetScreenSize(batch, out screenW, out screenH);

            var layout = SecurityWatchLayout.Compute(screenW, screenH, game.IsFinished);
            panelRect = ToXnaRect(layout.Panel);
            closeRect = ToXnaRect(layout.CloseBtn);

            // 1. Native Simitone modal backdrop + gradient title bar + badges + close pill
            DrawDialogShell(batch, layout.Panel, true, screenW, screenH);
            DrawTitleBar(batch, layout.HeaderBar, layout.TitleRect, "CCTV NIGHT WATCH", headerTitleStyle);

            string roundPill = game.IsFinished
                ? "SHIFT COMPLETE"
                : (game.IsQuietShift
                    ? "QUIET WATCH"
                    : (game.IsScanning
                        ? "SCANNING..."
                        : "EVENT " + (game.CurrentRoundIndex + 1) + " / " + game.TotalRounds));
            string scorePill = game.IsQuietShift
                ? "PATROL CALM"
                : "SCORE " + game.CorrectCount + "/" + game.TotalRounds;

            DrawPillBadge(batch, layout.RoundBadge, roundPill, SimUiTokens.PillDarkBg, cyanStyle);
            DrawPillBadge(batch, layout.ScoreBadge, scorePill, SimUiTokens.PillDarkBg, mintStyle);
            DrawPillButton(batch, layout.CloseBtn, "X", SimButtonVariant.Close, hoverClose);

            // Clean subtitle line below gradient header
            string sub = game.SimName + " on Duty  •  Security Guard (Lvl 1)  •  12:00 AM - 6:00 AM  •  Law Enforcement Precinct";
            DrawFittedLine(batch, sub, layout.SubtitleBar, subStyle, SimTextAlign.Left);

            if (game.IsFinished)
            {
                DrawSummary(batch, px, layout);
                return;
            }

            // 2. Responsive 2x2 physical retro CRT security monitor wall
            var ev = game.ActiveEvent;
            for (int i = 0; i < 4; i++)
            {
                camRects[i] = ToXnaRect(layout.Cameras[i]);
                DrawCameraFeed(batch, px, i, layout.Cameras[i], ev != null && ev.CameraIndex == i ? ev : null);
            }

            // 3. Responsive bottom tactical control deck (glass card)
            DrawGlassCard(batch, layout.DeckPanel, SimUiTokens.CardBgAlt, SimUiTokens.TealHighlight * 0.85f);

            string report;
            TextStyle reportStyle;
            if (ev != null)
            {
                report = SecurityWatchGame.CameraLabels[ev.CameraIndex] + "  —  " + ev.Headline;
                reportStyle = amberStyle;
            }
            else if (game.IsQuietShift)
            {
                report = "QUIET NIGHT PATROL  —  All 4 perimeter monitors calm";
                reportStyle = cyanStyle;
            }
            else
            {
                report = "SCANNING PERIMETER FEEDS...  Listen closely for movement";
                reportStyle = cyanStyle;
            }

            string clue = game.FeedbackTimer > 0f && !string.IsNullOrWhiteSpace(game.LastFeedback)
                ? game.LastFeedback
                : (ev != null
                    ? ev.ClueText
                    : (game.IsQuietShift
                        ? "No intruders detected. Confirm [D] ALL CLEAR to log an uneventful watch."
                        : "All 4 cameras quiet... stay sharp on the monitors."));
            TextStyle clueStyle = mutedStyle;
            if (game.FeedbackTimer > 0f)
            {
                if (game.LastResult == SecurityRoundResult.ThreatCaught ||
                    game.LastResult == SecurityRoundResult.FalseAlarmCleared ||
                    game.LastResult == SecurityRoundResult.QuietPatrolCleared)
                    clueStyle = mintStyle;
                else if (game.LastResult != SecurityRoundResult.None)
                    clueStyle = redStyle;
            }

            DrawFittedLine(batch, report, layout.ReportTextRect, reportStyle, SimTextAlign.Left);
            DrawFittedLine(batch, clue, layout.ClueTextRect, clueStyle, SimTextAlign.Left);

            // Event or quiet-patrol countdown motive bar on top-right of deck
            if (ev != null)
            {
                float tFrac = SimLayout.Clamp(ev.RemainingSeconds / Math.Max(0.1f, ev.DurationSeconds), 0f, 1f);
                DrawFittedLine(batch, "TIME LEFT: " + ev.RemainingSeconds.ToString("0.0") + "s",
                    layout.TimerLabelRect, tFrac < 0.30f ? alertStyle : mutedStyle, SimTextAlign.Right);
                DrawMotiveBar(batch, layout.TimerBarRect, tFrac, tFrac < 0.30f);
            }
            else if (game.IsQuietShift)
            {
                float qFrac = SimLayout.Clamp(game.QuietShiftRemainingSeconds / Math.Max(0.1f, game.QuietShiftDurationSeconds), 0f, 1f);
                DrawFittedLine(batch, "WATCH: " + game.QuietShiftRemainingSeconds.ToString("0.0") + "s",
                    layout.TimerLabelRect, cyanStyle, SimTextAlign.Right);
                DrawMotiveBar(batch, layout.TimerBarRect, qFrac, false);
            }
            else
            {
                DrawFittedLine(batch, "SCANNING...", layout.TimerLabelRect, mutedStyle, SimTextAlign.Right);
                float pulse = 0.5f + 0.5f * (float)Math.Sin(game.ElapsedSeconds * 4.5f);
                DrawMotiveBar(batch, layout.TimerBarRect, 0.25f + pulse * 0.5f, false);
            }

            // Tactical controls (native Simitone pill buttons + motive alertness bar)
            alarmRect = ToXnaRect(layout.AlarmBtn);
            clearRect = ToXnaRect(layout.ClearBtn);
            coffeeRect = ToXnaRect(layout.CoffeeBtn);

            bool actionsEnabled = ev != null || game.IsQuietShift;
            DrawPillButton(batch, layout.AlarmBtn, "[A] SOUND ALARM", SimButtonVariant.Danger, hoverAlarm && actionsEnabled, actionsEnabled);
            DrawPillButton(batch, layout.ClearBtn, "[D] ALL CLEAR", SimButtonVariant.Primary, hoverClear && actionsEnabled, actionsEnabled);

            float aFrac = SimLayout.Clamp(game.Alertness / 100f, 0f, 1f);
            DrawFittedLine(batch, "STAY AWAKE: " + (int)Math.Round(game.Alertness) + "%",
                layout.AwakeLabelRect, aFrac < 0.30f ? alertStyle : mutedStyle, SimTextAlign.Left);
            DrawMotiveBar(batch, layout.AwakeBarRect, aFrac, aFrac < 0.30f);

            bool hasCoffee = game.CoffeeSipsLeft > 0;
            DrawPillButton(batch, layout.CoffeeBtn, "[SPC] COFFEE (" + game.CoffeeSipsLeft + ")",
                SimButtonVariant.Warning, hoverCoffee && hasCoffee, hasCoffee);
        }

        void DrawCameraFeed(UISpriteBatch batch, Texture2D px, int camIdx, SimRect r, SecurityEvent ev)
        {
            bool selected = game.SelectedCamera == camIdx;
            bool hovered = hoveredCam == camIdx;
            bool activeMotion = ev != null;
            float blink = (float)Math.Sin(game.ElapsedSeconds * 7.5f);

            var monitorFrame = cctvRenderer.GetMonitorFrame(batch.GraphicsDevice);
            bool hasCrtCasing = monitorFrame != null;

            // Inner curved CRT screen aperture mapped directly to crt_monitor_frame.png (480x306 -> SX 14..466, SY 14..268)
            SimRect screen = hasCrtCasing
                ? new SimRect(
                    r.X + r.Width * 0.0292f,
                    r.Y + r.Height * 0.0458f,
                    r.Width * 0.9417f,
                    r.Height * 0.8301f)
                : r;

            float dimFactor = game.Alertness < 20f ? 0.55f : 1f;
            float hudH = hasCrtCasing ? 21f : 24f;
            float floorH = SimLayout.Clamp(screen.Height * 0.24f, 20f, 40f);
            float horizonY = screen.Bottom - floorH;

            // 1. Real Sims 1 night-time Full 3D lot backdrop rendered INSIDE the CRT monitor screen aperture
            var backdrop = cctvRenderer.GetBackdrop(batch.GraphicsDevice, camIdx);
            if (backdrop != null)
            {
                float camBright = (activeMotion ? 1.0f : (selected ? 0.90f : 0.80f)) * dimFactor;
                var tint = new Color(
                    (byte)Math.Round(255f * camBright),
                    (byte)Math.Round(255f * camBright),
                    (byte)Math.Round(255f * camBright),
                    (byte)255);
                batch.Draw(backdrop, ToXnaRect(screen), tint);
            }
            else
            {
                var bg = new Color((int)(8 * dimFactor), (int)(26 * dimFactor), (int)(22 * dimFactor), 245);
                FillRoundedRect(batch, screen, SimUiTokens.RadiusCamera, bg);
                FillRoundedBottomRect(batch, new SimRect(screen.X, horizonY, screen.Width, floorH), SimUiTokens.RadiusCamera, new Color(14, 40, 32, 220));
                FillRect(batch, px, screen.X + 2f, horizonY, Math.Max(0f, screen.Width - 4f), 2f, new Color(36, 108, 80, 170));
            }

            // 2. Live 60 FPS animated 3D Sims 1 actor (or swaying tree shadow) composited inside the CRT glass
            if (ev != null)
            {
                if (ev.Kind == SecurityEventKind.TreeShadow)
                {
                    DrawTreeShadowSweep(batch, px, screen, hudH, ev);
                }
                else if (cctvRenderer.HasLiveActorFrame && cctvRenderer.ActorTarget != null)
                {
                    DrawLive3DActorOverlay(batch, px, screen, hudH, dimFactor, ev);
                }
                else
                {
                    DrawSilhouette(batch, px, screen, horizonY, hudH, ev);
                }
            }
            else
            {
                float pillW = Math.Min(94f, screen.Width - 24f);
                var quietPill = new SimRect(screen.X + (screen.Width - pillW) * 0.5f, screen.Bottom - 24f, pillW, 17f);
                FillRoundedRect(batch, quietPill, 8, Color.Black * 0.52f);
                DrawFittedLine(batch, "ALL QUIET", quietPill, quietStyle, SimTextAlign.Center);
            }

            // 3. Subtle CRT phosphor scanlines strictly inside the glass aperture
            for (float sy = screen.Y + hudH + 2f; sy < screen.Bottom - 4f; sy += 4f)
                FillRect(batch, px, screen.X + 2f, sy, Math.Max(0f, screen.Width - 4f), 1f, new Color(0, 0, 0, 34));

            // 4. Compact translucent camera HUD bar inside top of CRT screen
            FillRect(batch, px, screen.X, screen.Y, screen.Width, hudH, Color.Black * 0.66f);

            float statusW = activeMotion ? 82f : 52f;
            var labelRect = new SimRect(screen.X + 10f, screen.Y, Math.Max(40f, screen.Width - statusW - 20f), hudH);
            DrawFittedLine(batch, "[" + (camIdx + 1) + "] " + SecurityWatchGame.CameraLabels[camIdx], labelRect, camStyle, SimTextAlign.Left);

            if (activeMotion)
            {
                if (blink > -0.2f)
                {
                    var motionRect = new SimRect(screen.Right - statusW - 8f, screen.Y + 2f, statusW, hudH - 4f);
                    DrawPillBadge(batch, motionRect, "! MOTION", SimUiTokens.DangerBtnTint, badgeTextStyle);
                }
            }
            else
            {
                float dotX = screen.Right - statusW - 4f;
                FillRoundedRect(batch, new SimRect(dotX, screen.Y + (hudH - 6f) * 0.5f, 6f, 6f), 3,
                    blink > 0 ? SimUiTokens.Danger : new Color(96, 36, 36, 180));
                DrawFittedLine(batch, "REC", new SimRect(dotX + 10f, screen.Y, statusW - 14f, hudH), mutedStyle, SimTextAlign.Left);
            }

            // 5. Physical retro Sims 1 CRT Security Monitor housing overlaid on top of the curved screen!
            if (hasCrtCasing)
            {
                batch.Draw(monitorFrame, ToXnaRect(r), Color.White);

                // Live glowing hardware power/alert LED inside the chin LED socket (434/480, 285/306)
                float ledCx = r.X + r.Width * 0.9042f;
                float ledCy = r.Y + r.Height * 0.9314f;
                float ledSize = SimLayout.Clamp(r.Height * 0.036f, 5f, 8f);
                Color ledCol = activeMotion
                    ? (blink > 0 ? SimUiTokens.Danger : SimUiTokens.Amber)
                    : (selected ? SimUiTokens.Mint : new Color(46, 150, 92, 220));
                Color ledHalo = ledCol * (activeMotion ? 0.42f : 0.28f);
                FillRoundedRect(batch, new SimRect(ledCx - ledSize, ledCy - ledSize, ledSize * 2f, ledSize * 2f), (int)ledSize, ledHalo);
                FillRoundedRect(batch, new SimRect(ledCx - ledSize * 0.5f, ledCy - ledSize * 0.5f, ledSize, ledSize), (int)(ledSize * 0.5f), ledCol);
            }

            // 6. Crisp glowing status outline around the CRT monitor chassis when active/selected/hovered
            if (activeMotion || selected || hovered || !hasCrtCasing)
            {
                Color border = activeMotion
                    ? (blink > 0 ? SimUiTokens.Danger : SimUiTokens.Amber)
                    : (selected ? SimUiTokens.Mint * 0.85f : (hovered ? SimUiTokens.Cyan * 0.78f : SimUiTokens.InnerBorder));
                int thick = activeMotion ? 2 : 1;
                DrawRoundedBorder(batch, new SimRect(r.X - 1f, r.Y - 1f, r.Width + 2f, r.Height + 2f), SimUiTokens.RadiusCamera + 2, border, thick);
            }
        }

        void DrawLive3DActorOverlay(UISpriteBatch batch, Texture2D px, SimRect r, float hudH, float dimFactor, SecurityEvent ev)
        {
            float contentX = r.X + 4f;
            float contentW = Math.Max(20f, r.Width - 8f);
            float contentY = r.Y + hudH;
            float contentH = Math.Max(20f, r.Height - hudH - 4f);

            float actorCx = contentX + contentW * cctvRenderer.ActorNormalizedX;
            float actorCy = contentY + contentH * cctvRenderer.ActorNormalizedY;
            float footY = contentY + contentH * cctvRenderer.ActorFootNormalizedY;

            // Grounded contact shadow ellipse directly beneath the 3D Sim's or pet's projected feet
            bool isPet = ev.Kind == SecurityEventKind.StrayCat || ev.Kind == SecurityEventKind.Raccoon;
            float shW = (isPet ? 21f : 36f) * SimLayout.Clamp(r.Width / 340f, 0.72f, 1.15f);
            float shH = isPet ? 6f : 9f;
            float shY = SimLayout.Clamp(footY - shH * 0.45f, contentY + 18f, r.Bottom - shH - 4f);
            FillRoundedRect(batch, new SimRect(actorCx - shW * 0.5f, shY, shW, shH), (int)(shH * 0.5f), Color.Black * 0.48f);

            // Composite the live 384x216 transparent _3DTargetScene RenderTarget with crisp night-vision phosphor tint
            var dest = new Rectangle(
                (int)Math.Round(contentX),
                (int)Math.Round(contentY),
                Math.Max(1, (int)Math.Round(contentW)),
                Math.Max(1, (int)Math.Round(contentH)));

            var nvTint = new Color(
                (byte)Math.Round(208f * dimFactor),
                (byte)Math.Round(255f * dimFactor),
                (byte)Math.Round(222f * dimFactor),
                (byte)255);
            batch.Draw(cctvRenderer.ActorTarget, dest, nvTint);

            // Motion-tracking corner reticle tightly framing the scaled 3D actor (compact for small pets!)
            float sc = SimLayout.Clamp(contentH / 128f, 0.65f, 1.15f);
            float bw = (isPet ? 38f : 54f) * sc;
            float bh = (isPet ? 28f : 70f) * sc;
            float bx = SimLayout.Clamp(actorCx - bw * 0.5f, r.X + 8f, r.Right - bw - 8f);
            float by = SimLayout.Clamp(actorCy - bh * 0.5f, contentY + 4f, r.Bottom - bh - 5f);
            DrawMotionBrackets(batch, px, bx, by, bw, bh, (isPet ? 8f : 10f) * sc, SimUiTokens.Amber * 0.88f);
        }

        void DrawTreeShadowSweep(UISpriteBatch batch, Texture2D px, SimRect r, float hudH, SecurityEvent ev)
        {
            float contentY = r.Y + hudH;
            float contentH = Math.Max(20f, r.Height - hudH - 6f);
            float sway = (float)Math.Sin(game.ElapsedSeconds * 2.1f) * 18f;
            float cx = r.X + r.Width * 0.5f + sway;

            // Organic multi-layered swaying tree canopy shadow sweeping across the real night lot
            var shadowDark = new Color(4, 16, 12, 165);
            var shadowSoft = new Color(8, 28, 20, 120);
            FillRoundedRect(batch, new SimRect(cx - 56f, contentY + contentH * 0.16f, 112f, 22f), 11, shadowSoft);
            FillRoundedRect(batch, new SimRect(cx - 38f + sway * 0.35f, contentY + contentH * 0.32f, 88f, 20f), 10, shadowDark);
            FillRoundedRect(batch, new SimRect(cx - 48f - sway * 0.25f, contentY + contentH * 0.50f, 96f, 18f), 9, shadowSoft);
            FillRoundedRect(batch, new SimRect(cx - 24f + sway * 0.4f, contentY + contentH * 0.64f, 64f, 14f), 7, shadowDark);

            // Motion-tracking brackets around the moving shadow
            float bw = 100f, bh = Math.Min(88f, contentH - 12f);
            float bx = SimLayout.Clamp(cx - bw * 0.5f, r.X + 10f, r.Right - bw - 10f);
            float by = contentY + (contentH - bh) * 0.5f;
            DrawMotionBrackets(batch, px, bx, by, bw, bh, 11f, SimUiTokens.Amber * 0.85f);
        }

        void DrawMotionBrackets(UISpriteBatch batch, Texture2D px, float bx, float by, float bw, float bh, float br, Color col)
        {
            FillRect(batch, px, bx, by, br, 2f, col);
            FillRect(batch, px, bx, by, 2f, br, col);
            FillRect(batch, px, bx + bw - br, by, br, 2f, col);
            FillRect(batch, px, bx + bw - 2f, by, 2f, br, col);
            FillRect(batch, px, bx, by + bh - 2f, br, 2f, col);
            FillRect(batch, px, bx, by + bh - br, 2f, br, col);
            FillRect(batch, px, bx + bw - br, by + bh - 2f, br, 2f, col);
            FillRect(batch, px, bx + bw - 2f, by + bh - br, 2f, br, col);
        }

        void DrawSilhouette(UISpriteBatch batch, Texture2D px, SimRect r, float floorY, float hudH, SecurityEvent ev)
        {
            float availH = Math.Max(46f, floorY - (r.Y + hudH + 6f));
            float sc = SimLayout.Clamp(availH / 92f, 0.54f, 1.05f);
            float sway = (float)Math.Sin(ev.MotionPhase * 4.5f) * 8f * sc;
            float cx = r.X + r.Width * 0.5f + sway;

            // Motion tracking brackets
            var bracketCol = ev.IsThreat ? SimUiTokens.Danger * 0.9f : SimUiTokens.Amber * 0.85f;
            float bw = 88f * sc, bh = 86f * sc, br = 12f * sc;
            float bx = cx - bw * 0.5f;
            float by = Math.Max(r.Y + hudH + 3f, floorY - bh + 2f);
            DrawMotionBrackets(batch, px, bx, by, bw, bh, br, bracketCol);

            var litGreen = new Color(168, 255, 198, 235);
            var darkSil = new Color(16, 34, 28, 245);
            var midGreen = new Color(86, 212, 146, 235);

            Action<float, float, float, float, Color> part = (ox, oy, pw, ph, col) =>
                FillRect(batch, px, cx + ox * sc, floorY + oy * sc, pw * sc, ph * sc, col);

            switch (ev.Kind)
            {
                case SecurityEventKind.Burglar:
                    part(-10, -74, 20, 8, darkSil);
                    part(-9, -66, 18, 16, litGreen);
                    part(-9, -61, 18, 5, darkSil);
                    for (int s = 0; s < 5; s++)
                        part(-14, -48 + s * 6, 28, 6, s % 2 == 0 ? darkSil : litGreen);
                    part(-11, -18, 8, 20, darkSil);
                    part(3, -18, 8, 20, darkSil);
                    part(-32, -52, 18, 24, midGreen);
                    part(16, -56, 4, 26, new Color(230, 248, 238, 245));
                    part(16, -58, 11, 4, new Color(230, 248, 238, 245));
                    break;

                case SecurityEventKind.Vandal:
                    part(-12, -72, 24, 22, darkSil);
                    part(-15, -50, 30, 32, midGreen);
                    part(-11, -18, 9, 20, darkSil);
                    part(2, -18, 9, 20, darkSil);
                    part(14, -46, 16, 6, midGreen);
                    part(28, -54, 6, 12, litGreen);
                    part(36, -64, 18, 16, new Color(255, 118, 168, 215));
                    part(42, -50, 14, 12, SimUiTokens.Cyan * 0.85f);
                    break;

                case SecurityEventKind.Prowler:
                    part(-22, -48, 18, 16, litGreen);
                    part(-26, -32, 30, 20, darkSil);
                    part(-20, -12, 24, 14, darkSil);
                    part(6, -30, 10, 5, litGreen);
                    part(16, -38, 28, 22, new Color(255, 245, 150, 115));
                    part(44, -44, 24, 34, new Color(255, 245, 150, 68));
                    break;

                case SecurityEventKind.PizzaDelivery:
                    part(-11, -74, 22, 7, new Color(210, 86, 86, 240));
                    part(4, -71, 12, 4, new Color(210, 86, 86, 240));
                    part(-9, -67, 18, 15, litGreen);
                    part(-13, -50, 26, 32, new Color(220, 245, 232, 235));
                    part(-11, -18, 8, 20, darkSil);
                    part(3, -18, 8, 20, darkSil);
                    part(10, -40, 24, 7, new Color(240, 215, 150, 240));
                    break;

                case SecurityEventKind.Raccoon:
                    part(-38, -24, 26, 22, new Color(92, 144, 124, 220));
                    part(-10, -24, 26, 18, midGreen);
                    part(-16, -22, 12, 12, litGreen);
                    part(-16, -18, 10, 4, darkSil);
                    for (int t = 0; t < 4; t++)
                        part(16 + t * 5, -20 - t * 2, 5, 6, t % 2 == 0 ? darkSil : litGreen);
                    break;

                case SecurityEventKind.StrayCat:
                    part(-14, -20, 24, 14, darkSil);
                    part(-22, -30, 14, 13, darkSil);
                    part(-22, -35, 4, 6, darkSil);
                    part(-12, -35, 4, 6, darkSil);
                    part(-20, -25, 3, 3, new Color(255, 240, 110, 255));
                    part(-14, -25, 3, 3, new Color(255, 240, 110, 255));
                    part(10, -36, 4, 20, darkSil);
                    part(14, -38, 6, 4, darkSil);
                    break;

                case SecurityEventKind.TreeShadow:
                    float lean = (float)(Math.Sin(ev.MotionPhase * 3.0f) * 10.0);
                    part(-6, -68, 12, 68, new Color(24, 68, 48, 210));
                    part(-34 + lean, -76, 68, 18, new Color(40, 116, 78, 185));
                    part(-24 + lean * 0.5f, -58, 54, 16, new Color(34, 98, 66, 180));
                    part(-38 + lean, -12, 76, 10, new Color(8, 22, 16, 165));
                    break;
            }
        }

        void DrawSummary(UISpriteBatch batch, Texture2D px, SecurityWatchLayout layout)
        {
            var o = finalOutcome ?? game.ComputeOutcome();

            // Main summary glass card
            DrawGlassCard(batch, layout.SummaryCard, SimUiTokens.CardBgAlt, SimUiTokens.Mint * 0.75f);

            // Big Simitone Grade Badge
            Color gradeBg = o.Grade == "S" ? new Color(10, 118, 76, 240)
                          : (o.Grade == "A" ? new Color(12, 92, 128, 240)
                          : (o.Grade == "B" ? new Color(142, 98, 20, 240) : SimUiTokens.DangerBg));
            Color gradeAccent = o.Grade == "S" ? SimUiTokens.Mint
                              : (o.Grade == "A" ? SimUiTokens.Cyan
                              : (o.Grade == "B" ? SimUiTokens.Amber : SimUiTokens.Danger));
            DrawGlassCard(batch, layout.GradeBadge, gradeBg, gradeAccent, SimUiTokens.RadiusModal);
            DrawFittedLine(batch, o.Grade, layout.GradeBadge, gradeStyle, SimTextAlign.Center);
            DrawFittedLine(batch, "SHIFT GRADE", layout.GradeCaptionRect, mutedStyle, SimTextAlign.Center);

            // Title & Wrapped Summary (strictly clamped inside SummaryBodyRect!)
            DrawFittedLine(batch, "NIGHT WATCH SHIFT REPORT  •  " + game.SimName, layout.SummaryTitleRect, mintStyle, SimTextAlign.Left);
            DrawFittedWrapped(batch, o.Summary, layout.SummaryBodyRect, bodyStyle, 3);

            // 3 Responsive Flex Stat Cards
            DrawStatCard(batch, layout.StatCards[0], "THREATS CAUGHT", o.ThreatsCaught.ToString(), SimUiTokens.Mint, statValMintStyle);
            DrawStatCard(batch, layout.StatCards[1], "FALSE ALARMS", o.FalseAlarmsCleared.ToString(), SimUiTokens.Cyan, statValCyanStyle);
            DrawStatCard(batch, layout.StatCards[2], "MISTAKES", o.Mistakes.ToString(), o.Mistakes == 0 ? SimUiTokens.Mint : SimUiTokens.Danger, o.Mistakes == 0 ? statValMintStyle : statValRedStyle);

            // Rewards Glass Card (2 balanced columns + motives line — zero collision!)
            DrawGlassCard(batch, layout.RewardsCard, SimUiTokens.CardBg, SimUiTokens.Cyan * 0.7f);
            DrawFittedLine(batch, "SHIFT REWARDS APPLIED TO " + game.SimName.ToUpperInvariant(), layout.RewardsHeaderRect, cyanStyle, SimTextAlign.Left);

            string cashStr = "+\u00a7" + o.BonusSimoleons + " Overtime Bonus";
            string perfStr = "Job Performance " + (o.JobPerformanceDelta >= 0 ? "+" : "") + o.JobPerformanceDelta;
            string motiveStr = "Energy " + (o.EnergyDelta >= 0 ? "+" : "") + o.EnergyDelta + "   •   Fun " + (o.FunDelta >= 0 ? "+" : "") + o.FunDelta;

            DrawFittedLine(batch, cashStr, layout.RewardCashRect, mintStyle, SimTextAlign.Left);
            DrawFittedLine(batch, perfStr, layout.RewardPerfRect, o.JobPerformanceDelta >= 0 ? mintStyle : redStyle, SimTextAlign.Left);
            DrawFittedLine(batch, motiveStr, layout.RewardMotivesRect, bodyStyle, SimTextAlign.Left);

            // Centered native green Simitone pill button
            claimRect = ToXnaRect(layout.ClaimBtn);
            DrawPillButton(batch, layout.ClaimBtn, "CLAIM REWARDS & RETURN", SimButtonVariant.Primary, hoverClaim);
        }

        void DrawStatCard(UISpriteBatch batch, SimRect rect, string label, string value, Color accent, TextStyle valueStyle)
        {
            DrawGlassCard(batch, rect, SimUiTokens.CardBg, accent * 0.8f);
            var inner = rect.Deflate(12f, 8f);
            var labelRect = new SimRect(inner.X, inner.Y, inner.Width, 17f);
            var valRect = new SimRect(inner.X, labelRect.Bottom + 3f, inner.Width, Math.Max(20f, inner.Bottom - (labelRect.Bottom + 3f)));
            DrawFittedLine(batch, label, labelRect, mutedStyle, SimTextAlign.Left);
            DrawFittedLine(batch, value, valRect, valueStyle, SimTextAlign.Left);
        }
    }
}
