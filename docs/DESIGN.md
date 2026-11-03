# Project Volvox — Design Doc, Review & First MVP

This file has three parts:

1. **Developer review** of the original *Homunculus* design doc (v0.1)
2. **Tech direction & first tiny MVP** — C# desktop, one organism, four needs
3. **Appendix: the original design doc**, unchanged

---

# Part 1 — Developer review

**Short version:** a strong vision document, not yet a buildable design. The ideas are good; the "MVP" in §12 is really a v1.0, and some core mechanics are underspecified or contradict each other.

## What's genuinely good

- **The constraints in §14 are the best part.** No LLM brains, genetics kept separate from culture, simulation decoupled from rendering, seeded RNG, bounded logs. These are the decisions that sink ALife projects when they're made late.
- **Visited / explored / known / taught (§7.3)** is the most original idea. A map of *what an organism believes* drawn over *what's actually there* is great visual storytelling, and cheap to build.
- **Food → nutrients → energy with a delay** is the right instinct. Delays produce interesting failures.
- **Logging as gameplay** is correct for an observation game. Without it nobody understands what they're watching.

## Where it breaks down

1. **The MVP isn't minimal.** Six subsystems, plus snapshot persistence, event replay and offline catch-up. That's months of work before learning whether the core idea is fun. The real risk is design, not technology.
2. **The intelligence threshold is a scripted gate.** "Reach X intelligence, unlock reproduction" contradicts *emergence over scripting*. It's also unclear what raises cognitive capabilities during a lifetime — experience (needs growth rules) or genes (then it doesn't "develop"). §9's "average intelligence" quietly reintroduces the single bar §5 says to avoid.
3. **Knowledge only matters if decisions consume it.** The knowledge database is specified carefully; the decision-maker that reads it (how confidence trades off against distance and urgency, when to trust memory vs. explore) is one sentence. That decision-maker is the heart of the game.
4. **In a static world, culture trivially wins.** If food never moves, inherited knowledge is never wrong, and "outdated knowledge" never happens. Resource dynamics (depletion, regrowth elsewhere, drift) matter more to the design goals than oxygen, body temperature or circulation. Biology is over-specified, environment under-specified.
5. **Balancing is the real hard problem and isn't mentioned.** Every ALife sim lives in a narrow band between instant extinction and population explosion. You need a headless fast-forward runner and parameter sweeps from day one — that tooling matters more than heatmaps.
6. **Persistence is architecturally confused (§10.2).** If the sim is deterministic, restoring is "load snapshot, step forward". Events are a history log, not a source of truth. Mixing event sourcing with snapshots adds duplicate-processing bugs for no gain. Offline catch-up then falls out of the headless runner for free (with a cap).
7. **Single-organism start means frequent game-overs.** One bad wander = extinction. Needs a reseed rule. Also, one parent means asexual reproduction, which the doc never states.
8. **The first ten minutes are a dot moving.** The "compelling moments" in §15 only land if the game surfaces them — highlight the first meal, flash the first birth. Observation games need a narrator layer.
9. **The UI is half the project.** Six heatmap layers, generation/period filters, collective knowledge map, inspector, timeline. Budget honestly or cut hard.

## Original MVP suggestion (from the review)

Prove one claim: **an offspring taught by its parent survives better than one that isn't.**

- 64×64 grid; food patches that deplete, regrow and occasionally relocate; 2–3 water tiles.
- Organism: energy, water, age; stomach → energy delay; sense radius.
- Memory as `{type, x, y, confidence, lastSeen}`; confidence decays and drops on "arrived, found nothing".
- Utility-based decisions with a one-line "why" in the inspector.
- Reproduction by splitting (no intelligence gate); child gets top-K memories with noise; genes: sense radius, metabolic rate, teaching budget K.
- Reseed on extinction.
- **Culture ON/OFF toggle + headless runner** over 20 seeds — if ON doesn't clearly win, the premise needs work.
- Minimal visuals: dots, selected trail, believed-vs-actual markers, plain event log.

That is now **MVP-1** below. The user chose to start even smaller: **MVP-0**.

---

# Part 2 — Tech direction & first tiny MVP

## Is C# reasonable? Yes.

C# is a good fit — arguably better than the browser plan for this kind of project:

| Concern | C# / .NET 8 |
|---|---|
| Deterministic sim, fixed timestep | Easy. Own `Random` wrapper (e.g. xoshiro/PCG) so state can be saved. |
| Fast headless runs for balancing | Excellent — a console runner can do millions of ticks per second for one organism. |
| Persistence | A JSON snapshot file replaces IndexedDB. No event replay needed. |
| Offline catch-up | On load: elapsed real time → ticks, run headless with a cap, then resume. No tab-throttling problems. |
| Desktop UI | WinForms with custom-drawn panels is the fastest path to a crisp, technical look. |

