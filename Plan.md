# SS14 Final Frontier Development Priority Plan

## Project Context

`bbuske/ss14-final-frontier` is a fork of Space Station 14 intended for a persistent, private, HRP, whitelist-only server. Because the server is persistent, priority should be given to systems that protect world saving, player identity, economy, access control, security/medical workflows, and admin reliability before adding wider gameplay content.

This plan converts the current requested feature list into an ordered implementation roadmap suitable for Codex-assisted development.

## Guiding Rules for Codex

1. Do not implement everything in one PR.
2. Use small branches grouped by system:
   - `fix/persistence-open-bugs`
   - `fix/stockmarket`
   - `fix/id-records`
   - `feature/secwatch-crimeassist`
   - `feature/triad-targeting-shipyard`
   - `feature/mail-system`
   - `feature/additional-races`
   - `feature/martial-arts-surgery`
3. Before changing code, locate existing upstream implementations from:
   - Frontier
   - Foxtrot
   - Triad
   - Persistence 14
   - Current SS14 upstream
4. Prefer clean porting and adaptation over rewriting from scratch.
5. Preserve existing Final Frontier custom behaviour unless it directly conflicts with the requested system.
6. Every PR must build successfully with `dotnet build`.
7. Every system port must include:
   - source files
   - shared/server/client code where required
   - YAML prototypes
   - localization strings
   - sprites/audio metadata where applicable
   - migration or data compatibility notes if persistence is affected

---

# Priority 0 — Baseline Stabilisation (COMPLETED)

## Goal

Make sure the current branch builds, runs, and saves correctly before adding large imported systems.

## Tasks

### 0.1 Build and test baseline

Run:

```bash
python RUN_THIS.py
dotnet build
```

Then start a local server and confirm:

- server boots
- maps load
- persistence save works
- no immediate prototype errors
- no missing localization errors that block startup
- no client/server shared type mismatch

## Acceptance Criteria

- `dotnet build` passes.
- Server boots locally.
- A test map can be saved manually.
- Any existing startup warnings/errors are logged into a tracking note before feature work begins.

---

# Priority 1 — Fix Open Bugs (COMPLETED)

## Goal

Resolve current bugs before feature imports make debugging harder.

## Task 1.1 Fix Smartfridge breaking map/persistence saving

Current issue: placing an item into a Smartfridge prevents the map from saving. This affects autosave, `persistencesave`, and `savemap`.

### Investigation Steps

1. Locate Smartfridge entity/components/prototypes.
2. Reproduce locally:
   - spawn Smartfridge
   - insert item
   - trigger manual save
   - inspect server console error
3. Identify whether the issue is caused by:
   - non-serializable component data
   - container state
   - entity storage behaviour
   - invalid map-save transform/state
   - prototype mismatch
4. Compare with current upstream SS14 Smartfridge implementation.
5. Patch the smallest possible cause.

### Acceptance Criteria

- Smartfridge can hold items.
- Manual map save works after Smartfridge use.
- Persistence save works after Smartfridge use.
- No admin deletion workaround is required.

## Task 1.2 Adjust plasma tank persistence balancing

Current issue: plasma tanks are depleted every ~30 minutes, which is unsuitable for a persistent server.

### Implementation Options

Pick the least invasive option:

1. Increase plasma tank capacity.
2. Reduce singularity plasma consumption rate.
3. Create a Final Frontier-specific persistent plasma tank variant.
4. Add a config/prototype value so persistent servers can tune this without code changes.

### Acceptance Criteria

- Singularity gameplay remains functional.
- Plasma tanks last significantly longer on a persistent server.
- Balance change is clearly documented in the PR.

---

# Priority 2 — Fix the Stockmarket (COMPLETED)

## Goal

Repair the Stockmarket before adding new economy-adjacent systems such as shipyards, mail logistics, and faction systems.

## Rationale

A persistent server needs working economy systems early because later systems may depend on money, trade, goods, ship purchase flow, or job income. If Stockmarket is broken, adding more economy features creates more places for bugs to hide.

## Tasks

1. Locate existing Stockmarket code/prototypes/UI.
2. Identify whether the breakage is:
   - server-side logic
   - client UI
   - network event/message issue
   - missing prototypes
   - missing localization
   - persistence/database issue
   - dependency removed during upstream merge
3. Compare with the source fork/version where Stockmarket works.
4. Restore core flow:
   - view market
   - buy/sell or interact as intended
   - prices update correctly
   - state persists if required
5. Add admin/debug commands or logs if useful for future diagnosis.

## Acceptance Criteria

