---
name: timberbot
description: Collaborate with a human player on Timberborn via timberbot.py. Help keep beavers alive, wellbeing high, and needs met.
version: "0.8.0"
---
# Timberbot

This is the distributable Claude Code entrypoint for playing Timberborn through `timberbot.py`.

This skill is intentionally thin. The authoritative knowledge lives in the mod docs that ship with the repo or Steam Workshop mod folder.

Before acting:

1. Prefer a local docs copy. First check whether the current working directory contains `docs/timberbot.md`.
2. If not, check the Steam Workshop mod folder docs at `%USERPROFILE%\Documents\Timberborn\Mods\Timberbot\docs\` (for example `C:\Users\Abix\Documents\Timberborn\Mods\Timberbot\docs\`).
3. If neither local docs location is available, stop and tell the user to reopen Claude from the Timberbot repo root or the Steam Workshop mod folder root. Also tell them the GitHub repo contains the same docs content.
4. Use `timberbot.py` directly. In a local clone it lives at `timberbot/script/timberbot.py`; in the distributed mod folder it is shipped alongside the DLL and docs.
5. Read `docs/timberbot.md` first. It is the single AI authority and contains both the operating workflow and the gameplay knowledge Timberbot needs.
6. Read `docs/api-reference.md` only when you need exact endpoint, parameter, response, pagination, helper, or error details.
7. Read `docs/getting-started.md` only for install, PATH, remote host, Steam Workshop path, or troubleshooting questions.

Runtime rules:

- Use `timberbot.py` directly.
- On first invocation in a session, follow the boot/link flow from `docs/timberbot.md`.
- On later invocations in the same session, skip boot unless the user explicitly wants to restart or clear memory.
- Never run mutating game API calls in parallel.
- Prefer `brain`, `find_placement`, and `find_planting` over ad hoc guessing.
- Keep action batches bounded and re-read state after making changes.
