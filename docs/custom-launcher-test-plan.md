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

Use the `launcher-flow` sawmill in the client and server logs. The launcher scaffold writes its own timestamped log under the configured local application data directory.

- [ ] Start the launcher with the testing server online. Confirm the launcher log records accepted `engine`, `fork`, and `version` metadata before starting the client.
- [ ] Confirm the launched client receives `--launcher`, `--connect-address`, and `--ss14-address`, enters launcher mode, and logs connection states through `Connected`.
- [ ] Stop the testing server and confirm the launcher records the failed endpoint and skips client launch.
- [ ] Return an `/info` response with a missing `build`, `engine_version`, `fork_id`, or `version`. Confirm the launcher rejects it with a specific metadata error.
- [ ] Connect an account with an existing finalized character. Confirm the server logs `route=spawn`, then a `Persistent spawn succeeded` source and the client receives the gameplay transition.
- [ ] Connect an account with no characters. Confirm the server logs `route=character-setup` with `reason=no-characters`, the client logs the setup open request, and then logs `Character setup UI opened successfully`.
- [ ] Force the setup UI container or lobby state to be unavailable in a local debug build. Confirm the client logs `Character setup UI failed to open` with the failed condition.
- [ ] Create a character successfully. Confirm the client logs one save request, the server logs the save attempt and success, and the server then logs the persistent spawn request and gameplay join.
- [ ] Submit an invalid slot, null profile, duplicate active character name, or simulate a database exception. Confirm the server logs `Character creation save rejected` or `Character creation save failed` with a reason and no secret data.
- [ ] Disconnect after reaching forced setup but before a successful spawn. Confirm the server logs `Disconnected before character setup completed` for the session identifier.
- [ ] Repeat with persistence disabled. Confirm `lobbyBypass=false` and normal lobby controls/flow remain unchanged.

## Enforcement

Do not add launcher-only enforcement on this branch yet. This branch is for proving the launch/connect/character-creation path and adding diagnostics only.