- Stockmarket UI opens.
- Stockmarket data loads.
- Core buy/sell or intended interaction works.
- No server exceptions during use.
- Behaviour survives restart if persistence is expected.

---

# Priority 3 — ID System and Records Repair (COMPLETED)

## Goal

Fix identity, access, crew records, criminal records, and medical records before adding security/medical feature systems.

## Rationale

The current Persistence 14 custom ID system is useful because it allows custom jobs and access on the fly, but it only works properly on the passenger ID spawned with the player. This causes major problems for admin-spawned cards, bought Captain IDs, PDA IDs, and job-specific crew IDs.

Security and medical tools should not be built on a broken ID/records base.

---

## Task 3.1 Investigate current custom ID implementation

### Known Problem

The custom ID system appears to bind access/identity to the original spawned passenger ID, player name, or entity ID. If a new ID card is bought or spawned and given to someone, access may not work unless an admin renames the target while the ID is in hand or pocket.

### Investigation Steps

1. Locate Persistence 14 custom ID code.
2. Trace how ID cards store:
   - character name
   - job title
   - job icon
   - access tags
   - station/crew ownership
   - entity UID or player session
3. Test these scenarios:
   - spawned passenger ID
   - spawned Captain ID
   - purchased Captain ID
   - PDA-contained ID
   - aghost PDA ID
   - admin tool ID
   - renamed player with ID in hand
   - renamed player with ID in pocket
4. Identify where identity/access is cached or bound incorrectly.

---

## Task 3.2 Make job-specific IDs work without admin intervention

### Desired Behaviour

Station crew should spawn with job-specific IDs showing:

- correct character name
- correct job title
- correct job icon
- correct access
- working PDA insertion
- working door/device access
- working records linkage

If an ID is spawned, purchased, or reassigned, it should work after being properly configured without requiring rename hacks.

### Proposed Implementation Direction

Refactor ID ownership/access so the ID card stores its own authoritative state instead of relying on fragile player/entity-name binding.

The ID should carry:

```text
CharacterName
JobTitle
JobIcon
AccessTags
CrewRecordId / CharacterRecordId
CriminalRecordId
MedicalRecordId
GeneralRecordId
```

Where possible, link records by stable character/record IDs rather than display name alone.

### Acceptance Criteria

- Spawned job IDs work.
- Bought/reassigned Captain ID works once configured.
- PDA ID works.
- Admin tool ID works.
- Aghost PDA ID and admin ID behaviour are consistent.
- Access checks use the card’s current access list.
- No rename workaround is required.

---

## Task 3.3 Repair Criminal Records Computer

### Known Problem

The Criminal Records Computer does not work. It may have been broken by the new faction control computer or ID computer changes.

### Desired Behaviour

Security should be able to use the Criminal Records Computer independently of the ID computer.

### Tasks

1. Locate criminal records UI/server systems.
2. Check whether it still queries old record storage.
3. Compare with the new ID computer record model.
4. Update it to read/write the same criminal record data used by the ID system.
5. Ensure access restrictions still apply to security roles.

### Acceptance Criteria

- Security can open the Criminal Records Computer.
- Security can search crew.
- Security can view criminal status/notes/wanted data.
- Security can update criminal records if their access allows it.
- Changes are reflected in the ID computer if both systems show the same record.

---

## Task 3.4 Repair Medical Records Computer

### Known Problem

The Medical Records Computer does not work, and the upstream vanilla one may also be broken.

### Desired Behaviour

Doctors and medical staff should be able to use the Medical Records Computer without needing access to the ID computer, which only the CMO can access.

### Tasks

1. Locate medical records UI/server systems.
2. Confirm whether vanilla is also broken.
3. Patch locally if upstream is broken.
4. Update the medical records computer to use the same record data as the ID system.
5. Ensure medical access gates are correct.

### Acceptance Criteria

- Medical staff can open the Medical Records Computer.
- Medical staff can view medical records.
- Medical staff can update medical notes/status where intended.
- CMO/ID computer remains separate and higher privilege.
- Medical record changes remain linked to the correct crew member.

---

## Task 3.5 Crew Records Computer

### Goal

Crew records should be restored after ID/criminal/medical records are fixed.

### Acceptance Criteria

- Crew records are searchable.
- Crew records show name, job, access/job assignment, and linked record status.
- Crew records do not desync from ID card data.
- Round-start and persistent crew both work.

---

# Priority 4 — Security Systems: SecWatch and CrimeAssist

## Goal

Bring in security tooling after ID and records are repaired.

## Rationale

