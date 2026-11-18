# Classification and Routing

Classify locally; do not spawn a classifier. Select the least expensive available route that meets the checks. Runtime `spawn_agent` metadata is authoritative.

## Classes

| Class | Typical work | Model (least to most expensive) | Effort (least to highest) |
| --- | --- | --- | --- |
| `scan` | Discovery, evidence, logs, mechanical comparison | `gpt-6-luna` | `low` -> `medium` -> `high` -> `xhigh` for synthesis |
| `scoped` | Bounded implementation, debugging, tests, docs | `gpt-6-luna` -> `gpt-6.1-terra` -> `gpt-6.2-sol` | `medium` -> `high` -> `xhigh` for edge cases |
| `deep` | Architecture, cross-cutting diagnosis, security/correctness | `gpt-6-luna` -> `gpt-6.1-terra` -> `gpt-6.2-sol` -> `gpt-6.2-astra` | `medium` -> `high` -> `xhigh` |
| `verify` | Independent acceptance review | `gpt-6-luna` -> `gpt-6.1-terra` ->`gpt-6.2-sol` -> `gpt-6.2-astra` | `medium` -> `high` -> `xhigh` for high-risk work |
| `inherit` | Work where parent context and identical capabilities matter more than specialization | Parent model | Parent effort |

For the `inherit` route, omit per-spawn model and effort overrides. Codex can still apply `[agents]` defaults or settings from a selected custom agent. Use the current session's tool schema to check which overrides are available. [Custom agents and configuration precedence](https://learn.chatgpt.com/docs/agent-configuration/subagents#custom-agents)

Effort follows ambiguity, consequence, and verification depth: `low` clear/narrow, `medium` normal work, `high` complex/edge cases, `xhigh` ambiguous/high-consequence. Do not route by title or use escalation as an automatic retry.

## Spawn Contract

```text
task_name: stable ownership-oriented name
fork_turns: "none"
model: selected available model
reasoning_effort: selected supported effort
message: goal, inputs, constraints, ownership, satisfied dependencies,
         acceptance checks, leaf boundary, and concise result schema
```

The example uses the default fork mode for self-contained assignments. Follow the forking rule in the skill when conversation history is needed.

Results use evidence pointers and omit raw logs, source dumps, and repeated assignment context unless requested.

Use `reasoning_effort` for collaboration `spawn_agent`; persistent Codex config uses `model_reasoning_effort`.