**What changes from the original doc:** §10's browser/IndexedDB specifics are dropped. Everything else in §10 still applies (fixed timestep, seeded RNG, speed controls, catch-up with safeguards).

### Recommended stack

- **.NET 8, WinForms**, double-buffered custom `Panel` for the map, GDI+ drawing. No game engine — one organism and a grid don't need one.
  - Alternative if cross-platform matters later: **Avalonia**. Not worth it for MVP-0.
- Single-file self-contained `win-x64` publish → one `.exe`.

### Solution layout

```text
Volvox.sln
├── Volvox.Sim/        class library, no UI references (engine)
│   ├── World.cs           grid, resources, regrowth
│   ├── Organism.cs        needs, body, position
│   ├── Mind.cs            memory + decision-making
│   ├── SimClock.cs        fixed timestep, tick counter
│   ├── Rng.cs             seedable, serializable RNG
│   ├── EventLog.cs        structured events
│   └── Snapshot.cs        JSON save/load
├── Volvox.App/        WinForms desktop app (render + log UI)
└── Volvox.Runner/     console app: run N seeds headless, print stats
```

The `Sim` project must never reference `App`. That single rule keeps the engine reusable, testable and fast-forwardable.

### Visual direction — "subtle technical"

- Graph-paper map: off-white or very dark slate background, fine 1-unit grid lines, stronger lines every 10 units, coordinate labels on the edges.
- Monospace UI type (Cascadia Mono / Consolas). Muted palette; one accent colour for the organism.
- Resources as small labelled glyphs, not sprites (`◇ F`, `○ W`, etc.).
- Organism trail as a thin polyline that fades with age.
- Need gauges as thin horizontal bars with numeric readouts, lab-instrument style.
- Log panel looks like an instrument readout: `T+000214  COG  discovered object #3 at (41,12)`.

## MVP-0 — "One organism, four needs, figure it out"

**Goal:** watch a single organism that knows nothing either die or work out how to stay alive, and be able to read *why* from the log.

### World

- Planar graph-paper space, e.g. **200×200 units**, continuous positions (not tile-locked).
- Organism spawns near the centre.
- **One source per need, only reasonably far away** and in different directions, e.g. 25–45 units out. Distance alone isn't the challenge; **effort** is.
- **Decoy objects** scattered around (rocks, inedible plants, maybe one mildly harmful). This is what makes "no instructions" real — see below.
- Sources deplete when used and slowly regenerate. No relocation yet.

#### Terrain: effort instead of distance

Each source sits behind something that costs effort to cross, so the organism faces **short-but-hard vs. long-but-easy** trade-offs:

| Terrain | Effect | Typical placement |
|---|---|---|
| **Hill** (height field) | Climbing drains extra energy and rest, scaled by slope; downhill is cheap. Movement slows on steep slopes. | Food on a hilltop, or water in a valley behind a ridge |
| **Marsh** | Slow movement; drains warmth (wet and cold). | Around the water source |
| **Thicket** | Blocks sight — objects inside are invisible until very close. | Hides the shelter |
| **Exposed ridge** | Fast to cross, but drains warmth quickly. | Between spawn and the heat source |

Every source should have at least two routes: a direct one through costly terrain and a longer detour around it.

The world sets the per-step cost (the slope comes from the height field; terrain type adds modifiers). The organism **doesn't know the costs in advance**. It feels them as its needs drain while it moves.

**What it can learn:**
- **Effort memory.** A coarse grid (e.g. 5×5-unit cells) remembering "crossing here cost me X", with confidence. Planning uses it as a travel-cost map.
- **Routes emerge.** Early on it goes straight over the hill. After a few costly trips, its remembered costs can make the detour score better — it "discovers" a path without any pathfinding being scripted.
- **Context matters.** When thirst is critical, the short hard route can still win. The same organism takes different routes depending on its state.

Pathing: simple A* over the coarse grid using *remembered* costs (unknown cells get a neutral guess). Cheap, inspectable, and the planned route can be drawn on the map.

**Visuals:** hills as thin contour lines over the graph paper (topographic-map style), marsh as fine stipple, thicket as light hatching, ridge as a dashed outline. Optional overlay: the organism's effort memory as a faint heat tint.

For MVP-0 the minimum is **the height field (hills) plus one other terrain type** (marsh). Thicket and ridge can come right after.

### The four needs

Pick four that each require a *different, spatially separated* thing, so the organism must balance travel:

