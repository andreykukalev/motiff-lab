---
name: council
description: Convene the Motiff Lab architecture council on one fork (full or light) — brief, facts, three rounds of read-only seats, fact checks against the code, a decision record and questions for Andrei. Only when Andrei asks for a council or the coordinator passes on his request; not for code review and not for a single question.
---

# Architecture council

Roles, rules and prohibitions are in `governance/roles/council.md`; read it and `governance/roles/common.md` before the
first step. You are the moderator: you run the rounds, check the facts, write the records, and do not vote.

## When

- Andrei asks for a council ("convene a council on …", `/council <topic>`), or the coordinator passes on such a request
  with his message attached. Without a question from Andrei there is no council.
- A full council is for a decision that is expensive to undo (architecture, a contract, a format, monetisation). A
  light council is for everything else: the researcher, two seats chosen for the question, the blind seat, R1 and R2.

## Starting the seats

Each seat is a subagent defined in `.claude/agents/`:

| Seat | `subagent_type` | Model |
| --- | --- | --- |
| Researcher | `researcher` | sonnet |
| Core and data | `council-seat-core` | opus |
| Web | `council-seat-web` | sonnet |
| iPad | `council-seat-ipad` | opus |
| Cost and delivery | `council-seat-cost` | sonnet |
| Advocate of option N | `council-advocate` | sonnet; haiku if there are more than three options (pass `model: haiku` when starting it) |
| Blind seat | `council-blind` | opus |

**If that `subagent_type` does not exist** (claude.ai cloud threads did not read the repository's `.claude/agents` on
2026-11-08): start `Explore` with the `model` from the table, and as the prompt pass first the line

> Use only Read, Grep and Glob. No Bash, no MCP tools (GitHub or any other), no writing anywhere.

then the whole text of the definition (everything after the second `---` line), then the round assignment. For the
researcher, use this line instead: "Bash for reading only: git log, git show, git diff, git grep, wc, ls, grep, find; no
MCP tools and no writing".

**Honest limits.** `Explore` has `Bash` and every MCP tool, GitHub writes included, and the deny rules in
`.claude/settings.json` do not apply in a cloud thread. So in the cloud, "read only" is held by a line in the prompt and
by the seat's definition, not by a technical limit. The tool list in the frontmatter only applies where agents load by
name (Claude Code on a computer). If a seat's report shows it ran anything other than reads, the moderator stops the
council and tells Andrei.

The round assignment in each seat's prompt: the council folder path, which files to read, the name of the round
instructions file, and for an advocate the number and title of its option. All seats of a round start in parallel, in
one message.

## Where the council runs

| | claude.ai cloud thread | Claude Code on Andrei's computer |
| --- | --- | --- |
| Seats | The `Explore` fallback above | By name, with the tool list from the definition |
| Council folder | `/mnt/project-files/councils/<topic>/` (a network mount: put a trailing slash on the path, re-read a file about 10 seconds after writing it) | A folder outside the repository next to the clone: `../motiff-lab-councils/<topic>/`; Andrei can move it to the project files afterwards. Claude Code may ask before reading outside its working folder; if so, add it with `/add-dir` [unverified] |
| Decision record | A Claude Doc | `record.md` in the council folder |
| Questions to Andrei | Decision cards (`request_decision`) | A question with options (`AskUserQuestion`), recommendation first |
| Runtime checks | No .NET SDK: "check before building" | The SDK is there: a build can check it |

The steps below use the cloud thread's names; on a computer, use the right-hand column.

## Council folder

| File | Written by | What |
| --- | --- | --- |
| `README.md` | moderator | The council's timeline, the link to the decision record, Andrei's answers |
| `brief.md` | moderator with Andrei | The frozen brief (template `templates/brief.md`) |
| `concept-*.md` | Andrei | His product concept, if he gave one, in full |
| `lenses.md` | moderator | The seats' lenses, if for this council they differ from the definitions |
| `facts-baseline.md` | researcher | Facts `F<n>` on the named SHA |
| `r<N>-instructions.md` | moderator | The round instructions (templates `templates/r1.md`, `r2.md`, `r3.md`) |
| `r<N>/<seat>.md` | moderator | The seat's report, unedited |
| `facts.md` | moderator | Verified facts `V<n>` (template `templates/facts.md`) |
| `usage.md` | moderator | Usage: seat, model, tokens, time and tool calls for every run |

## Steps

1. **Brief.** Fill in `templates/brief.md` from Andrei's question. Neutral: nothing about what he leans to in the
   architecture. His product concept goes in full, as a separate file. Send the brief to Andrei and ask on one card:
   confirm, correct, full or light council.
2. **Facts, in parallel with the brief's confirmation.** Start the researcher with the brief's questions and the
   current `main` SHA. Save `facts-baseline.md`, re-check three to five numbers yourself.
3. **Freeze.** Once Andrei says yes, the brief does not change (`governance/roles/council.md`, "Moderator").
4. **R1, independent.** All seats in parallel. The blind seat reads only `brief.md` and `facts-baseline.md`.
5. **Fact check.** Every claim the positions disagree on or depend on is checked against the code, with `file:line` on
   the SHA. Write `facts.md`. List the convergence and the live disagreements (a), (b), … in `r2-instructions.md`.
6. **R2, cross-attack.** Every seat sees all R1 reports and `facts.md`.
7. **Fact check** of every new claim an attack rests on. Append to `facts.md`.
8. **R3, final** (full council only). The settled points S1… and the open cruxes C1… with numbers go in
   `r3-instructions.md`.
9. **The checks the seats asked for**, when they are cheap and safe; whatever needs a build or a runtime (the
   container has no .NET SDK) goes in a "check before building" list.
10. **Decision record** — a Claude Doc in Russian (Andrei reads it in the app), following `templates/record.md`.
11. **The gate.** Questions to Andrei on decision cards (`request_decision`), one per card, recommendation first. Card
    limits are in bytes and Cyrillic takes 2 bytes per letter: option label ≤ 40 bytes (about 20 letters),
    consequence and reason ≤ 200 bytes (about 100 letters). Record the answers in `README.md` in the same turn; after
    the last answer, say the gate is closed.

## Common failures

- **The brief changes after R1** (in the pilot it changed four times before R1). A changed question means a new
  council.
- **A fact one seat stated confidently becomes a premise.** Before the next round, only through `facts.md`.
- **A vague attack.** "This might be complex" is not an attack: send it back to the seat for a scenario.
- **A technical question in the gate.** If the seats agree and a check confirms it, it is not a question for Andrei
  but an item "settled by a check" in the record.
- **A decision record that lives only in chat.** Everything goes in the council folder and the Claude Doc.

## Done when

- [ ] Andrei confirmed the brief and it did not change after R1
- [ ] Every seat ran read-only, and their reports are saved unedited
- [ ] `facts.md` closes every disputed claim: verified (how) or unverified (why it matters)
- [ ] `usage.md` is filled in
- [ ] The decision record is published, the gate asked, Andrei's answers in `README.md`
