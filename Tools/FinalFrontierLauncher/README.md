# The Final Frontier Launcher

This is the first development scaffold for a custom launcher path for The Final Frontier.

It is intentionally minimal. It does not replace the upstream SS14 launcher yet and does not add launcher-only enforcement. Its current purpose is to verify and log the early launch flow against the testing server.

## Branding

The scaffold uses Final Frontier-facing product strings from `launcher.settings.json`:

- `The Final Frontier Launcher`
- `The Final Frontier`
- `The Final Frontier Testing`

## What it currently does

1. Loads `launcher.settings.json`.
2. Logs the selected Final Frontier server target.
3. Queries the configured status and info endpoints.
4. Validates and logs the server engine, fork, and version metadata.
5. Writes launcher logs to the user's local app data folder.
6. Optionally launches a local client executable in launcher mode when `FINAL_FRONTIER_CLIENT_PATH` is set.

The client is started with `--launcher`, `--connect-address`, and `--ss14-address` so the RobustToolbox launcher connection state is used rather than the normal main menu path.

## Running locally

From this folder:

```bash
dotnet run --project FinalFrontierLauncher.csproj
```

To print the active config:

```bash
dotnet run --project FinalFrontierLauncher.csproj -- --print-config
```

## Branch usage

Use `feature/custom-launcher-flow-diagnostics` for launcher development and diagnostics.
Use `testing` for normal server and gameplay changes.
Use `master` only for release-ready production changes.

## Next steps

- Replace the placeholder local server address with the real Final Frontier testing endpoint.
- Confirm the exact SS14 client launch arguments for direct connect in this fork.
- Move this from a console scaffold into a fork of `SS14.Launcher` once the server metadata and update path are proven.
- Add clearer launcher UI branding, icons, and server selection only after the connection path is stable.
- Keep launcher-only enforcement out of this branch until the basic launch, connect, and character creation flow is proven.
