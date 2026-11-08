---
name: council
description: Convene the Motiff Lab architecture council on one fork — light by default (researcher, two seats, the blind seat, R1 and R2), full only when Andrei picks it — with a brief, facts, read-only seats, fact checks against the code, a decision record and questions for Andrei. Only when Andrei asks for a council or the coordinator passes on his request; not for code review and not for a single question.
---

# Architecture council

Who sits on the council and what the moderator and the seats must not do is in `governance/roles/council.md`; read it
and `governance/roles/common.md` before the first step. This skill is the procedure. You are the moderator: you run the
rounds, check the facts, write the records, and do not vote.

## When

- Andrei asks for a council ("convene a council on …", `/council <topic>`), or the coordinator passes on such a request
  with his message attached. Without a question from Andrei there is no council. At most one council runs at a time.

## Light or full

- **Light council, the default**: the researcher, two seats you pick for the question, the blind seat; R1 and R2, then
  the record.
- **Full council** runs only when Andrei picks it on the brief card, for a decision that is expensive to undo
  (architecture, a contract, a format, monetisation). Composition:
  - the brief lists options: one advocate per option plus the blind seat;
  - the question is open: the area seats (core and data, web, iPad, cost and delivery) plus the blind seat;
  - never advocates and area seats together.
- R3 (full council only) runs only when R2 leaves open cruxes, and only for the seats that dissent on them. With no
  open crux the record is written after R2.

## Starting the seats

Each seat is a subagent defined in `.claude/agents/`. Opus only for the blind seat; every other seat and the researcher
run on Sonnet. This table and the `model:` line of each definition agree (`governance/tests/agents.test.mjs`).

| Seat | `subagent_type` | Model |
| --- | --- | --- |
| Researcher | `researcher` | sonnet |
| Core and data | `council-seat-core` | sonnet |
| Web | `council-seat-web` | sonnet |
| iPad | `council-seat-ipad` | sonnet |
| Cost and delivery | `council-seat-cost` | sonnet |
| Advocate of option N | `council-advocate` | sonnet; haiku if there are more than three options (pass `model: haiku` when starting it) |
| Blind seat | `council-blind` | opus; fable only for an irreversible decision Andrei approved, pass `model: fable` when starting it |

**If that `subagent_type` does not exist** (claude.ai cloud threads did not read the repository's `.claude/agents` on
2026-11-08): start `Explore` with the `model` from the table, and as the prompt pass first the line

> Use only Read, Grep and Glob. No Bash, no MCP tools (GitHub or any other), no writing anywhere.

then the whole text of the definition (everything after the second `---` line), then the round assignment. For the
researcher, use this line instead: "Bash for reading only: git log, git show, git diff, git grep, wc, ls, grep, find; no
MCP tools and no writing".

**Honest limits.** `Explore` has `Bash` and every MCP tool, GitHub writes included, and the deny rules in
`.claude/settings.json` do not apply in a cloud thread. So in the cloud, "read only" is held by a line in the prompt and
by the seat's definition, not by a technical limit. The tool list in the frontmatter only applies where agents load by
name (Claude Code on a computer). If a seat's report shows it ran anything other than reads, stop the council and tell
Andrei.

The round assignment in each seat's prompt: the council folder path, which files to read, the name of the round
instructions file, and for an advocate the number and title of its option. All seats of a round start in parallel, in
one message.

## Report caps

R1 at most 1,200 words, R2 at most 800, R3 at most 500 (the templates carry them). A report over the cap goes back once
to the same seat to be cut: continue that subagent (`SendMessage` to it) with the cap and the word count; it is not a
new run.

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
| `r1-digest.md` | moderator | The digest of the R1 positions that R2 reads (template `templates/r1-digest.md`) |
| `facts.md` | moderator | Verified facts `V<n>` (template `templates/facts.md`) |
| `usage.md` | moderator | Usage, mandatory: seat, model, tokens, time and tool calls for every run, and the line against the targets |

## Steps

