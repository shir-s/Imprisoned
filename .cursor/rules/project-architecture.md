# Project Architecture Rules

These rules ensure Cursor maintains a clean, scalable project structure.

## Folder Structure
- Preserve the user’s existing folder layout under `Assets/`.
- Do not move or rename folders unless explicitly instructed.
- Place new scripts into appropriate directories:
  - `Scripts/` or specified subsystem folder.
  - Subfolders for systems (Player/, Combat/, Audio/, etc.)

## Assembly Definitions
- Respect existing `.asmdef` boundaries.
- Do not move scripts across assemblies unless requested.
- If creating new subsystems, ask the user before generating new `.asmdef` files.

## OOP Design Principles
- Follow SOLID principles.
- Avoid giant classes; keep responsibilities narrow.
- Favor composition over inheritance.
- Prefer interfaces for cross-system communication.
- Encapsulate logic; avoid public fields unless required.

## Architecture Patterns
- Use ScriptableObjects for global configuration.
- Use lightweight managers only when required.
- Avoid static state unless absolutely necessary.
- Use event-driven patterns when appropriate.
- Do not implement ECS-style code unless requested.

## Multiplayer / Networking (future-proof)
- Keep systems deterministic when possible.
- Separate input from actions to enable future multiplayer extensions.

## File Modifications
- When refactoring:
  - Explain the plan.
  - Modify only the files affected.
  - Maintain method signatures unless explicitly asked to change them.

## Code Safety
- Prevent null reference issues by validating serialized fields in `Awake()`.
- Use defensive programming in core systems.

These architecture rules ensure Cursor behaves like a senior Unity engineer and keeps your project stable, modular, and scalable.
