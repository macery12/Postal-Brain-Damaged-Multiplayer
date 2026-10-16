# Weapon grips and player animation

This pass keeps the coat/gown player model and the existing combat rules. Weapon placement now uses authored profiles instead of the longest pickup-mesh dimension. Arm reach follows the primary and supporting grips, including aim pitch; bow and dual-wield variants use their appropriate hands. Native material copies preserve weapon textures, tint and glass alpha.

## Implemented presentation

| Feature | Behavior |
| --- | --- |
| Regular weapon grips | Explicit orientation, size, palm/support points and muzzle sockets. |
| Saw | Corrected display-pivot tilt and forward axis; native teeth texture scrolls at idle/active speeds using a presentation-only native damage-window flag. |
| Bow | Left-hand hold and right-hand draw along the weapon axis. |
| Dual wield | Base geometry is reused for IDs without pickup prefabs; separate left/right grips and handed native flashes. |
| Aim | Smoothed yaw/pitch; arms follow reachable grip targets. |
| Movement | Native directional run, jump/air and slide clips. |
| Crouch | Uniform body proportions, lowered pelvis, bent knees and grounded feet with preserved ankle orientation. |
| Firing and recovery | Actual shot callbacks trigger native pose/recoil; primary/secondary metadata and native effects remain separate from combat authority. |
| Charge / block / shield / melee / kick | Bounded presentation state and supporting hand/body poses; native kick notification also works while a firearm is equipped. |
| Switching | Native swap clip and brief lower/raise motion; no invented shot on switch or initial snapshot. |
| Pause | Clip, grip/recoil and teeth clocks use the shared hold's zero delta. |

Character clips are reused from the native machinegun/shotgun/rocket families, with tailored grip/action overlays. Pickup meshes do **not** provide every first-person internal mechanism animation: dedicated pump/bolt/barrel/string deformation parity and full player-death presentation remain separate polish. There is no universal native reload action to replay; do not confuse recovery presentation with a new reload mechanic. Cosmetic alignment never authorizes a hit or moves a player's gameplay collider.

## Repeat the checks

Use a private game copy, with the game closed. Build and copy the matching plugin/network pair into that copy before running:

```powershell
./scripts/Test-WeaponPresentation.ps1 -GameDir '<GameDir>' -Label review
./scripts/Test-Travel.ps1 -GameDir '<GameDir>' -WeaponPresentation -Level LEVEL_AD_Neighborhood
```

The capture runner creates an ignored `.local/weapon-<label>-<timestamp>/` directory with 768px PNGs, a native asset/clip inventory, grip measurements, crouch foot measurements and an isolated avatar-update benchmark. It captures front/back/left/right/three-quarter views, grip close-ups, +/-45 and +/-85 degree aim, crouch, early/late firing and melee, secondary, charge, block, shield, loop, air, slide and kick states. Available installed extras are captured for robustness without extending campaign compatibility promises.

Camera renders wait a frame after manually posing the skeleton; skin matrices are recalculated for the capture camera. Otherwise Unity can show the preceding body pose alongside a newly moved weapon. Hand error must remain below **0.035 m**, grounded crouch-foot error below **0.01 m**, and the model must contain no gameplay scripts/colliders. Those checks supplement visual review; they do not prove arbitrary mesh clipping is impossible.

The connected fixture performs real native inventory equips and native attack entry points for pistol, shotgun and saw in both directions. It also sends eight explicit synthetic action/movement states and checks shared pause/resume clocks. Synthetic states prove transport/presentation behavior; they are not evidence of every natural weapon mode or campaign route.

## Acceptance notes

Final exact-build results and installation are recorded in [verification](verification.md). The full matrix is 27 available weapon variants and 621 images. Review the saw, bow, rocket direction, two-hand grips, dual-wield spacing and crouch from multiple angles before exporting a build.

The focused avatar benchmark excludes equip/construction, rendering and whole-world synchronization. The 15-byte pose extension adds at most about **300 application bytes/s per sender** at the unchanged nominal 20 Hz pose rate, excluding transport overhead. No skeleton/bone stream or additional scene scan is used.
