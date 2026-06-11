# Custom Launcher Development Branch

This branch is reserved for testing the Final Frontier custom launcher path from launcher startup through to the player character creation screen.

## Scope

This branch should be used for development-only diagnostics around:

- launcher/server status and build metadata checks;
- client launch and connection failures;
- lobby bypass behaviour;
- first-time player routing;
- character setup screen opening;
- character creation/save failures;
- disconnects or silent hangs before character creation.

## Current target flow

1. Player opens the Final Frontier launcher.
2. Launcher shows the Final Frontier testing server.
3. Launcher queries server status/build metadata.
4. Launcher launches the matching client build.
5. Client connects to the testing server.
6. Existing players load into their selected/available character.
7. New players are routed directly to character creation.
8. Character creation failures are logged with enough context to identify the cause.

## Logging requirements

Development logs should include clear context for every transition in the path:

- player/session identifier where safe;
- whether the player already has a profile/character;
- whether lobby bypass is enabled;
- why the player is being spawned or sent to character setup;
- any rejected character creation/save attempt;
- missing build metadata or launcher-facing config;
- unexpected disconnects during pre-spawn or character setup.

Avoid logging secrets, tokens, IP addresses unless already part of normal server logs, or full personally identifiable information.

## Testing checklist

- Fresh launcher install connects to the testing server.
- Cached launcher install reconnects without using stale content.
- Server offline produces a clear launcher error.
- Missing or bad build metadata produces a clear launcher error.
- New account with no characters reaches character creation.
- Existing account with a character spawns normally.
- Character creation save failures are visible in logs.
- Disconnects during character setup are visible in logs.

## Enforcement

Do not add launcher-only enforcement on this branch yet. This branch is for proving the launch/connect/character-creation path and adding diagnostics only.
