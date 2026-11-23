# Pet artwork

The kitten that sits on the desktop is drawn from the `pet-*.png` files in this
folder. They are loaded by `FullKitten` in `Sources/MochiIsland/AgentMarks.swift`
and selected from the visible task state in `KittenShell.petArtwork(rows:tasks:)`.

All images are PNG with an alpha channel. Every frame keeps its own transparent
canvas, so the drawn character is **not** centred in the file — see
[Placement](#placement).

## Status images

1024 × 1024. The shell picks exactly one, by priority, and the image changes only
when that choice changes.

| File | Shown when | Priority |
|-|-|-|
| `pet-needs-permission.png` | A visible task is waiting and it is a permission request | 1 |
| `pet-needs-input.png` | A visible task is waiting and it carries answer choices | 2 |
| `pet-needs-check.png` | Any other visible wait | 3 |
| `pet-working.png` | A visible task is running | 4 |
| `pet-interrupted.png` | A visible task failed | 5 |
| `pet-done.png` | A visible task finished | 6 |
| `pet-idle.png` | Nothing above applies | 7 |

"Visible" means the task still has an icon in the capsule, so the same rules the
cards already use decide the face — the artwork never invents a state.

## Drag frames

256 × 256. While the kitten is being dragged, the status image is replaced by
these.

| Direction | Files | When |
|-|-|-|
| `left` | `pet-drag-left-a.png`, `pet-drag-left-b.png` | Horizontal move, negative x |
| `right` | `pet-drag-right-a.png`, `pet-drag-right-b.png` | Horizontal move, positive x |
| `up` | `pet-drag-up-a.png`, `pet-drag-up-b.png` | Vertical move, positive y (upward) |
| `center` | `pet-drag-center-a.png`, `pet-drag-center-b.png` | Not dragging, or dragging straight down |

- The direction comes from the change between two drag samples, not from the
  total offset, so fast and slow drags look the same.
- `a` and `b` alternate at 4 fps (0.25 s) to swing the tail; they are two
  hand-picked phases, not a video. Do not interpolate between them.
- Releasing the mouse returns to the status image immediately.
- The 3 pt drag threshold and the saved screen position are unchanged.

## Placement

The delivered images were painted on different parts of their canvas, so
`AgentMarks.swift` centres the painted area instead of shipping cropped copies:

- Each status image has its painted bounds written in `placement(for:)`; the
  drag frames share the 256 × 256 default row.
- The painted area is scaled to 88% of the frame and offset so its centre lands
  on the frame centre.
- Anything outside the frame is clipped.

If new artwork is dropped in, either keep the same painted bounds or update the
numbers in `placement(for:)`. A mismatch shows up as the kitten jumping when it
switches state.

## Look tint and badge

`look` still multiplies the artwork by a colour tint and stamps the letter badge
in the bottom-right corner (`PetLookStyle`). Look `a` is white, so it leaves the
artwork unchanged; looks `b`–`f` recolour it. The badge and the tint are the
existing placeholder-pet behaviour, kept so Settings preview and the desktop
match.

## Fallbacks

1. `pet-<name>.png` from this folder.
2. `kitten-wake.png`, the earlier illustration.
3. `KittenMark`, the vector mark drawn in code.

The app is built with `Scripts/build-app.sh`, which copies this folder into
`island/MochiIsland.app/Contents/Resources/Web/pets/`. The copy inside the app
bundle is generated — edit the files here, never the bundle.

## Not wired up yet

- `pet-icon-{check,exclamation,question}-{64,128}.png` are badges for finished,
  failed, and waiting states. Nothing loads them yet; they are staged for the
  capsule icon row.
- `kitten-*.png`, `angry.png`, `dead.png`, `happy.png`, `hungry.png`,
  `playful.png`, and `sleepy.png` are the older placeholder artwork. Only
  `kitten-wake.png` is still used, as the fallback above.
- There is no animation for tier changes, walking, or waking up — the artwork is
  static per state plus the two drag phases.
