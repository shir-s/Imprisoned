# Unity Coding Rules

These rules apply to every generated or modified script.

## Language & Engine
- Use C# for Unity 6 (6000.x).
- Always ensure scripts compile on the first try inside Unity.
- Only use namespaces available in Unity (no System.Windows, no non-existent .NET APIs).

## Structure
- Use `[SerializeField] private` fields for Inspector references.
- Use `Awake()` for reference caching.
- Use `Start()` for initialization logic.
- Use `OnEnable()`/`OnDisable()` for event subscription lifecycles.
- Keep each MonoBehaviour focused on a single responsibility.

## Performance & Memory
- Avoid GC allocations inside `Update()`.
- Do not use LINQ in hot paths.
- Avoid string concatenation in loops.
- Use cached references — never use `GetComponent()` repeatedly.
- Use `Time.deltaTime` for movement; use `Time.fixedDeltaTime` for physics.

## Code Style
- PascalCase for classes, methods, and properties.
- camelCase for fields and local variables.
- do not use Hungarian notation or prefixes like `_myVar` unless instructed.

## Gameplay Systems
- Separate data (ScriptableObject), logic (C# classes), and presentation (MonoBehaviours).
- Use scriptable events, C# events, or UnityEvents only when appropriate.
- Keep managers minimal and focused; prefer modular systems.

## Inspector & Prefabs
- Assume the user prefers to connect references in the editor.
- Never auto-create hidden objects unless explicitly requested.

## Documentation
- Add concise comments for complex logic.
- Do not over-comment simple code.

These rules ensure consistency and clarity across all Unity code produced inside Cursor.
