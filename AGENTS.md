# Mochi Island

A Mac kitten that lives on the desktop. The shipping app is Swift plus a thin web bridge in `web/`. There is no local web server. The main path is a frameless kitten, an adaptive capsule, and one regular-glass beak bubble.

## Run

`Scripts/build-app.sh` builds `island/MochiIsland.app` and replaces a running copy of that app. The running app also relaunches itself when its executable on disk is newer, so a finished build does not leave the previous process on the desktop. `Scripts/notarize.sh` signs a DMG when `DEVELOPER_ID_APP` is set, and notarizes it when `NOTARY_PROFILE` is set.

`swift run -c debug MochiCoreChecks` runs the play, save, and hook checks.

## Layout

- `Sources/MochiCore/` — pet rules, save files, Cursor hook install. No AppKit.
- `Sources/MochiIsland/` — the kitten shell, the regular-glass bubble, native sound, and the message bridge.
- `Sources/MochiHookCLI/` — the program Cursor hooks run. If it fails, it still exits 0.
- `web/island.html`, `web/island.css`, `web/island.js`, `web/pets/`, `web/status/` — copied into the app as `Resources/Web`.
- `Tests/MochiCoreChecks/main.swift` — the checks.

## Messages

The page talks to Swift only through `webkit.messageHandlers.island`. Each call includes `action` and `rpc`. Swift answers with `window.mochiReply(rpc, json)`.

| action | purpose |
|---|---|
| `hotRects` | click-through rectangles |
| `glass` | glass plates behind bubbles and cards |
| `savePrefs` | kitten position (`kitten_x`, `kitten_y`, `kitten_placed`), color, sound, and look. `dock` is derived from the kitten |
| `getState` | pet snapshot plus `slots`, `selected`, and `dock` |
| `play` | play with the pet |
| `getTasks` | current Cursor tasks |
| `cursorLink` | connect or disconnect hooks for an agent. Pass `source` as `cursor`, `codex`, or `claude`. `linkAction` is `connect`, `disconnect`, or `status`. First Cursor connect needs `acknowledged: true` after the privacy line is shown |
| `cursorChoice` | Bubble choice. Writes `~/.cursor/mochi-island-reply.json`. Does not open an agent window |
| `openAgent` | Title or empty bubble space. `source` is `cursor`, `codex`, `claude`, `grok-bot`, `gpt`, or `mochi`. Brings that app forward, or opens its site when the app is not installed. `mochi` stays on this Mac |
| `setScan` | optional local file notice. Default off. It records names and times, not chat text |
| `createSlot` | Still accepted. The plus button does not create a seed; it opens Cursor, Codex, Claude, Grok, or GPT |

Do not add `fetch` to a localhost URL. Do not add a new action without a row in this table.

Swift watches `~/.cursor/mochi-island-tasks.json` and calls `window.pushTasks(tasks, state)` when that file changes. The page does not send `pushTasks`. `getState` and `getTasks` still poll every 5 seconds as a fallback. The page keeps the hot-rect bridge and does not draw the kitten, the capsule, or the bubble.

The kitten, capsule, bubble, and settings are native. Click-through is the kitten or capsule frame plus the bubble frame. Swift still calls `window.applyCapsuleChrome(info)` with the capsule frame. That is not a page-to-Swift action.

## Play

Rules live in `Sources/MochiCore/PetEngine.swift`. Finished tasks still feed the pet there. The desktop shows the kitten shell, not a page-drawn pet.

- Hunger, energy, and happiness still decay on the old intervals.
- There is no Feed button. Dragging the kitten saves its place. Clicking it opens the capsule.
- One finished Cursor task feeds the pet once. A failed task does not. The task id is stored, and only the latest 500 are kept.
- Hunger at 95, or energy at 0, makes the pet sleep. Sleep stops decay. The next finished AI task wakes it. There is no death and no New Pet button.
- Do not write copy that makes the user feel guilty for leaving.
- Dialogue slots are not seeded. A running task adds its icon only while it is still being worked on. A quiet one leaves, the same as a finished chat the user has already opened. Wait, done, and fail show a red dot and a bubble until the user opens that task. After the bubble closes, a read task that is done leaves the capsule. Wait stays until the user chooses. An icon last touched more than 24 hours ago leaves. The card shows a short line of the question while the task is running, waiting, done, or failed.

## Data

Writable files go in `~/Library/Application Support/com.mochiisland.mac/` or in `~/.cursor/` under a `mochi-island-` name. Do not write into the app bundle, the repo, or a `.venv`.

JSON writes go to a temporary file, then replace the destination. Before changing `~/.cursor/hooks.json`, copy it to `hooks.json.mochi-island.bak` and only add or remove entries marked `mochi-island`.

The hook command is the `MochiHookCLI` binary inside the app, not `python3`.

## Universal connection layer

Users connect many products from Mochi (Cursor and Codex today; more later). **All configuration happens in Settings → Agents with one Connect click.** Users must never hand-edit hook or config files.

Implementation rule: each product is an **`AgentConnector`** (`Sources/MochiCore/AgentConnector.swift`) that owns:

- that product's hook file layout and paths
- how to enable hooks in its own config (if any)
- its permission / allow-deny stdout contract
- `connect` / `disconnect` / `status` / `retarget` against the **current** app's `MochiHookCLI`

Settings shows one Connect / Disconnect control per connector. Clicking Connect runs that connector only. Adding a new product means adding a connector to `AgentConnectorRegistry.primary` and a Settings row — UX stays the same.

On every launch, `AppSession.repairInstalledHooks()` calls `AgentConnectorRegistry.retargetAll` so hooks keep pointing at this `.app`, not an older copy.

