# SS14 Final Frontier

SS14 Final Frontier is a persistent, private, HRP-focused Space Station 14 fork built for the Final Frontier community.

This repository contains the game content, prototypes, systems, maps, and server-specific changes used by the project.

## Project Focus

Final Frontier is built around:

- Persistent roleplay progression
- HRP/whitelist-focused gameplay
- Extended station systems
- Custom jobs, factions, gear, and records
- Additional species and character options
- Expanded legal, medical, cargo, and command gameplay
- Long-term feature development on top of Space Station 14

## Branches

- `master` — stable/default repository branch
- `testing` — active development and integration branch
- feature/fix branches — short-lived branches for individual Jira tasks or GitHub issues

Most active development should target `testing` first.

## Development Workflow

Recommended flow:

1. Create a branch from `testing`
2. Make the smallest safe change possible
3. Open a pull request back into `testing`
4. Test in-game where needed
5. Merge once reviewed/tested
6. Promote tested changes toward stable release branches as required

Example branch names:

```text
fix/sff-49-map-save-serialization
feature/sff-13-enable-diona
feature/sff-11-felinid-race
```

## Building

This project follows the standard Space Station 14 build process.

```bash
git clone https://github.com/buskeit/ss14-final-frontier.git
cd ss14-final-frontier
git checkout testing
dotnet build
```

For a clean rebuild after source-generator or serialization changes:

```bash
dotnet clean
dotnet build
```

## Testing Notes

When changing persistence, station records, species, maps, or entity prototypes, test:

- Server startup
- Prototype loading
- Character creation
- Spawning into the game
- Relevant UI screens
- Map autosave
- Server restart/load behaviour

Persistence-related changes should not be considered complete until map saving has been tested.

## Jira / Task Tracking

Development tasks are tracked in Jira under the `SFF` project.

Pull requests should reference the relevant Jira issue where possible, for example:

```text
SFF-13
SFF-49
```

## Contributing

Keep pull requests focused and small where possible.

Avoid large mixed PRs that combine unrelated systems, maps, balance changes, and content changes. Large feature ports should be split into smaller reviewable tasks.

## Upstream

This project is a fork of Space Station 14 and inherits large parts of its codebase, content structure, and licensing.

Original upstream:
https://github.com/space-wizards/space-station-14

## License

Code is licensed under the MIT license unless otherwise specified.

Most assets are licensed under CC-BY-SA 3.0 unless stated otherwise in their metadata files. Some assets may use non-commercial or otherwise restricted licenses. Always check asset metadata before reusing or redistributing assets.
