using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Simitone.Conversations
{
    public class AiSubject
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("title")] public string Title;
        [JsonProperty("interest")] public int Interest;            // 0..7 Sims 1 interest slot
        [JsonProperty("skill"), JsonConverter(typeof(StringEnumConverter))] public SkillKind Skill = SkillKind.None;
        [JsonProperty("tone"), JsonConverter(typeof(StringEnumConverter))] public Tone ToneHint = Tone.Gentle;
        [JsonProperty("description")] public string Description;
    }

    // AI-generated Talk subjects. Replaces the 8 hardcoded Sims 1 interest labels everywhere Talk is used
    // (both manual Talk choice buttons and autonomous parallel room chatter).
    public class AiSubjectBank
    {
        static readonly HashSet<string> RawTs1Labels = new HashSet<string>(TopicLogic.Interests, StringComparer.OrdinalIgnoreCase);

        readonly List<AiSubject> subjects;
        public IList<AiSubject> All { get { return subjects; } }
        public int Count { get { return subjects.Count; } }
        public bool LoadedFromBank { get; private set; }

        AiSubjectBank(List<AiSubject> list, bool fromBank)
        {
            subjects = list;
            LoadedFromBank = fromBank;
        }

        public static bool IsValid(AiSubject s)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Title)) return false;
            if (s.Interest < 0 || s.Interest >= 8) return false;
            if (RawTs1Labels.Contains(s.Title.Trim())) return false;
            string reason;
            if (!DialogueBank.IsAcceptable(s.Title, out reason)) return false;
            if (s.Title.Trim().Length > 44) return false;   // fits cleanly on a pie/dialog button
            return true;
        }

        public static AiSubjectBank FromBank(DialogueBank bank)
        {
            var raw = bank == null ? null : bank.RawSubjects;
            if (raw != null && raw.Count > 0)
            {
                var valid = raw.Where(IsValid).GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                if (valid.Count >= 3) return new AiSubjectBank(valid, true);
            }
            return Builtin();
        }

        public static AiSubjectBank FromList(IEnumerable<AiSubject> list, bool includeBuiltinFallback = true)
        {
            var valid = (list ?? Enumerable.Empty<AiSubject>()).Where(IsValid)
                .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            if (valid.Count == 0 && includeBuiltinFallback) return Builtin();
            return new AiSubjectBank(valid, valid.Count > 0);
        }

        public static AiSubjectBank Builtin()
        {
            return new AiSubjectBank(DefaultSubjects(), false);
        }

        // Selects up to `count` diverse AI subjects tailored to the speaker, known listener state, skills, and memory.
        public List<AiSubject> SelectCandidates(RawSim speaker, RawSim listener, double knowledge, SocialMemory memory, int count, Random rng)
        {
            speaker = speaker ?? new RawSim();
            listener = listener ?? new RawSim();
            rng = rng ?? new Random();
            var spInt = speaker.Interests != null && speaker.Interests.Length >= 8 ? speaker.Interests : new short[8];
            var lsInt = listener.Interests != null && listener.Interests.Length >= 8 ? listener.Interests : new short[8];

            var scored = new List<Tuple<AiSubject, double>>();
            foreach (var s in subjects)
            {
                double score = 0.0;
                score += (spInt[s.Interest] / 1000.0) * 0.45;
                bool knowsInterest = knowledge >= 0.55 || (memory != null && memory.HasLearnedInterest(speaker, listener, s.Interest));
                if (knowsInterest) score += (lsInt[s.Interest] / 1000.0) * 0.40 * Math.Max(0.5, knowledge);

                if (s.Skill != SkillKind.None)
                {
                    if (speaker.SkillValue(s.Skill) >= 300) score += 0.20 * (speaker.SkillValue(s.Skill) / 1000.0);
                    bool knowsSkill = knowledge >= 0.45 || (memory != null && memory.HasLearnedSkill(speaker, listener, s.Skill));
                    if (knowsSkill && listener.SkillValue(s.Skill) >= 300) score += 0.25 * (listener.SkillValue(s.Skill) / 1000.0);
                }

                if (memory != null) score += memory.SubjectAdjustment(speaker, listener, s.Id);
                // small tie-breaker jitter so conversations stay varied across sessions
                score += rng.NextDouble() * 0.14;
                scored.Add(Tuple.Create(s, score));
            }

            // Pick top diverse subjects (prefer distinct interest buckets first so choices feel varied)
            var ordered = scored.OrderByDescending(x => x.Item2).ToList();
            var picks = new List<AiSubject>();
            var usedInterests = new HashSet<int>();
            foreach (var item in ordered)
            {
                if (picks.Count >= count) break;
                if (usedInterests.Add(item.Item1.Interest)) picks.Add(item.Item1);
            }
            foreach (var item in ordered)
            {
                if (picks.Count >= count) break;
                if (!picks.Any(p => p.Id == item.Item1.Id)) picks.Add(item.Item1);
            }
            return picks;
        }

        // Picks 3 distinct AI subjects to show in the manual Talk dialog, using Laya ranking when available.
        public List<AiSubject> OfferForManualTalk(RawSim speaker, RawSim listener, LayaClient laya, SocialMemory memory, Random rng)
        {
            speaker = speaker ?? new RawSim();
            listener = listener ?? new RawSim();
            rng = rng ?? new Random();
            double knowledge = Math.Max(0.2, Math.Min(1.0, (speaker.LtrToTarget + 20) / 120.0));
            var candidates = SelectCandidates(speaker, listener, knowledge, memory, Math.Min(6, subjects.Count), rng);
            if (candidates.Count <= 3) return candidates;

            if (laya != null && laya.Available)
            {
                var sp = TraitProfile.From(speaker);
                var lp = TraitProfile.From(listener);
                var ctx = sp.Summary + " They are starting a conversation with " + lp.Name + ". " + lp.Summary;
                var spSkills = speaker.TopSkills(300);
                if (spSkills.Length > 0) ctx += " " + sp.Name + " is skilled in " + string.Join(", ", spSkills) + ".";
                var lsSkills = listener.TopSkills(300);
                if (lsSkills.Length > 0) ctx += " " + lp.Name + " is skilled in " + string.Join(", ", lsSkills) + ".";

                var criteria = candidates.Select(c => c.Id + "|" + c.Title + ": " + c.Description).ToArray();
                var probs = laya.ScoreChoices(ctx, "Which conversation topic would spark the best talk between these two Sims?", criteria);
                if (probs != null && probs.Count > 0)
                {
                    candidates = candidates
                        .OrderByDescending(c => (probs.ContainsKey(c.Id) ? probs[c.Id] : 0.0) + (memory != null ? memory.SubjectAdjustment(speaker, listener, c.Id) * 0.5 : 0.0))
                        .ToList();
                }
            }
            return candidates.Take(3).ToList();
        }

        // 48 AI-authored subjects (6 per Sims 1 interest slot 0..7), covering all 6 skills and everyday Sim life.
        public static List<AiSubject> DefaultSubjects()
        {
            return new List<AiSubject>
            {
                // 0: Travel & Toys
                S("coastal_roadtrip", "Coastal Road Trips", 0, SkillKind.None, Tone.Playful, "scenic drives, roadside diners, and spontaneous weekend escapes"),
                S("street_food_markets", "Night Street Food Markets", 0, SkillKind.Cooking, Tone.Gentle, "tasting exotic snacks and secret recipes while traveling"),
                S("vintage_board_games", "Vintage Board Games", 0, SkillKind.Logic, Tone.Playful, "clever strategy games, rare collectibles, and game-night rivalries"),
                S("model_train_builds", "Miniature Model Builds", 0, SkillKind.Mechanical, Tone.Gentle, "tinkering with tiny clockwork trains and detailed dioramas"),
                S("hidden_island_coves", "Hidden Island Coves", 0, SkillKind.Body, Tone.Bold, "swimming to secluded beaches and exploring untouched trails"),
                S("dream_vacation_spots", "Dream Vacation Spots", 0, SkillKind.Charisma, Tone.Gentle, "trading bucket-list destinations and funny airport stories"),

                // 1: Sci-Fi, Mystery & Thrills (Violence & Aliens slot)
                S("late_night_stargazing", "Late-Night Stargazing", 1, SkillKind.Logic, Tone.Gentle, "spotting strange lights in the sky and debating life beyond SimCity"),
                S("detective_mysteries", "Classic Detective Mysteries", 1, SkillKind.Logic, Tone.Playful, "unraveling whodunits, plot twists, and clever alibis"),
                S("sci_fi_gadgets", "Retro Sci-Fi Gadgets", 1, SkillKind.Mechanical, Tone.Playful, "wild inventions, laser gizmos, and futuristic home robots"),
                S("action_movie_stunts", "Action Movie Stunts", 1, SkillKind.Body, Tone.Bold, "daring movie choreography, martial arts, and cinema thrills"),
                S("strange_neighborhood_lights", "Strange Lights Over Town", 1, SkillKind.Charisma, Tone.Playful, "whispered rumors about UFO sightings and mysterious neighbors"),
                S("galactic_novels", "Space Opera Novels", 1, SkillKind.Creativity, Tone.Gentle, "epic interstellar worlds, starships, and imaginative storytelling"),

                // 2: Community, Politics & Pets
                S("town_hall_rumors", "Town Hall Rumors", 2, SkillKind.Charisma, Tone.Bold, "neighborhood debates, mayor decisions, and local policies"),
                S("stray_cat_rescue", "Neighborhood Pet Stories", 2, SkillKind.None, Tone.Gentle, "adorable stray kittens, clever dogs, and pet antics"),
                S("community_garden_plan", "Community Garden Plans", 2, SkillKind.Cooking, Tone.Gentle, "growing fresh herbs together and sharing neighborhood harvests"),
                S("public_speaking_tips", "Winning Over a Crowd", 2, SkillKind.Charisma, Tone.Bold, "persuasive speeches, charm, and handling tough debates"),
                S("historic_architecture", "Preserving Old Houses", 2, SkillKind.Mechanical, Tone.Gentle, "restoring neighborhood landmarks and historic craftsmanship"),
                S("civic_fundraisers", "Neighborhood Fundraisers", 2, SkillKind.Charisma, Tone.Playful, "organizing lively block parties and charity bake sales"),

                // 3: Nostalgia, Learning & Campus (Sixties & School slot)
                S("retro_vinyl_era", "Retro Sixties Style", 3, SkillKind.Creativity, Tone.Playful, "groovy fashion, vintage posters, and carefree nostalgia"),
                S("favorite_old_books", "Unforgettable Old Books", 3, SkillKind.Logic, Tone.Gentle, "timeless stories, late-night reading, and favorite authors"),
                S("weird_science_facts", "Mind-Bending Science Facts", 3, SkillKind.Logic, Tone.Playful, "surprising physics puzzles, experiments, and odd trivia"),
                S("kitchen_chemistry", "The Science of Baking", 3, SkillKind.Cooking, Tone.Gentle, "how sourdough rises, caramelization, and culinary experiments"),
                S("workshop_projects", "DIY Home Projects", 3, SkillKind.Mechanical, Tone.Bold, "fixing stubborn appliances and building sturdy furniture"),
                S("school_day_memories", "Wild School Memories", 3, SkillKind.Charisma, Tone.Playful, "funny classroom mishaps, old teachers, and nostalgic pranks"),

                // 4: Weather, Cozy Home & Seasons
                S("rainy_day_baking", "Rainy-Day Comfort Food", 4, SkillKind.Cooking, Tone.Gentle, "warm soup, freshly baked bread, and cozy stormy afternoons"),
                S("thunderstorm_watching", "Watching Summer Storms", 4, SkillKind.None, Tone.Gentle, "rolling thunder, cool breezes, and sitting on the porch"),
                S("solar_home_tweaks", "Draft-Proofing the House", 4, SkillKind.Mechanical, Tone.Gentle, "weatherproofing windows, cozy fireplaces, and smart home fixes"),
                S("autumn_color_walks", "Crisp Autumn Evenings", 4, SkillKind.Creativity, Tone.Gentle, "golden sunsets, changing leaves, and seasonal inspiration"),
                S("morning_coffee_rituals", "Morning Coffee Rituals", 4, SkillKind.Cooking, Tone.Playful, "perfect espresso, sleepy sunrises, and starting the day right"),
                S("wild_forecast_misses", "Wild Weather Forecasts", 4, SkillKind.Charisma, Tone.Playful, "laughing about surprise downpours and overly dramatic meteorologists"),

                // 5: Sports, Fitness & Competition
                S("marathon_training", "Morning Run Routines", 5, SkillKind.Body, Tone.Bold, "building stamina, beating personal bests, and staying energized"),
                S("championship_highlights", "Last Night's Big Match", 5, SkillKind.Charisma, Tone.Playful, "unbelievable comeback plays, rivalries, and locker-room drama"),
                S("chess_tactics", "Tricky Chess Openings", 5, SkillKind.Logic, Tone.Bold, "outsmarting opponents, bold sacrifices, and competitive focus"),
                S("protein_smoothie_ideas", "Post-Workout Recipes", 5, SkillKind.Cooking, Tone.Gentle, "tasty high-energy meals and favorite healthy snacks"),
                S("bicycle_tuning", "Tuning Up Racing Bikes", 5, SkillKind.Mechanical, Tone.Playful, "gears, smooth rides, and tinkering with athletic gear"),
                S("pickup_basketball", "Weekend Pickup Games", 5, SkillKind.Body, Tone.Playful, "friendly neighborhood competition and bragging rights"),

                // 6: Music, Art & Performance
                S("indie_live_gigs", "Secret Local Concerts", 6, SkillKind.Creativity, Tone.Playful, "tiny acoustic venues, unforgettable encores, and discovering bands"),
                S("piano_improvisation", "Late-Night Piano Jams", 6, SkillKind.Creativity, Tone.Gentle, "melodies, songwriting sparks, and learning new instruments"),
                S("vinyl_sound_gears", "Restoring Turntables", 6, SkillKind.Mechanical, Tone.Gentle, "warm analog sound, speaker setups, and vintage audio gear"),
                S("stage_charisma", "Commanding a Stage", 6, SkillKind.Charisma, Tone.Bold, "winning over an audience, comedic timing, and live energy"),
                S("dance_floor_anthems", "All-Time Dance Anthems", 6, SkillKind.Body, Tone.Playful, "irresistible rhythms, throwback hits, and terrible dance moves"),
                S("painting_color_moods", "Painting from Memory", 6, SkillKind.Creativity, Tone.Gentle, "brushwork, bold palettes, and turning feelings into art"),

                // 7: Outdoors, Nature & Exploration
                S("ridge_trail_hikes", "Mountain Trail Hikes", 7, SkillKind.Body, Tone.Bold, "steep climbs, fresh pine air, and sweeping hilltop views"),
                S("campfire_cooking", "Campfire Cooking Tricks", 7, SkillKind.Cooking, Tone.Playful, "cast-iron skillets, toasted marshmallows, and meals under the stars"),
                S("backyard_botany", "Rare Backyard Plants", 7, SkillKind.Logic, Tone.Gentle, "propagating orchids, pollinators, and curious garden ecology"),
                S("sketching_landscapes", "Sketching in the Park", 7, SkillKind.Creativity, Tone.Gentle, "capturing sunlight on trees and peaceful afternoon sketches"),
                S("starlight_camping", "Weekend Camping Gear", 7, SkillKind.Mechanical, Tone.Playful, "handy lanterns, pitching tents fast, and outdoor survival hacks"),
                S("botanical_picnics", "Sunny Park Picnics", 7, SkillKind.Charisma, Tone.Gentle, "easygoing afternoons outside, good company, and fresh air"),
            };
        }

        static AiSubject S(string id, string title, int interest, SkillKind skill, Tone tone, string desc)
        {
            return new AiSubject { Id = id, Title = title, Interest = interest, Skill = skill, ToneHint = tone, Description = desc };
        }
    }
}
