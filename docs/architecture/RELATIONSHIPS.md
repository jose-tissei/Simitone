# How the Sims 1 social and relationship logic works

Source: the game's own scripts, decoded with `Tools/ScriptDump` from a Sims Complete Collection install, plus the engine
code in `FreeSO/TSOClient/tso.simantics`. The engine only stores and changes numbers; every rule below is in the
original scripts. Parts that are interpretation rather than direct reading are marked *(inferred)*.

## 1. The numbers

Each Sim keeps four slots per other Sim, in each direction (A to B is separate from B to A).

| Slot | Meaning | Range |
|---|---|---|
| 0 | STR, short-term (daily) relationship | -100..100 |
| 1 | flag: 1 once STR has passed 70 | 0/1 |
| 2 | LTR, long-term (lifetime) relationship | -100..100 |
| 3 | flag: same, for LTR | 0/1 |

Thresholds (`Global Tuning`, BCON 258): **50 = friend**, **70 = lasting relationship**.
"Friend" is mutual: both directions at 50 or above (the scripts differ between `>` and `>=` 50).
Simitone's relationship panel draws slot 0 and slot 2 as its two bars.
Stored per pair of neighbours in the neighbourhood save (`NBRS` chunk), or per object on a lot.

## 2. What happens when a social changes STR

Every social ends in a script that adds a delta to the actor's STR toward the other Sim (clamped to +-100) and a delta to
the social motive. Then, in this order:
1. STR crossing 70 upwards sets slot 1 (headline and sound); falling below 70 clears it (sound). Not for children.
2. The family-friend count is taken before and after. A change shows the "new family friend" or "lost family friend" dialog.
3. A floating icon over the Sim: delta > 10, > 0, > -9, or lower give four different icons.
4. If the flag argument is 1 (romantic socials), the gender-preference counters are incremented.
5. In the person-level version (`SocialGlobals` 8201/8206/8280), the STR delta also moves LTR: +4..+7 gives LTR +1,
   +8 or more gives +2, -4..-7 gives -1, -8 or less gives -2.
   *I did not find an LTR step in the social-object versions (`SocialInteractions` 4137/4138).*

## 3. Per-interaction deltas

Source: the `Tuning <name>` tables in `SocialInteractions.iff` (BCON 4102-4121), read through the `<name> - Person A/B` scripts.
Person A is the one who starts it, B the one it is done to. Each cell is `social motive / STR change`, applied to that
Sim's own relationship toward the other.

How the outcome is chosen (read for Hug, Kiss and Insult; the others follow the same pattern *(inferred)*):
- **Hug:** score = target's relationship toward you x2 + target's mood + target's outgoing personality / 10, minus 30 for same sex.
  Above 205 = outcome 3 (best), above 110 = outcome 2, else outcome 1 (rejected).
- **Kiss:** score = target's relationship toward you + target's mood, minus 25 for same sex.
  Above 130 = outcome 3, above 95 = outcome 2, else outcome 1. Outcomes 2 and 3 pass the romantic flag.
- **Insult:** score = target's nice personality / 10 + target's mood. Below 20 = outcome 3, below 70 = outcome 2, else outcome 1.
- An optional in-game dialog ("social outcome choice") can force the outcome.
- For tables with two columns, outcome numbering follows the order of the branches in the script. Which is "success" and
  which is "refused" is confirmed for Hug and Kiss only.

| Interaction | Outcome 1 | Outcome 2 | Outcome 3 |
|---|---|---|---|
| Apologize | A 0 / -10, B 0 / -10 | A +15 / +10, B +15 / +10 | - |
| Brag | A 0 / -5, B 0 / -5 | A +13 / +5, B +7 / +5 | - |
| Cheer Up | A +7 / +5, B +10 / +10 | A +5 / 0, B +5 / 0 | A 0 / -3, B 0 / -10 |
| Compliment | A 0 / -10, B 0 / -7 | A +5 / +5, B +11 / +5 | - |
| Entertain | A 0 / -15, B 0 / -7 | A +7 / +4, B +13 / +8 | - |
| Flirt | A -17 / -10, B 0 / -10 | A 0 / -5, B 0 / 0 | A +13 / +5, B +13 / +10 |
| Give Gift | A 0 / -15, B 0 / -5 | A +7 / +5, B +13 / +10 | - |
| Hug | A 0 / -10, B 0 / -10 | A +7 / +2, B +7 / +4 | A +15 / +7, B +15 / +8 |
| Insult | A 0 / +5, B -13 / -12 | A +3 / 0, B -5 / -5 | A +7 / -10, B -7 / -14 |
| Joke | A 0 / -6, B 0 / -7 | A +7 / +2, B +7 / +3 | A +13 / +5, B +13 / +7 |
| Kiss | A +5 / -15, B 0 / -10 | A +10 / +5, B +10 / +5 | A +20 / +12, B +20 / +12 |
| Scare | A 0 / -5, B 0 / -10 | A +10 / +5, B +8 / +5 | - |
| Slap | A -7 / -10, B +7 / -15 | A +3 / 0, B -17 / -20 | - |
| Tease | A 0 / -4, B -7 / -13 | A +7 / +5, B +7 / +5 | - |
| Tickle | A 0 / -5, B 0 / -8 | A +13 / +5, B +13 / +8 | - |
| Back Rub | A +7 / +5, B +13 / +9 | A 0 / -7, B 0 / -10 | - |
| Dance With | A 0 / -5, B 0 / -5 | A +13 / +8, B +13 / +10 | - |

Notes: Back Rub is stored interleaved (first the social values, then the STR values); I regrouped it.
Conversation (Talk) is handled separately: `adjust relationship by topic interest` gives STR **+3** per exchange when the
topic interest is 4 or higher and **-3** when it is lower, and +5 or +3 social motive *(inferred from tuning slots 2-5 and 0-1)*.
Not mapped: Stereo Dance, Move In and Propose (separate tables), the phone and sofa objects, jealousy, marriage.

## 4. Daily decay

`VisitGenerator.iff` (the neighbourhood scheduler object) runs `decrease relationships` once a day at **hour 16 (4 PM)**
*(assuming Global[0] is the hour of day)*. It loops over every neighbour's relationships and moves **STR (slot 0) two
points toward zero** (`Tuning[4]` = 2), without crossing zero. If STR is then below 70, slot 1 is cleared. LTR (slot 2)
is not touched. The same pass also picks the neighbour with the highest relationship as a visitor candidate.
It runs only on residential lots.

## 5. Starting values and gates

- Members of the same family start with STR from `Tuning - resident start` (20, if the table index was resolved correctly).
- `add LTR if needed` gives LTR entries to pairs that only have STR and copies STR into LTR.
- Each interaction has a `... TEST` script that gates it. Example: `Hug Romantic TEST` calls a helper that is true only when STR
  is at least 40 and LTR is at least 40 (a missing LTR counts as 0), and then needs mood above 35 and attraction.
- The dialog that lets a player pick a social's outcome by hand (3 buttons) exists in all 23 social scripts, but only appears
  when bit 2 of `Global[25]` is set (a debug/cheat flag). The outcome is stored in attribute 9 of the social object (1, 2 or 3).

## 6. Not yet read

Jealousy start/end values, marriage and move-in outcomes, the rest of the `TEST` thresholds, the phone and sofa paths,
and where the social-object path updates LTR.