| Need | Drains from | Satisfied by | Notes |
|---|---|---|---|
| **Energy** | time + movement | **Food** source | Via a stomach: eating fills stomach, stomach converts to energy over time (the doc's delay principle). |
| **Hydration** | time, faster when warm | **Water** source | Fast-draining — the most urgent driver. |
| **Warmth** | ambient cold | **Heat** source (warm vent / sun patch) | Must periodically go back; gives a reason to oscillate between places. |
| **Rest** | movement, wakefulness | Resting; faster at a **shelter** | Can rest anywhere, but in the open recovery is slow and other needs keep draining. |

Any need at 0 drains **health**. Health 0 = death. Oxygen is left out because it's uniform in a flat world and would do nothing.

### "No instructions" — what the organism actually doesn't know

To make this more than a pathfinding demo, the organism starts knowing **nothing about the world, including what things are for**:

- It has **drives** (it feels hungry, thirsty, cold, tired) and **senses** (sees objects within a radius, feels local temperature).
- It does **not** know which object satisfies which drive.
- On reaching an object, it can **try** it: eat, drink, rest, or stay near it. It then observes which internal values changed.
- It records an **association**: `object kind → effect on needs`, with a confidence score that strengthens with repeat success and weakens with failure.
- It records a **location memory**: `object at (x,y)`, with confidence that decays slowly over time.

So the first discovery isn't "found water" — it's "tried drinking from this thing, hydration went up". Rocks teach it nothing; the harmful plant teaches it to avoid that kind. That's real learning with tiny rules.

### Decision-making (simple, inspectable)

Every N ticks the mind scores a few candidate actions:

- **Go to remembered object X and use it for need N** — score ≈ urgency(N) × association confidence × location confidence ÷ travel cost.
- **Explore** — score ≈ curiosity × (how unvisited the nearby area is), higher when no good memory exists.
- **Try the nearest unknown object** — the experiment action.
- **Rest here** — score from fatigue.

Highest score wins. Write the top 2–3 scores to the inspector so the player can see *why*: `GOTO water#2 0.71 | EXPLORE 0.34 | REST 0.12`.

### What it should look like in practice

- Early life: wandering, poking at rocks, maybe dying of thirst. **That's fine and expected.**
- A good run: stumbles on water, learns it, comes back; finds food later; starts cycling between known places.
- **Tuning target:** starting reserves should give roughly a 30–60% chance of finding the first source by exploring. Too generous = boring, too stingy = it always dies. Use the headless runner to measure this.

### Event log

- Scrollable list with a **search box** (plain substring, case-insensitive) and **category toggles** (BIO / BEH / COG / ENV).
- Virtualised list so it handles thousands of lines.
- Structured events underneath: `{id, tick, category, organismId, x, y, text, data}`.
- Don't log every step. Log: decisions that *change* intent, discoveries, tries, learning, need thresholds crossed (e.g. "hydration critical"), death.
- Clicking an event centres the map on where it happened.

Example:

```text
T+000000  BIO  Organism #1 born at (100,100)
T+000142  BEH  Intent: EXPLORE (no useful memory)
T+000613  COG  Noticed unknown object #7 at (131,88)
T+000640  BEH  Tried EAT on object #7 (kind: stone) → no effect
T+000641  COG  Association: stone ⇸ energy (conf 0.20)
T+001905  BIO  Hydration critical (12%)
T+002210  COG  Noticed unknown object #2 at (168,121)
T+002240  BEH  Tried DRINK on object #2 → hydration +18
T+002241  COG  Association: pool → hydration (conf 0.35)
T+002241  COG  Remembered pool at (168,121)
```

### UI layout

```text
┌──────────────────────────────────────┬──────────────────────┐
│                                      │ ORGANISM #1          │
│        graph-paper map               │ age  T+002241        │
│   (zoom / pan, trail, objects,       │ ENERGY    ████░░ 64  │
│    sense radius ring)                │ HYDRATION ██░░░░ 31  │
│                                      │ WARMTH    █████░ 80  │
│                                      │ REST      ███░░░ 52  │
│                                      │ HEALTH    ██████ 100 │
│                                      │ intent: GOTO pool#2  │
│                                      │ why: 0.71 / 0.34 ... │
│                                      │ KNOWLEDGE (5)        │
├──────────────────────────────────────┴──────────────────────┤
│ [search______] [BIO][BEH][COG][ENV]   ▌▌ 1× 5× 20× 100×     │
│ T+002240  BEH  Tried DRINK on object #2 → hydration +18     │
│ T+002241  COG  Association: pool → hydration (conf 0.35)    │
└─────────────────────────────────────────────────────────────┘
```

Toggle: **"show what it believes"** — draws the organism's remembered objects as hollow markers over the real ones.

### Explicitly out of scope for MVP-0

Reproduction, genetics, teaching, multiple organisms, heatmaps, statistics charts, offline catch-up. (Simple JSON save/load on exit/start is a nice-to-have.)

### Acceptance criteria

1. One organism spawns knowing nothing; sources are moderately far, behind costly terrain, and unmarked to it.
2. It explores, tries objects, and logs what happened.
3. It can learn that a kind of object satisfies a need, and remember where it was.
3b. It remembers how costly terrain was and, over repeated trips, can switch to a cheaper route.
4. It can die from any of the four needs, and the log makes the cause obvious.
5. On a good seed, it settles into revisiting known sources.
6. The inspector always shows current intent and why.
7. The log is scrollable and searchable; clicking an entry locates it on the map.
8. A headless runner reports, over N seeds: survival time, time to first discovery, cause of death.

## Roadmap after MVP-0

- **MVP-1 — Culture.** Splitting reproduction, top-K teaching, three genes, reseed on extinction, culture ON/OFF A/B in the runner. (The review's original MVP.)
- **MVP-2 — Change.** Sources relocate and dry up, so inherited knowledge can go stale. Believed-vs-actual becomes dramatic.
- **MVP-3 — Persistence.** JSON snapshots, offline catch-up with a cap.
- **MVP-4 — Observation tools.** Movement heatmap, per-organism history, simple population charts.

---

# Appendix — Original design doc (Homunculus v0.1, unchanged)

# HOMUNCULUS
## Artificial Life & Cultural Evolution Simulator
### Game Design & Technical Direction — v0.1

---

# 1. Vision

Homunculus is an observational artificial-life simulation.

The player introduces an organism into a simulated environment and watches it discover how to survive, develop cognitive abilities, accumulate knowledge, reproduce and eventually establish a population.

The player does not directly control the organisms.

Instead, the player observes their behaviour, explores their biological and cognitive development, and studies how individual discoveries become collective knowledge passed between generations.

The central premise:

**Start with one organism that knows nothing. Give it a world to survive in. See what happens.**

The simulation should be capable of running indefinitely, independently of player interaction. The player should be able to leave and return later to discover what happened.

## Core design principles

1. Emergence over scripting: behaviour should result from interacting systems rather than predefined sequences.
2. Understandable simulation rules: biological and cognitive processes should be inspectable and explainable.
3. Observation over management: the player observes rather than micromanages.
4. Individuality: organisms should have their own histories, knowledge, experiences and traits.
5. Cultural evolution: acquired knowledge should be transferable between generations.
6. Persistent world: the environment and organisms continue evolving while the player is away.
7. Visual storytelling: movement, discoveries, population changes and knowledge should be visible on the map and through historical logs.

---

# 2. The World

The simulation takes place in a two-dimensional biome.

The environment should initially be relatively simple and visually readable.

### Environmental elements

- Walkable terrain.
- Food sources.
- Water sources.
- Oxygen availability.
- Temperature variations.
- Potential hazards.
- Environmental boundaries.

Environmental resources should have their own properties and behaviours.

For example:
- Food can be consumed and eventually depleted.
- Some food sources may regenerate.
- Water can be distributed across the environment.
- Temperature can vary spatially.
- Hazards can cause damage or death.

The environment should be independent of individual organisms. It is a world with its own state and rules.

### Map representation

Use a simple 2D coordinate system.

The map should support:
- Zooming and panning.
- Resource visualization.
- Organism positions.
- Movement trajectories.
- Heatmap overlays.
- Selection of individual organisms.

The player should be able to see the entire physical world, including resources that organisms have not yet discovered.

This creates an important distinction between what exists and what an organism knows exists.

---

# 3. Organism Biology

Each organism is an autonomous biological entity.

It has internal requirements, physical capabilities and biological processes.

Initially, organisms should be relatively simple.

## 3.1 Essential biological variables

Each organism should track:

| Variable | Description |
|---|---|
| Energy | Available metabolic energy |
| Hydration | Internal water availability |
| Oxygenation | Oxygen availability |
| Health | Overall physical condition |
| Body temperature | Internal temperature |
| Age | Time since birth |
| Nutrients | Available nutritional resources |

These variables should change continuously through simulation processes.

## 3.2 Biological systems

Implement biological processes as interacting systems.

| System | Responsibility |
|---|---|
| Metabolism | Consumes energy to sustain life |
| Digestion | Converts consumed food into nutrients |
| Respiration | Exchanges oxygen and CO2 |
| Circulation | Distributes oxygen and nutrients |
| Muscular | Enables movement and consumes energy |
| Nervous | Processes sensory information |
| Reproduction | Produces offspring |
| Immune/healing | Responds to damage and disease |

Not every system needs complex biological modelling.

The initial implementation should use simple numerical models with understandable inputs and outputs.

### Important principle

Needs and resources should not be interchangeable.

For example, eating food should not directly replenish energy. Food must be digested, nutrients processed and energy made available through metabolism.

This creates delays, dependencies and possible biological failures.

## 3.3 Biological consequences

Systems should influence each other.

Examples:
- Low oxygen reduces energy production.
- Low energy reduces movement.
- Nutrient deficiency limits healing.
- Dehydration affects circulation.
- Increased movement raises energy consumption.
- Poor health can impair cognitive and physical capabilities.

Organisms should be able to die naturally through biological failure.

There should be no artificial guarantee that organisms survive long enough to reproduce.

---

# 4. Behaviour and Environmental Interaction

Organisms should have basic autonomous behaviour.

They are not directly controlled by the player.

Their actions should emerge from biological requirements and available sensory information.

### Initial behaviours

- Move.
- Explore.
- Detect food.
- Consume food.
- Detect water.
- Drink.
- Respond to environmental conditions.
- Avoid harmful situations when possible.
- Rest or reduce activity when necessary.

The first implementation can use relatively simple decision-making.

For example, an organism with low hydration can prioritize searching for water.

However, the organism should not automatically know where water is located.

It must detect, discover or learn its location.

### Behavioural priorities

An organism should continuously evaluate its requirements and choose actions according to its current state.

Avoid implementing a rigid sequence of scripted behaviours.

The same organism should behave differently depending on its biological condition, knowledge and environment.

---

# 5. Cognition and Learning

**One of the central concepts of Homunculus is the development of intelligence through experience.**

Organisms should initially possess only basic biological drives and simple sensory capabilities.

Over time, they accumulate knowledge through exploration, experimentation and interaction with the environment.

Intelligence should emerge from several cognitive capabilities rather than a single experience bar.

## 5.1 Cognitive capabilities

| Capability | Function |
|---|---|
| Perception | Detect environmental stimuli |
| Memory | Remember locations and experiences |
| Association | Connect events with consequences |
| Learning | Adjust behaviour based on experience |
| Planning | Choose actions based on anticipated outcomes |
| Communication | Transfer information to others |
| Teaching | Deliberately help others acquire knowledge |

These capabilities can initially be represented by numerical attributes.

They should have practical effects on behaviour.

Examples:

- Better memory allows organisms to remember previously discovered resources.
- Better association allows them to connect environmental patterns with outcomes.
- Better learning improves adaptation to unfamiliar situations.
- Better planning allows them to anticipate requirements and plan routes.
- Communication allows them to share information.
- Teaching allows them to deliberately transfer acquired knowledge.

## 5.2 Learning through experience

Organisms should learn from their own experiences.

Examples:
- Finding food and remembering its location.
- Discovering that certain vegetation is edible.
- Associating certain environmental conditions with danger.
- Remembering water sources.
- Learning that certain routes are more efficient.

Learning should be recorded as actual knowledge, not simply reflected in a generic intelligence score.

## 5.3 Knowledge representation

Each organism maintains its own knowledge database.

A knowledge entry should support:

- Knowledge category.
- Subject or discovered object.
- Associated location, where applicable.
- Confidence.
- Number of successful experiences.
- Number of failed experiences.
- Last confirmation time.
- Retention or forgetting behaviour.
- Transferability to other organisms.

Knowledge should be capable of being incomplete or incorrect.

An organism can remember something incorrectly, develop an inaccurate association or retain outdated information.

Knowledge should also be capable of being forgotten.

---

# 6. Reproduction and Cultural Evolution

**This is another defining feature of the project: biological inheritance and acquired knowledge must be separate systems.**

The simulation starts with a single organism.

Reproduction should become possible when the organism reaches sufficient biological and cognitive maturity.

A configurable intelligence threshold should initially govern the ability to reproduce and teach.

The threshold should not be a magical transformation. It unlocks more sophisticated reproductive and social capabilities.

## 6.1 Reproduction

When reproduction becomes possible, organisms should:
- Meet biological reproduction requirements.
- Have sufficient resources and energy.
- Produce offspring.
- Pass on genetic traits.
- Transfer selected knowledge, depending on their cognitive capabilities.

Reproduction should have meaningful biological costs.

Population growth should emerge from successful survival and reproduction rather than an automatic spawning mechanism.

## 6.2 Genetic inheritance

Offspring inherit physical and biological traits from their parents.

Traits should support variation through mutation.

Potential inheritable traits:

| Trait | Potential consequence |
|---|---|
| Metabolic rate | Energy consumption |
| Body size | Strength and resource requirements |
| Mobility | Exploration efficiency |
| Digestive efficiency | Nutrient processing |
| Temperature tolerance | Habitat suitability |
| Reproductive capacity | Reproductive potential |
| Sensory range | Environmental detection |
| Cognitive capacity | Memory and learning capabilities |

Mutations should generally be small, with occasional more significant variations.

Biological inheritance and acquired knowledge must remain separate.

An organism should not genetically inherit something merely because its parent learned it.

## 6.3 Cultural inheritance

Offspring can receive acquired knowledge from their parents.

This is essential.

A newly born organism should not necessarily start from zero knowledge.

Its parent can transfer useful survival information:
- Known food sources.
- Water locations.
- Dangerous areas.
- Useful behavioural patterns.
- Environmental associations.

This should dramatically accelerate the survival and development of later generations.

## 6.4 Teaching

Teaching should be an actual activity with a cost.

It requires:
- An organism capable of teaching.
- An organism capable of learning.
- Time and energy.
- A transferable piece of knowledge.

Knowledge transfer should not be unlimited.

Parents should have limited time and teaching capacity, creating a prioritization problem.

For example, a parent with 30 knowledge entries may only have time to teach five useful things.

The offspring must still learn some things independently.

Knowledge transmission should not guarantee correctness. An organism can pass on outdated or inaccurate information.

## 6.5 Cultural evolution

Over multiple generations, populations can develop collective knowledge.

Potential emergent outcomes:
- More efficient exploration.
- Shared routes to resources.
- Avoidance of previously dangerous locations.
- Different behaviours in isolated populations.
- Loss of knowledge after environmental changes.
- Competition between populations with different knowledge.

Do not script these outcomes.

Provide the mechanisms and let them emerge.

---

# 7. Movement Visualization and Spatial Knowledge

**The map and movement history should be a major part of the player experience, not merely a debugging tool.**

The player should be able to observe how organisms explore and gradually become familiar with their environment.

## 7.1 Individual movement trajectories

Record organism positions over time.

The player should be able to:
- Select an organism.
- Display its movement trajectory.
- See the direction of movement.
- Toggle historical movement trails.
- Filter by individual or generation.
- Inspect its exploration history.

Older movement trails should optionally fade.

Different organisms can have different trajectory colours.

Movement records should be timestamped.

Do not permanently record every rendered frame. Use periodic spatial samples and additional samples when meaningful movement events occur.

## 7.2 Heatmaps

Provide several independently selectable heatmap layers.

| Layer | Information |
|---|---|
| Movement density | Areas where organisms spend time |
| Food discovery | Locations where food was discovered |
| Feeding activity | Areas where organisms consume food |
| Danger | Locations associated with harmful encounters |
| Reproduction | Locations of births |
| Knowledge | Areas known to organisms |

Heatmaps should be filterable by:
- Individual organism.
- Generation.
- Entire population.
- Selected historical period.

## 7.3 Four levels of spatial understanding

Keep these concepts separate:

1. **Visited:** An organism physically passed through an area.
2. **Explored:** An organism gathered environmental information about the area.
3. **Known:** An organism remembers useful information about the area.
4. **Taught:** An organism acquired information about the area from another organism.

This distinction enables interesting emergent behaviour.

For example, an organism might know where food is located without ever having physically visited that location, because its parent taught it.

## 7.4 Collective knowledge map

The player should eventually be able to inspect the knowledge of the entire population.

Visualize:
- Areas unexplored by the population.
- Areas known by only one organism.
- Areas known by multiple organisms.
- Locations whose knowledge has spread between generations.
- Previously discovered resources that have been forgotten.

The physical world should remain visible independently of organism knowledge.

---

# 8. Logging and Historical Observation

**Logging is a primary gameplay feature.**

The player should be able to understand what happened, why it happened and how the population developed.

Implement structured event logging.

Do not rely exclusively on a textual console.

## 8.1 Event categories

Examples:

**Biological**
- Birth.
- Death.
- Feeding.
- Drinking.
- Injury.
- Recovery.
- Reproduction.

**Behavioural**
- Movement.
- Exploration.
- Resource search.
- Resting.
- Avoidance.

**Cognitive**
- Discovery.
- Learning.
- Memory formation.
- Association.
- Planning.

**Social**
- Interaction.
- Communication.
- Teaching.
- Knowledge transfer.

**Evolutionary**
- Mutation.
- Trait inheritance.
- Generation change.
- Population divergence.

## 8.2 Event structure

Each event should contain:

- Unique event ID.
- Simulation timestamp.
- Event category.
- Organism ID, where applicable.
- Related organism IDs.
- Location, where applicable.
- Event description.
- Relevant contextual data.

Example:

```text
[GEN 001] Organism 001 born
[00:02:14] Detected potential food source
[00:03:08] Changed movement direction
[00:04:51] Consumed organic matter
[00:05:12] Learned food association
[00:12:30] Discovered water source
[00:35:10] Cognitive threshold reached
[00:38:22] First offspring produced
[00:38:25] Teaching initiated
[00:39:02] Offspring learned food location
```

These timestamps represent simulation time, not necessarily real-world time.

## 8.3 Filtering and inspection

The player should be able to filter events by:
- Organism.
- Generation.
- Category.
- Simulation time.

Selecting an event should ideally allow the player to locate the corresponding organism and position on the map.

## 8.4 Organism history

Every organism should have an individual history.

The player should be able to inspect:
- Birth and death.
- Parentage.
- Biological development.
- Cognitive development.
- Acquired knowledge.
- Knowledge transferred.
- Major discoveries.
- Reproductive history.

This allows individual life stories to emerge naturally.

---

# 9. Population Statistics

Provide a population overview that makes long-term development visible.

Initial metrics:

- Current population.
- Births and deaths.
- Generation count.
- Average age.
- Average health.
- Average intelligence.
- Average knowledge.
- Genetic diversity.
- Knowledge inheritance rate.
- Survival rate.

Statistics should be tracked historically, not only displayed as current values.

Eventually, provide charts showing how these metrics change over generations.

---

# 10. Persistent Simulation and Real Time

**The world should continue evolving even when the player is not interacting with it.**

The initial implementation should use a browser-based simulation with offline catch-up.

There is no initial requirement for a continuously running cloud server.

## 10.1 Simulation clock

Separate the simulation engine from the visual rendering system.

Recommended initial configuration:

- Fixed simulation timestep.
- Configurable simulation speed.
- Independent rendering loop.
- Structured event logging.
- Periodic automatic saving.

Rendering frequency must not determine biological update frequency.

Use a seeded random number generator so that simulation behaviour can be reproduced when necessary.

## 10.2 Persistence

Use IndexedDB for persistent simulation storage.

Save:
- World state.
- Organism states.
- Biological attributes.
- Genetic information.
- Knowledge databases.
- Resource states.
- Simulation timestamp.
- Random generator state.
- Event sequence number.
- Historical records.

Use periodic snapshots rather than saving the entire world after every update.

Store subsequent events separately.

When restoring:
1. Load the latest snapshot.
2. Restore its random generator state.
3. Load events after the snapshot's sequence number.
4. Advance the simulation through the elapsed period.
5. Resume normal simulation.

Snapshots and events must be coordinated to avoid duplicate processing.

## 10.3 Offline catch-up

When the player returns:
- Calculate elapsed real-world time.
- Convert it into simulated time.
- Advance the simulation using its actual biological and behavioural rules.
- Process births, deaths, movement, consumption, learning and other events.
- Save the resulting state.

Do not simply increment organism ages or assume survival.

Offline simulation must produce meaningful outcomes.

Implement safeguards against extremely long catch-up periods and excessive event generation.

The browser may throttle or suspend background execution, so persistence and catch-up must not depend on background timers continuing to run.

## 10.4 Simulation speed controls

Provide:
- Pause.
- 1× speed.
- 5× speed.
- 20× speed.
- 100× speed.

Simulation speed should change the rate of simulated time, not the underlying biological rules.

---

# 11. Technical Architecture

Keep the simulation engine independent of the user interface.

The same engine should eventually be reusable in a browser, desktop application or cloud-hosted environment.

Suggested modules:

```text
Homunculus/
│
├── simulation/
│   ├── SimulationEngine
│   ├── SimulationClock
│   ├── RandomGenerator
│   └── WorldState
│
├── environment/
│   ├── Biome
│   ├── Resources
│   ├── Temperature
│   └── Hazards
│
├── organism/
│   ├── Organism
│   ├── Biology
│   ├── Metabolism
│   ├── Movement
│   ├── Behaviour
│   └── Reproduction
│
├── cognition/
│   ├── Perception
│   ├── Memory
│   ├── Learning
│   ├── Knowledge
│   ├── Communication
│   └── Teaching
│
├── evolution/
│   ├── Genetics
│   ├── Mutation
│   └── Inheritance
│
├── logging/
│   ├── EventLogger
│   ├── EventHistory
│   └── Statistics
│
├── persistence/
│   ├── Database
│   ├── Snapshots
│   └── OfflineCatchUp
│
└── presentation/
    ├── WorldMap
    ├── MovementTrails
    ├── Heatmaps
    ├── OrganismInspector
    ├── EventTimeline
    └── PopulationDashboard
```

This is a suggested logical organization, not a requirement to create dozens of classes immediately.

Prefer simple, cohesive modules and avoid unnecessary abstraction.

---

# 12. Initial Playable Prototype

The first milestone should be a complete, observable survival and reproduction loop.

Do not attempt to implement all the long-term concepts immediately.

## MVP requirements

### Environment
- A simple 2D biome.
- Food and water sources.
- Oxygen and temperature.
- Resource consumption and regeneration.

### Organism
- One initial organism.
- Energy, hydration, oxygenation and health.
- Movement.
- Basic sensory detection.
- Eating and drinking.
- Biological deterioration and death.

### Cognition
- Basic memory.
- Resource discovery.
- Learning through experience.
- Knowledge entries with confidence and location.

### Reproduction
- Intelligence threshold.
- Biological reproduction requirements.
- Offspring generation.
- Basic genetic inheritance.
- Cultural inheritance of selected knowledge.

### Visualization
- World map.
- Organism positions.
- Individual movement trails.
- Basic movement heatmap.
- Organism inspector.
- Event log.

### Persistence
- Automatic saving.
- Restore after closing the application.
- Offline catch-up.
- Simulation speed controls.

## MVP acceptance criteria

The prototype should demonstrate the following complete sequence:

1. One organism is introduced into the world.
2. It independently explores its surroundings.
3. It discovers and consumes food and water.
4. It learns and remembers useful resource locations.
5. It can die if its biological needs are not met.
6. Under suitable conditions, it develops sufficient cognitive capability.
7. It becomes capable of reproducing.
8. It produces offspring.
9. Offspring inherit selected knowledge from the parent.
10. Offspring can use that knowledge to survive more efficiently.
11. The player can inspect the movement, history and knowledge of both generations.
12. The player can close the application, return later and observe the simulation having advanced.

The prototype is successful when this loop works without scripted player intervention.

---

# 13. Future Possibilities (Not MVP Requirements)

These ideas are deliberately reserved for later development.

### Advanced intelligence
- More sophisticated planning.
- Behavioural imitation.
- Communication systems.
- Social cooperation.
- Specialization.

### Cultural development
- Traditions.
- Knowledge loss.
- Cultural divergence.
- Population-specific knowledge.
- Incorrect or outdated inherited information.

### Advanced biology
- More detailed organ systems.
- Immune systems.
- Diseases.
- Multicellular organisms.
- Organ specialization.

### Environmental evolution
- Changing climate.
- Seasonal conditions.
- More complex ecosystems.
- Predators and competitors.
- Multiple biomes.

### Evolutionary developments
- Species divergence.
- Adaptation to different environments.
- Complex social behaviour.
- Competition between populations.

These should emerge from the underlying systems rather than predefined milestones or scripted civilizations.

---

# 14. Important Implementation Constraints

1. Do not use LLMs to make individual organisms think. Implement cognition through simulation rules, memory, associations and decision-making.
2. Do not hardcode a sequence of discoveries or guarantee reproduction.
3. Do not conflate intelligence, knowledge and genetic traits.
4. Do not make offspring automatically inherit all parental knowledge.
5. Do not make movement and knowledge maps the same thing.
6. Do not tie simulation logic to rendering frames.
7. Do not depend on a browser tab remaining active to preserve the simulation.
8. Do not generate an unbounded log of every movement update.
9. Keep the engine deterministic where possible, using controlled random seeds.
10. Prioritize inspectability: the player should be able to understand why an organism performed an action.

---

# 15. Overall Design Goal

Homunculus should feel like a scientific observation tool crossed with an artificial-life sandbox.

The player is not building a civilization or completing objectives.

The player introduces life, observes its struggles, discovers its behavioural patterns, and watches knowledge accumulate across generations.

The most compelling moments should be unexpected:

- An organism discovering a food source after prolonged exploration.
- An organism learning to avoid a dangerous area.
- The first successful reproduction.
- An offspring immediately finding food because its parent taught it where to look.
- A population developing a completely different behavioural pattern from its ancestors.
- A previously successful population losing knowledge and struggling to survive.

**The ultimate objective is not to create intelligent creatures. It is to create a system in which intelligence, knowledge and culture can emerge as useful adaptations to the challenge of staying alive.**