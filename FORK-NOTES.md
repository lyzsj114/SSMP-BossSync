# SSMP Boss Sync Fork

This repository is an experimental fork of
[Silksong.MultiplayerMod](https://github.com/Silksong-Modding/Silksong.MultiplayerMod)
based on PR #99 world synchronisation.

## Current version

`0.3.1.3` focuses on save and boss synchronisation stability:

- Stable scene host ownership: the first player in a scene keeps ownership until
  they leave or disconnect. Death and ordinary re-entry do not trigger transfers.
- Entity cloning suppresses `SetParent` FSM actions while the clone hierarchy is
  being constructed, avoiding Unity activation/deactivation errors.
- `FisherWalkerIdleTimeLeft` and `FisherWalkerTimer` no longer use network save
  updates. Both fields previously generated over 100,000 updates per session.
- Full-save compound fields, 64-bit memory-orb progress, packet dispatch ordering,
  and null-string handling from `0.3.1.2` are retained.

## Validation

```powershell
dotnet build SSMP.sln -c Release
dotnet run --project SSMP.SaveSync.Tests/SSMP.SaveSync.Tests.csproj -c Release
```

The current test suite contains 82 checks, including 1,319 enabled native
PlayerData fields.

Real two-PC Unity gameplay, especially boss encounters, still needs manual
verification.

## Installation

Copy the contents of the packaged `客户端` directory into:

```text
Hollow Knight Silksong/BepInEx/plugins/SSMP/
```

Every player must use the same build. Keep local configuration, certificates,
server saves, and authentication keys private.

## License

This fork retains the upstream LGPL-2.1-or-later license. See `LICENSE`.
