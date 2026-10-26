# 1.2.2 validation — 2026-10-26

Built against the locally installed Sailwind managed assemblies with .NET SDK 10.0.302: zero compiler warnings and errors (NuGet audit disabled for the offline build).

Automated tests ran inside the real Sailwind player in a separate background runtime with Propeller, Unlimited Hammer 1.0.2, and a temporary test plugin. Save-slot paths were redirected to disposable workspace files. The test plugin was not included in the release.

Passed:

- Created a full disposable save using the original installed 1.2.1 DLL and a paired propeller/panel. Loaded it with 1.2.2; the pair remained operational without migration.
- Picked up and re-nailed the existing panel in air with Unlimited Hammer disabled, then enabled. The real patched hammer method accepted it; normal mod ticking registered the mount, retained pairing, and preserved the chosen position.
- Repeated the placement on a spawned vanilla furniture surface.
- Enabled the paired propeller and observed the real propulsion method execute and positive applied power.
- Unnailed the panel: propulsion stopped. Re-nailing restored eligibility without automatically restarting propulsion.
- Saved with 1.2.2 and restarted the game process: position, pairing, and stopped state were retained.
- Spawned a new unmounted panel: vanilla assigned boat ownership, and nailing in air with Unlimited Hammer disabled caused automatic registration on the correct boat.

The harness drove game methods rather than mouse input. Existing pump fixtures used the existing mount/bind API; panel placement under test used the real hammer patch and normal update loop. This covers a synthetic 1.2.1 save, not every historical user save or every mod combination.