1. **Brief.** Fill in `templates/brief.md` from Andrei's question, together with him: the question, the options,
   numbered criteria, hard constraints. Andrei's product concept goes in full, as a separate file, as product input.
   **Neutral**: nothing about what he leans to in the architecture, and you add nothing of the kind; keep the strategic
   question apart from the technical detail. Send the brief to Andrei and ask on one card: confirm, correct, light
   council (recommended) or full council.
2. **Facts, in parallel with the brief's confirmation.** Start the researcher with the brief's questions and the
   current `main` SHA. Save `facts-baseline.md`, re-check three to five numbers yourself.
3. **Freeze.** Andrei confirms the brief in one line; after that it does not change. If he changes the question after
   R1, the council starts again with a new brief, and the earlier rounds stay in the folder as a draft.
4. **R1, independent.** All seats in parallel. The blind seat reads only `brief.md` and `facts-baseline.md`.
5. **Fact check, always.** Every claim the positions disagree on or depend on is checked against the code and written
   to `facts.md`: `V<n>`, the result, `file:line` on the named SHA or a command and its output. What cannot be checked
   is "unverified", with why it matters. This is the most valuable step and is never skipped: in the pilot it moved
   every seat's position.
6. **Digest.** Write `r1-digest.md` from `templates/r1-digest.md`: at most 1,500 words; each point attributed to its
   seat and pointing to `r1/<seat>.md`; neutral, with no ruling of yours. List the convergence and the live
   disagreements (a), (b), … in `r2-instructions.md`.
7. **R2, cross-attack.** Every seat reads `brief.md`, `facts.md`, `r1-digest.md` and `r2-instructions.md`, not all raw
   R1 reports. A seat may open the raw report of a position it attacks.
8. **Fact check** of every new claim an attack rests on. Append to `facts.md`.
9. **Open cruxes?** Light council: go to step 11. Full council: if R2 leaves open cruxes, write the settled points S1…
   and the open cruxes C1… with numbers in `r3-instructions.md` and start R3 only for the seats that dissent on them;
   with no open crux, go to step 11.
10. **R3, final**, then a fact check of what it adds.
11. **The checks the seats asked for**, when they are cheap and safe. Whatever needs a build or a runtime (the container
    has no .NET SDK) goes in a "check before building" list in `facts.md` and the record. If such a check settles a
    dispute, tell Andrei: the SDK can be added to the environment's setup script.
12. **Decision record** — a Claude Doc in Russian (Andrei reads it in the app), following `templates/record.md`.
13. **The gate.** Questions to Andrei on decision cards (`request_decision`), one per card, recommendation first. Card
    limits are in bytes and Cyrillic takes 2 bytes per letter: option label ≤ 40 bytes (about 20 letters),
    consequence and reason ≤ 200 bytes (about 100 letters). Record the answers in `README.md` in the same turn they
    arrive; after the last answer, say the gate is closed.
14. **Usage.** Close `usage.md` with one line against the targets: a light council at most 0.7M subagent tokens, a
    full one at most 1.4M. It is a measurement, not a gate: a council over the target is still finished. (The last full
    council before these targets: 25 subagent runs, about 2.5M tokens.)

## Common failures

- **The brief changes after R1** (in the pilot it changed four times before R1). A changed question means a new
  council.
- **A fact one seat stated confidently becomes a premise.** Before the next round, only through `facts.md`.
- **A vague attack.** "This might be complex" is not an attack: send it back to the seat for a scenario.
- **A digest that takes sides.** It reports positions with their seat and file; rulings belong to the fact check.
- **A technical question in the gate.** If the seats agree and a check confirms it, it is not a question for Andrei
  but an item "settled by a check" in the record.
- **A decision record that lives only in chat.** Everything goes in the council folder and the Claude Doc.

## Done when

- [ ] Andrei confirmed the brief and it did not change after R1; a full council only if he picked it
- [ ] Every seat ran read-only, its reports are saved unedited and within the caps
- [ ] `facts.md` closes every disputed claim: verified (how) or unverified (why it matters)
- [ ] `usage.md` is filled in, with the line against the targets
- [ ] The decision record is published, the gate asked, Andrei's answers in `README.md`
