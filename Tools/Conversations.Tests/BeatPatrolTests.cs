using System;
using System.Collections.Generic;
using System.Linq;
using Simitone.Conversations;
using Xunit;

namespace Conversations.Tests
{
    public class BeatPatrolTests
    {
        [Fact]
        public void Registry_MarksLevel3BeatPatrolAsImplemented()
        {
            var desc = CareerDispatchRegistry.Resolve("Law Enforcement", "Patrol Officer", 2);
            Assert.NotNull(desc);
            Assert.Equal(CareerMiniGameKind.BeatPatrol, desc.Kind);
            Assert.True(desc.IsImplemented);
            Assert.Equal(2, desc.PromotionLevel);
        }

        [Fact]
        public void PickPatrolLot_RotatesAcrossDowntownOldTownAndNeighbors_ExcludingHomeLot()
        {
            var available = new List<int> { 1, 2, 7, 10, 21, 22, 23, 50, 51, 55 };
            var seenZones = new HashSet<PatrolLotZone>();
            var seenLots = new HashSet<int>();

            for (int seed = 0; seed < 12; seed++)
            {
                PatrolLotZone zone;
                string sectorName;
                int lotId = BeatPatrolGame.PickPatrolLot(homeHouseId: 7, availableHouseIds: available, seed: seed, out zone, out sectorName);
                Assert.NotEqual(7, lotId);
                Assert.Contains(lotId, available);
                Assert.False(string.IsNullOrWhiteSpace(sectorName));
                seenZones.Add(zone);
                seenLots.Add(lotId);
            }

            Assert.Contains(PatrolLotZone.Downtown, seenZones);
            Assert.Contains(PatrolLotZone.OldTown, seenZones);
            Assert.Contains(PatrolLotZone.NeighborHouse, seenZones);
            Assert.True(seenLots.Count >= 6);
        }

        [Fact]
        public void BurglarProwler_SneakApprehendIsPerfect_ConfrontGood_WarnMiss()
        {
            var gSneak = new BeatPatrolGame("Bob", logicSkillLevel: 4, charismaSkillLevel: 3, patrolLotId: 21, PatrolLotZone.Downtown, "DOWNTOWN PLAZA • LOT 21", seed: 10);
            Assert.True(gSneak.IsBriefing);
            Assert.True(gSneak.BeginPatrol());
            Assert.False(gSneak.IsBriefing);

            var burglarActions = BeatPatrolGame.GetActionsForIncident(PatrolIncidentKind.BurglarProwler, false);
            var sneakOpt = burglarActions.FirstOrDefault(a => a.Action == PatrolActionKind.SneakApprehend);
            Assert.NotNull(sneakOpt);
            Assert.True(sneakOpt.UsesSneak);
            Assert.Equal("patrol_apprehend", sneakOpt.ResolveClip);

            var r1 = gSneak.ResolveIncident(0, PatrolActionKind.SneakApprehend);
            Assert.Equal(PatrolActionRating.Perfect, r1);
            Assert.Equal(100, gSneak.Score);
            Assert.Equal(1, gSneak.StealthArrests);
            Assert.Equal(35, gSneak.ExtraFineSimoleons);

            var gLoud = new BeatPatrolGame("Bob", 2, 2, 21, PatrolLotZone.Downtown, "DOWNTOWN", seed: 10, startInBriefing: false);
            var r2 = gLoud.ResolveIncident(0, PatrolActionKind.ConfrontLoud);
            Assert.Equal(PatrolActionRating.Good, r2);
            Assert.Equal(65, gLoud.Score);
            Assert.Equal(0, gLoud.StealthArrests);

            var gWarn = new BeatPatrolGame("Bob", 2, 2, 21, PatrolLotZone.Downtown, "DOWNTOWN", seed: 10, startInBriefing: false);
            var r3 = gWarn.ResolveIncident(0, PatrolActionKind.WarnCitizen);
            Assert.Equal(PatrolActionRating.Miss, r3);
            Assert.Equal(15, gWarn.Score);
        }

