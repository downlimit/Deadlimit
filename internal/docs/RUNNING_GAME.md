# Deadlimit — running Deadlock and retail VPK replacement

## 2026-08-23 — live-confirmed file-lock behavior

### Evidence

Confirmed by the user's live Ivy `BUILD & TEST` run with retail Deadlock already running:

```text
The process cannot access the file
...\game\citadel\addons\pak01_dir.vpk
because it is being used by another process.
```

The failure happened while Deadlimit tried to inspect/replace the currently deployed VPK slot. This is direct pipeline evidence that the running retail client holds the loaded VPK with a Windows file lock strong enough to block Deadlimit's update transaction.

### Conclusion

For the current retail VPK model-replacement workflow, changing/reselecting the hero cannot solve the deployment problem by itself because the new VPK cannot be written while the old archive is locked.

Therefore the previous hot-reload hypothesis is rejected for **replacement of the same retail `pak##_dir.vpk` file while Deadlock is running**.

This does not claim that every Source 2 resource is incapable of hot reload through other development/tool paths. It only records the behavior proven for Deadlimit's retail addons VPK deployment path.

### Deadlimit behavior

`BUILD & TEST` now checks whether the Deadlock client process is running before VPK ownership inspection or deployment.

If Deadlock is running:

```text
BUILD & TEST click
→ explain that the loaded VPK is locked
→ ask once whether Deadlimit may close Deadlock automatically
→ No  = cancel without touching the build/deploy transaction
→ Yes = request normal window close
→ wait briefly
→ if the process remains, terminate the remaining Deadlock process tree
→ verify the game is actually stopped
→ continue normal BUILD & TEST
```

After a successful build the existing completion dialog again offers `LAUNCH DEADLOCK GAME`.

The build button tooltip also states that a running client must be closed because it locks the loaded VPK.

### Defensive error handling

`VpkSlotOwnershipService` now converts a raw Windows `IOException` during VPK hashing into a specific Deadlimit error explaining that the retail VPK is locked and that Deadlock or another VPK viewer must be closed.

This remains useful if some other process locks the archive or if Deadlock is launched during the build after the initial running-process check.

### External context

Current Deadlock modding guidance (rechecked 2026-08-23) still installs replacement archives directly as `game/citadel/addons/pak##_dir.vpk`. Current Source/Valve VPK behavior is also consistent with archives being held open by a running game process. The decisive evidence for Deadlimit, however, is the live Windows lock observed in this project rather than external assumptions.
