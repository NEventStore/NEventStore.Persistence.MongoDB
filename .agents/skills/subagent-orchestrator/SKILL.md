---
name: subagent-orchestrator
description: OpenAI Codex specific. Orchestrate parallel subagents via Codex's collaboration `spawn_agent` tool, with task routing, exclusive ownership, acceptance checks, approval control, integration, and final verification. Skip small or tightly coupled work.
---

# Subagent Orchestrator

Requires Codex's collaboration `spawn_agent` tool; not applicable to other agent runtimes.

The root retains requirements, decisions, approvals, integration, and final verification.

## Plan

Delegate when two or more bounded units can progress independently or an isolated review materially improves confidence.
Stay single-agent for small, ordered, shared-state, or single-bottleneck work.

Before spawning, read [routing.md](references/routing.md) and classify each proposed work unit.
Use the available `spawn_agent` model list as the runtime source of truth.

The root selects the smallest agent count that covers independent units: one agent per non-overlapping unit, plus an independent verifier only when justified. Never fill available slots without a bounded unit.

For every spawned unit, the root selects its class, then the least expensive available model and lowest sufficient effort from the routing table. Escalate only when the unit's ambiguity, consequence, or verification depth requires it.

Pass `model` and `reasoning_effort` explicitly unless using the `inherit` route.

Track each agent's ID, assignment, ownership, and status. Add dependencies and acceptance-check results for multistage work. Record model, effort, or fork mode when they matter to the assignment.

Each unit needs:

- ID, goal, class, dependencies, and inputs.
- Exclusive ownership, allowed actions, and forbidden side effects.
- Observable acceptance checks.
- Return contract: concise findings, changed files, commands/results, uncertainty, and blockers. Use evidence pointers; omit raw logs, source dumps, and assignment restatement unless requested.

Never overlap write ownership.
Make one unit read-only or serialize conflicts.
Stabilize shared inputs before parallel consumers start.

## Execute

Run dependency-free, non-overlapping units concurrently within available slots; serialize dependencies.
Continue useful root work while agents run.

Give each agent a self-contained prompt containing its applicable requirements, decisions, permissions, ownership, and checks.

Unless a work unit explicitly owns coordination or further decomposition, include this boundary verbatim: `Complete this assignment directly; do not spawn other agents.`

Use `fork_turns: "none"` for self-contained assignments. Include the requirements, decisions, and inputs the agent needs in its brief. Use `"all"` when the assignment depends on conversation context that would be expensive or error-prone to restate. Follow the fork modes and override restrictions exposed by the current session.

Use collaboration `spawn_agent`, never a user-visible task/thread tool. If unavailable, work single-agent and disclose it.

Delegation never expands authority. Agents stop and report actions needing new approval. Delegate destructive/live actions, commits, pushes, or external messages only when that exact action is authorized; otherwise delegate read-only preparation.

Subagents inherit the parent turn's sandbox and approval settings. A read-only brief limits the assignment. Sandbox enforcement comes from the session. [Approvals and sandbox controls](https://learn.chatgpt.com/docs/agent-configuration/subagents#approvals-and-sandbox-controls)

## Coordinate

- Agents may message dependency findings directly to affected agents, but must also notify the root.
- The root confirms any changes to assignments or dependencies.
- Use follow-ups only for bounded continuation on the same ownership surface.
- Interrupt work that became invalid, unsafe, duplicated, or outside scope. [Codex orchestration controls](https://learn.chatgpt.com/docs/agent-configuration/subagents#orchestration-and-thread-controls)
- Resolve conflicts with evidence and precedence; never average incompatible conclusions.
- On failure, fix context, decomposition, ownership, or checks before escalating model/effort.

## Finish

The root integrates unless an explicit integration unit owns it. Accept results only after their checks pass.

Verify the integrated result against the acceptance checks, preserve approvals and unrelated changes, then report artifacts, results, skips, uncertainty, and blockers. Never claim completion with unresolved required work, checks, approvals, or integration verification.

## Final reporting

When the user explicitly invokes this skill and agents were spawned, include a compact `Subagents used` table with each assignment and outcome. Include requested model or effort when relevant. Report effective settings only when the runtime exposes them.
