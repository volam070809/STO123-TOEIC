## Graphify: local code navigation

The local graph is `graphify-out/graph.json`; the installed project skill is
`.codex/skills/graphify/SKILL.md`. Read this file at the start of a repository task.
When the user invokes `$graphify`, use the installed Graphify skill.

For a code change or codebase question:

1. Check that the graph exists and compare its age and `graphify-out/manifest.json`
   with the affected source files. A newer file timestamp alone does not prove its
   content changed; an absent or outdated entry means the graph may be stale.
2. If the graph is usable, run one focused `graphify query "<symbol or workflow>"
   --budget 700` before a broad repository search. Use `graphify path` or
   `graphify explain` only when the relationship needs it. Reuse paths already found.
   If `graphify-out/wiki/index.md` exists, it can help with broad navigation.
3. Inspect the relevant source code before editing. Use targeted `rg` or direct
   file inspection when a symbol is absent, the graph is stale, or the extractor
   cannot represent the file. Never treat graph edges as proof of runtime behavior.
4. Run relevant tests after a code change. Refresh the graph on demand with
   `graphify update .` when indexed source changed; do not run a full extraction or
   update after a task with no source changes. Check the installed CLI before using
   flags that may differ by version.

Keep Graphify local: set `GRAPHIFY_BACKEND=none` for Graphify commands where
supported. Do not use Graphify Cloud, Claude CLI, external semantic extraction,
or paid LLM APIs. If a full reindex is genuinely required, use code-only
extraction; use `cluster-only --no-label` when clustering needs a local refresh.
Do not install background watchers or hooks for this workflow.

Known indexing gaps: `STO123/Program.cs`, many test files and some SQL migrations
have only file-level nodes; CSS is outside the current code-only manifest.
Inspect those files directly with a narrow search. The `PreToolUse` entry in
`.codex/hooks.json` is a compatibility no-op, so these steps are manual. The
generated graph stays on this machine in `graphify-out/`; its Git ignore rule
does not remove it. Future sessions should check it again rather than assume
automatic freshness or automatic hook enforcement.