SecWatch and CrimeAssist likely depend on identity, criminal records, wanted status, access, alerts, or crew data. Importing them before the record layer is repaired risks duplicating bugs.

---

## Task 4.1 Get SecWatch in

### Tasks

1. Locate the source implementation.
2. Identify required dependencies:
   - criminal records
   - wanted status
   - station/crew records
   - security access
   - radio/alert integration if any
3. Port server/shared/client systems.
4. Port UI, localization, prototypes, and sprites.
5. Adapt record lookups to Final Frontier’s repaired records system.
6. Test against live criminal record updates.

### Acceptance Criteria

- SecWatch opens and displays valid crew/security data.
- SecWatch reflects criminal record changes.
- SecWatch respects security access permissions.
- No stale/null crew records.

---

## Task 4.2 Get CrimeAssist in

### Tasks

1. Locate source implementation.
2. Identify whether it integrates with:
   - criminal records
   - fines
   - warrants
   - sentencing
   - security alerts
   - evidence/crime categories
3. Port required dependencies.
4. Connect it to the repaired criminal records system.
5. Ensure it does not bypass security access.

### Acceptance Criteria

- CrimeAssist opens.
- Security can use it for intended workflows.
- It reads/writes criminal records correctly.
- It does not require ID computer access to function.
- No duplicated criminal record state.

---

# Priority 5 — Targeting System

## Goal

Add targeting after security/records systems are stable.

## Important Note

The requested shipyard consoles and targeting system should be Triad ones, not Frontier ones. Do not blindly port Frontier targeting if Triad has a different implementation.

## Tasks

1. Locate Triad targeting implementation.
2. Compare with Frontier targeting only for reference, not as the target source.
3. Identify dependencies:
   - ship/grid systems
   - weapons
   - consoles
   - radar/sensors
   - permissions/access
   - UI network messages
4. Port shared/server/client systems.
5. Port all YAML prototypes and localization.
6. Test on ships/grids relevant to Final Frontier.

## Acceptance Criteria

- Targeting console opens.
- Valid targets are detected.
- Target selection works.
- Targeting interacts correctly with ship weapons/systems.
- No dependency on Frontier-only assumptions unless explicitly adapted.

---

# Priority 6 — Shipyard Consoles

## Goal

Add Triad shipyard consoles and integrate them with Final Frontier’s economy and ship systems.

## Rationale

Shipyard consoles should come after Stockmarket and before wider ship-combat polish, because ship purchasing/spawning may involve economy, access, and persistence.

## Tasks

1. Locate Triad shipyard console implementation.
2. Identify all dependencies:
   - ship prototypes
   - pricing/economy
   - docking/spawning
   - grid ownership
   - access restrictions
   - persistence handling
3. Port console UI and backend.
4. Ensure ships spawned through console persist correctly.
5. Ensure purchased/spawned ship ownership and access are correct.
6. Do not use Frontier shipyard console code unless needed only as comparison.

## Acceptance Criteria

- Console opens.
- Available ships list correctly.
- Ship purchase/spawn works.
- Spawned ships have correct ownership/access.
- Spawned ships do not break persistence saving.
- Economy deduction/payment works if applicable.

---

# Priority 7 — Mail System

## Goal

Add a manual mail system using Frontier-style envelopes and mail gear, without requiring fully automatic mail generation at first.

## Desired Scope

Implement enough for players to send physical mail and for a mailman role/job to deliver it.

## Phase 7.1 Minimal Manual Mail

### Tasks

1. Port envelopes.
2. Port mail bags/gear.
3. Port stamps/labels if required.
4. Add mail-related vending/locker/job loadout if applicable.
5. Add basic addressing/label behaviour if available.
6. Add mailman gear to relevant department/job.

### Acceptance Criteria

- Player can place items/paper into envelope.
- Envelope can be labelled/addressed.
- Mailman can carry/deliver mail.
- No automatic mail generation is required.

## Phase 7.2 Optional Future Automatic Mail

Defer automatic mail generation until the manual system is proven stable.

Potential later additions:

- automatic station mail
- job-specific mail
- player-generated parcel tracking
- mailbox/depot system
- payment or stamp economy

---

# Priority 8 — Additional Races from Foxtrot and Other Sources

## Goal

Add additional playable species/races after core identity systems are fixed.

## Rationale

Races/species often touch character creation, spawn profiles, body systems, sprites, clothing, medical, damage, languages, and records. Importing them before ID/records are fixed could create more broken crew data.

## Tasks