Shipped first-class connectors: **Cursor** (`CursorConnector`) and **Codex** (`CodexConnector`). Other agents may still use legacy install helpers until they get their own connector.

## Cursor and agents

Read hook status from `~/.cursor/mochi-island-tasks.json`. Do not scan agent transcripts unless the user turns that switch on, and even then do not copy prompt text into our files.

Do not drive Cursor with AppleScript or Accessibility. Do not start agents with the Cursor SDK.

Prompt and command text are not stored. Permission bubbles only say allow or deny.

Tasks in `~/.cursor/mochi-island-tasks.json` share one schema and include `source`: `cursor`, `codex`, `claude`, `grok-bot`, `gpt`, or `mochi`. Cursor tasks are written by `MochiHookCLI` after Connect Cursor installs `~/.cursor/hooks.json`. Codex tasks use the same CLI after Connect Codex writes `~/.codex/hooks.json` and turns on `features.hooks` in `~/.codex/config.toml`. Claude Code tasks use the same CLI after Connect Claude writes hooks into `~/.claude/settings.json`. Grok Bot and GPT tasks can still be written by `Scripts/mochi-upsert-task.sh` (`source=gpt` and `source=claude` are accepted). Opening an agent brings the app forward. `Codex.app` is not required: when it is missing, Codex opens `https://chatgpt.com/codex`. Claude opens `Claude.app` or `https://claude.ai`. GPT prefers `ChatGPT.app`, otherwise `https://chatgpt.com`. Grok Bot opens `Grok Bot.app` or `Grok.app` when one is installed, otherwise `https://grok.com`. `mochi` stays on this Mac. Do not use AppleScript or Accessibility to focus a text field.

## UI

Collapsed is a frameless 56×56 kitten drawn from the full `kitten-wake.png` illustration. No capsule and no glass plate. The Pet size slider (`pet_scale`, 0.5–2) scales that kitten and the open capsule together, immediately. Click opens the capsule with a spring. The kitten's top stays put and the capsule grows downward at a constant width of 56, radius 28. Inside the open capsule the kitten is an icon row, the same 28pt size as the agent icons, plus, and gear, and it sits inside the padding. The glass is system Liquid Glass (`.glassEffect(.regular)` in a `Capsule`). A drag of 3pt or more saves the screen-relative position and stays collapsed. One icon per conversation sits in the body only while that conversation is actively running (a hook touched it inside `activeTaskWindow`), waiting for allow or deny, or finished and not yet opened. After the user opens a finished icon and closes it, that icon leaves. A running icon that has gone quiet leaves too; it is not still in progress. An unanswered wait stays until the user chooses. An icon last touched more than 24 hours ago leaves. The card shows a short line of the question in every state. The selected highlight springs to the new icon. It is Liquid Glass only while it is moving, then a flat darker fill. It is not a second glass layer on the capsule. Height follows the icon count, shortens instead of shifting the kitten, and scrolls when it is still too tall. Agent icons are the installed app's icon, or `web/pets/agent-*.svg` when the app is missing. There is no chevron menu and there are no preset seed slots.

One bubble is used for a task card, an alert, and the plus picker. The picker shows the Cursor, Codex, Grok, and GPT icons that are turned on in settings, with no names. The bubble uses the same `.glassEffect(.regular)` as the capsule, on `BubbleShape`, inside `GlassEffectContainer`. The body, beak root, and beak tip share radius 12. The beak is 10×16 and aims at the anchor icon, stopping 6pt short of the capsule. When the kitten is on the right, the bubble sits to the left. On the left half of the screen it flips to the right. It does not cover the kitten or the agent icons. The capsule, bubble, picker, and settings use the same system `.regular` Liquid Glass. CSS blur, saturate, and fill are not applied to native glass.

Settings is a separate panel on regular glass. The three macOS window buttons sit at the top left inside the rounded glass. Close dismisses the panel. The tab bar is the next row: a light bar, not a dark pill. The selected tab springs with Liquid Glass only while it moves, then rests as a slightly deeper flat fill, with no second glass layer. General resets the kitten position, sets open at login, and shows the version and build. Pet is the live pet preview, its size (`pet_scale`), and a letter picker (`look` = `a`…`f`) so users can choose among placeholder pets; picking a letter updates the Size preview and the desktop capsule together. Dragging size scales the live pet only, and the settings panel placement updates the next time settings opens. Sound previews boop, chirp, mew, pop, and twinkle, plus submit, done, and stuck cues, and a muted Mac stays quiet. Agents lists each connector with status and a Connect / Disconnect button (one-click auto-config; no hand-edited files), the Cursor privacy line, the optional local-file notice, and the plus-picker toggles. Sound is native `AVAudioEngine` and plays an alert at most once a second.

Chinese uses PingFang SC. Never use `<input type="color">`.

## Release

Bundle ID `com.mochiisland.mac`, the marketing version, and `LSMinimumSystemVersion` stay in `island/Info.plist` and match the Swift package platform (`macOS 26`). Do not turn ATS off. Do not ship an ad-hoc signature. A change in `Sources/MochiCore/` or `Sources/MochiIsland/AppSession.swift` needs a matching change in `Tests/MochiCoreChecks/main.swift`.

Before handing off a build, search `Sources` and `web/island.*` for `127.0.0.1`, `8765`, `fetch(`, `python3`, `osascript`, `NSAllowsArbitraryLoads`, and `killall`. Those strings should not be there. `swift run -c debug MochiCoreChecks` should pass, and the app should open from the built `.app` without this repo.