        [Fact]
        public void FullSRankPatrol_ComputesRewardsAndRelationshipDeltas()
        {
            var g = new BeatPatrolGame(
                officerName: "Bob",
                logicSkillLevel: 4,
                charismaSkillLevel: 4,
                patrolLotId: 21,
                lotZone: PatrolLotZone.Downtown,
                sectorDisplayName: "DOWNTOWN PLAZA • LOT 21",
                disturbanceCitizenName: "Bella Goth",
                disturbanceNeighborId: 12,
                innocentCitizenName: "Mortimer Goth",
                innocentNeighborId: 13,
                seed: 42,
                startInBriefing: false);

            Assert.Contains("LOGIC TIP", g.ActiveRadioHint());

            // 1. Stealth catch Burglar
            Assert.Equal(PatrolActionRating.Perfect, g.ResolveIncident(0, PatrolActionKind.SneakApprehend));
            Assert.False(g.IsFinished);

            // 2. Check in politely with innocent bystander (+Rel bonus)
            Assert.Equal(PatrolActionRating.Good, g.ResolveIncident(3, PatrolActionKind.CheckInCitizen));
            Assert.False(g.IsFinished);

            // 3. Warn Noise Disturbance (+Rel)
            Assert.Equal(PatrolActionRating.Perfect, g.ResolveIncident(1, PatrolActionKind.WarnCitizen));
            Assert.False(g.IsFinished);

            // 4. Inspect & log Civic Hazard -> finishes all 3 required targets
            Assert.Equal(PatrolActionRating.Perfect, g.ResolveIncident(2, PatrolActionKind.InspectHazard));
            Assert.True(g.IsFinished);

            var outcome = g.ComputeOutcome();
            Assert.Equal("S", outcome.Grade);
            Assert.Equal(3, outcome.ResolvedTargets);
            Assert.Equal(1, outcome.StealthArrests);
            Assert.True(outcome.Reward.BonusSimoleons >= 145); // 110 S-rank + 35 stealth arrest bonus
            Assert.Equal(55, outcome.Reward.LogicXpDelta);
            Assert.Equal(45, outcome.Reward.CharismaXpDelta);
            Assert.Equal(2, outcome.RelationshipDeltas.Count);
            Assert.All(outcome.RelationshipDeltas, d => Assert.True(d.DailyDelta > 0));
        }

        [Fact]
        public void TicketAndWrongfulCitation_ApplyFinesAndNegativeRelationships()
        {
            var g = new BeatPatrolGame(
                "Bob", 1, 1, 50, PatrolLotZone.OldTown, "OLD TOWN",
                disturbanceCitizenName: "Claire", disturbanceNeighborId: 22,
                innocentCitizenName: "Marge", innocentNeighborId: 23,
                seed: 7, startInBriefing: false);

            // Ticket on noise disturbance: Good, +$25 fine, -6 Rel
            var rNoise = g.ResolveIncident(1, PatrolActionKind.IssueTicket);
            Assert.Equal(PatrolActionRating.Good, rNoise);
            Assert.Equal(25, g.ExtraFineSimoleons);

            // Wrongful ticket on innocent citizen: Miss, -25 pts, -10 Rel
            var rInno = g.ResolveIncident(3, PatrolActionKind.IssueTicket);
            Assert.Equal(PatrolActionRating.Miss, rInno);
            Assert.Equal(60, g.Score); // 85 - 25

            g.FinishShiftEarly();
            Assert.True(g.IsFinished);

            var outcome = g.ComputeOutcome();
            Assert.Equal(2, outcome.RelationshipDeltas.Count);
            Assert.Contains(outcome.RelationshipDeltas, d => d.NeighborId == 22 && d.DailyDelta == -6);
            Assert.Contains(outcome.RelationshipDeltas, d => d.NeighborId == 23 && d.DailyDelta == -10);
        }

        [Fact]
        public void CruiserActions_ShowReturnToCruiserOnlyWhenOnFoot()
        {
            var inCar = BeatPatrolGame.GetActionsForCruiser(officerInCruiser: true);
            Assert.Single(inCar);
            Assert.Equal(PatrolActionKind.EndShift, inCar[0].Action);

            var onFoot = BeatPatrolGame.GetActionsForCruiser(officerInCruiser: false);
            Assert.Equal(2, onFoot.Count);
            Assert.Equal(PatrolActionKind.ReturnToCruiser, onFoot[0].Action);
            Assert.Equal(PatrolActionKind.EndShift, onFoot[1].Action);
        }
    }
}