1. Locate Foxtrot races/species.
2. List each candidate race and dependencies.
3. For each race, port:
   - species prototype
   - body prototypes
   - organs/metabolism if any
   - damage modifiers
   - language/accent if any
   - sprites
   - clothing compatibility
   - character creation data
   - localization
4. Add races one at a time.
5. Test spawning, clothing, medical interaction, and records.

## Acceptance Criteria

- Each new race appears in character creation if intended.
- Each race spawns correctly.
- Clothing and inventory work.
- Medical scanners/records do not break.
- No missing sprite/prototype errors.

---

# Priority 9 — Martial Arts and Surgery

## Goal

Add martial arts and surgery after ID, records, medical records, and races are stable.

## Rationale

Surgery touches medical systems, body parts, organs, damage, races/species, tools, and possibly medical records. Martial arts touches combat balance, stamina, actions, and species/body logic. These should come after the medical/race foundation is stable.

---

## Task 9.1 Martial Arts

### Tasks

1. Locate source implementation.
2. Identify dependencies:
   - actions
   - stamina
   - combat mode
   - status effects
   - damage types
   - skill/trait systems if any
3. Port one martial art style first.
4. Test with human baseline.
5. Test with additional races after race porting is complete.

### Acceptance Criteria

- Martial art action/combat flow works.
- Damage/stamina effects are balanced.
- No null refs during combat.
- Works with relevant body/species types.

---

## Task 9.2 Surgery

### Tasks

1. Locate source implementation.
2. Identify dependencies:
   - body/organ systems
   - medical tools
   - surgery tables/beds
   - wounds/damage
   - anesthesia if any
   - medical records if any
3. Port minimal surgery framework first.
4. Add surgical tools/prototypes.
5. Add recipes/procedures.
6. Test with human baseline first.
7. Test with added races later.

### Acceptance Criteria

- Surgery UI/workflow opens.
- Tools work.
- Procedures complete.
- Medical gameplay does not break existing healing.
- Surgery does not break new races/species.

---

# Priority 10 — Final Integration and QA

## Goal

Make sure all imported systems work together on a persistent HRP server.

## Full Regression Checklist

### Build

```bash
dotnet build
```

Must pass.

### Startup

- Server starts.
- Client connects.
- No fatal prototype errors.
- No fatal localization errors.

### Persistence

Test save/load after:

- Smartfridge used
- Stockmarket used
- ship bought/spawned
- mail item created
- ID card edited
- criminal record edited
- medical record edited
- new race spawned
- surgery performed

### ID and Access

Test:

- passenger ID
- job-specific spawn ID
- bought Captain ID
- spawned Captain ID
- PDA ID
- aghost PDA ID
- admin tool ID
- reassigned ID
- custom job/access edit

### Records

Test:

- ID computer
- crew records computer
- criminal records computer
- medical records computer
- SecWatch
- CrimeAssist

All should reference the same underlying crew/record identity.

### Ship Systems

Test:

- Triad shipyard console
- targeting console
- ship purchase/spawn
- ship persistence
- access/ownership

### Content

Test:

- mail gear
- envelopes
- mailman delivery flow
- added races
- martial arts
- surgery

---

# Recommended PR Order

## PR 1 — Baseline Build and Open Bug Fixes

Includes:

- Smartfridge persistence/map-save fix
- plasma tank persistent balance fix
- build verification

## PR 2 — Stockmarket Repair

Includes:

- Stockmarket investigation
- restored UI/backend/data flow
- persistence/economy validation

## PR 3 — ID and Records Foundation

Includes:

- custom ID bug fix
- job-specific IDs
- spawned/bought/admin IDs
- PDA ID consistency
- shared record model clean-up

## PR 4 — Records Computers

Includes:

- Criminal Records Computer fix
- Medical Records Computer fix
- Crew Records Computer fix
- ID computer compatibility

## PR 5 — SecWatch and CrimeAssist

Includes:

- SecWatch
- CrimeAssist
- integration with repaired criminal/crew records

## PR 6 — Triad Targeting

Includes:

- Triad targeting system
- required UI/prototypes/localization
- ship/grid targeting tests

## PR 7 — Triad Shipyard Consoles

Includes:

- Triad shipyard consoles
- ship purchase/spawn flow
- economy and persistence integration

## PR 8 — Manual Mail System

Includes:

- envelopes
- mail gear
- mailman delivery support
- no automatic mail generation yet

## PR 9 — Additional Races

Includes:

- Foxtrot/additional races
- character creation support
- clothing/body/medical validation

## PR 10 — Martial Arts and Surgery

Includes:

- martial arts
- surgery framework
- surgery tools/procedures
- compatibility with added races
