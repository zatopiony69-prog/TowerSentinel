# TowerSentinel

[![build](https://github.com/zatopiony69-prog/TowerSentinel/actions/workflows/build.yml/badge.svg)](https://github.com/zatopiony69-prog/TowerSentinel/actions/workflows/build.yml)
[![license: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](LICENSE)

TowerSentinel is an EXILED plugin for SCP: Secret Laboratory that deploys a
networked defensive structure around the Surface admin tower while a Tutorial
player is present. The turret retaliates against direct attacks, grenades that
touch the structure, and shots that actually strike it.

## Features

- Animated barricades and a compound laser/minigun turret.
- Obstruction-aware targeting: walls and players block shots and retaliation.
- A single rate-limited firing routine prevents coroutine and network-toy spam.
- Deterministic Surface-room-relative placement with configurable offsets.
- Complete round-end, disable, animation, temporary-effect, and failure cleanup.
- No telemetry, external network requests, file collection, or native code.

## Requirements

- SCP: Secret Laboratory Dedicated Server.
- EXILED 9.14.2 or newer compatible 9.x release.
- The current release was built against EXILED 9.14.2 and dedicated-server
  build `23340795`.

## Installation

1. Download `TowerSentinel.dll` from the latest GitHub Release.
2. Place it in the EXILED `Plugins` directory.
3. Restart the server and edit the generated EXILED configuration if desired.

Only binaries attached to releases in this repository are official builds.
Every tagged release is rebuilt by GitHub Actions, includes a SHA-256 checksum,
and becomes immutable when it is published. Verify a downloaded release with:

```powershell
gh release verify v1.0.0 --repo zatopiony69-prog/TowerSentinel
gh release verify-asset v1.0.0 TowerSentinel.dll `
  --repo zatopiony69-prog/TowerSentinel
```

## Configuration

| Setting | Default | Purpose |
| --- | ---: | --- |
| `is_enabled` | `true` | Enables the plugin. |
| `debug` | `false` | Enables EXILED debug logging. |
| `turret_damage` | `100` | Heavy-laser damage. |
| `minigun_damage` | `15` | Damage per retaliatory minigun shot. |
| `tower_activation_radius` | `15` | Tutorial proximity required for deployment. |
| `shooting_detection_range` | `200` | Maximum range for a shot to strike the tower. |
| `grenade_detection_radius` | `8` | Explosion overlap radius. |
| `retaliation_cooldown` | `0.75` | Minimum delay between attacks. |
| `admin_tower_offset_x/y/z` | `39 / 14 / -32` | Surface-room-local tower position. |
| `barricade_width/depth/height` | `10 / 10 / 3.4` | Barricade dimensions. |
| `turret_offset_x/z` | `-4.5 / 0` | Turret position relative to the structure. |
| `structure_y_offset` | `-1.5` | Vertical adjustment for the whole structure. |

Invalid, non-finite, negative, or extreme values are clamped to safe limits and
reported in the server log.

## Building

The project targets .NET Framework 4.8 and restores EXILED 9.14.2 from the
official `ExMod.Exiled` NuGet package. It also needs the game's managed Unity,
Mirror, and MEC assemblies.

When the repository is placed beside `SCPSL_Data`, the path is detected
automatically:

```powershell
dotnet build TowerSentinel.csproj -c Release
dotnet test TowerSentinel.Tests/TowerSentinel.Tests.csproj -c Release
```

For another checkout location, provide the managed directory explicitly:

```powershell
dotnet build TowerSentinel.csproj -c Release `
  -p:GameManagedPath="C:\path\to\SCPSL_Data\Managed"
```

Local deployment is intentionally opt-in:

```powershell
dotnet build TowerSentinel.csproj -c Release -p:DeployPlugin=true
```

GitHub Actions obtains the dedicated-server reference files directly through
Valve SteamCMD; game assemblies are never committed to this repository.

## Security and authenticity

Security reports should follow [SECURITY.md](SECURITY.md). The canonical
`main` branch is protected and release artifacts are produced only from reviewed
source with passing checks. Forks remain separate repositories and cannot alter
the canonical project. Version tags matching `v*` are protected from updates and
deletion after creation.

TowerSentinel is licensed under GPL-3.0-only. The license allows inspection and
modification, but redistributed versions must retain the GPL terms and source.
Modified projects must not present themselves as official TowerSentinel builds.

Copyright (C) 2026 zatopiony69-prog.
