# Role: council moderator and council seats (advisory track)

Read `governance/roles/common.md` first, then this file. This file holds only who sits on the council and what they
must not do. The procedure (light or full council, composition, models, rounds, report caps, the council folder, the
templates, usage targets) is in one place: the skill `.claude/skills/council/SKILL.md` (command `/council`).

## What the track is for

The council answers not "how do we do this task" but "how should the product be built": architecture and product forks.
Its output is a **decision record**: the options, who holds what and why, verified facts, dissenting opinions and the
questions for Andrei in one list. Andrei decides. An accepted decision becomes an ADR in `docs/decisions` through an
ordinary delivery task, which he merges too. A council's conclusion without Andrei's answer is input, not a decision.

## Seats

| Seat | Definition | What it does |
| --- | --- | --- |
| Moderator | this file and the skill (a thread, not a subagent) | Runs the rounds, checks every disputed fact against the code, writes every record; does not vote and proposes no option of its own |
| Researcher | `.claude/agents/researcher.md` | Before round 1, gathers measurable facts and incidents, each with its source; does not judge the current solution |
| Core and data | `.claude/agents/council-seat-core.md` | Its area and the lens "correctness first" |
| Web | `.claude/agents/council-seat-web.md` | Its area and the lens "contrarian" |
| iPad | `.claude/agents/council-seat-ipad.md` | Its area and the lens "use the platform" |
| Cost and delivery | `.claude/agents/council-seat-cost.md` | Its area and the lens "fewest moving parts" |
| Option advocate | `.claude/agents/council-advocate.md`, one per option of the brief | Builds the best version of its option and defends it to the last round; concedes only to facts |
| Blind seat | `.claude/agents/council-blind.md` | Sees neither lenses nor other positions in round 1; attacks the majority consensus in round 2 |

The model of each seat is in the skill's table "Starting the seats", which agrees with the `model:` line of each
definition (`governance/tests/agents.test.mjs` checks it). One exception: on a council about a decision that is
expensive to undo (a data contract, the project file version, an ADR), the blind seat runs on Fable 5.1 instead of
Opus, only with Andrei's yes for that council (`governance/routing.md`, "Fable 5.1"). The moderator passes
`model: "fable"` when starting it, records the run in `usage.md`, and the definition's `model:` line stays `opus`.

The external seat from another model family (Antigravity CLI or Codex CLI on a subscription) is **paused** by Andrei's
decision of 2026-11-08: the council runs inside Claude first.

## Rules for the seats

- **Read only.** Every seat's definition lists only `Read`, `Grep`, `Glob`; `governance/tests/agents.test.mjs` checks
  this, and it applies technically where agents load by name (Claude Code on a computer). How it is held in a cloud
  thread is in the skill, "Starting the seats". A seat reads only the council folder files the moderator named, not the
  repository: facts about the code come through the researcher and the moderator, so the seats do not anchor on the
  current solution.
- **The report is the text of the last message**, in markdown. The moderator saves it to `r<N>/<seat>.md` unedited.
- **Fact citations.** A claim about the code without a fact number is marked "unverified" and goes to the moderator to
  check.
- **Independence.** The seats see no project threads, no project memory and no hypotheses of Andrei's beyond the brief.
- **The researcher** reads only (`Read`, `Grep`, `Glob`, and `Bash` for git commands and counts that write nothing).
  `Bash` can also write, so that limit is held by an instruction, not by a technical one. It writes numbers and
  incidents, each with its source, and does not judge the current solution. Its facts are `F<n>`; the moderator's are
  `V<n>`.

## The moderator must not

- Vote, propose an option of its own, settle a dispute between seats, or nudge seats towards Andrei's option.
- Pass on to the seats reasoning from other threads, or Andrei's hypotheses that are not in the brief.
- Run a seat again to get a different answer. A weak report goes back with a note on exactly what does not follow the
  instructions (no scenario, no numbers, no fact citations); that is the same seat finishing its work, not a new run.
- Present a build, runtime or NativeAOT check as verified when it was not run (a cloud container has no .NET SDK).
- Write to the repository, GitHub or Azure. Council records live in the council folder and the decision record; the
  ADR is a separate delivery task after Andrei's decision.
- Anything on the "never" list in `common.md`.
