# Agent Operating Rules

These rules are permanent for this repository.

## Engineering

- Important gameplay logic belongs in C#.
- Prefer testable plain C# logic where practical.
- Use Unity components as adapters between game logic and scenes.
- Avoid large visual scripting graphs.
- Keep changes scoped to the current approved phase.

## Assets and project settings

- Do not manually edit Unity YAML unless unavoidable.
- Never carelessly delete or recreate `.meta` files.
- Do not change the Unity editor version without explicit approval.
- Do not add paid or externally licensed assets without approval.
- Keep `Assets`, `Packages`, and `ProjectSettings` under source control.

## Scope

- Do not add multiplayer, story, crafting, or other out-of-scope systems.
- Do not implement future-phase systems early.

## Completion

- Update documentation after every completed phase.
- Run available tests and check for compiler errors before committing.
- Validate affected scenes and a development build when build support is available.