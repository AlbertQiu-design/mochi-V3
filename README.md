# Mochi Island

A kitten that sits on your Mac desktop and watches your coding agents.
一只趴在 Mac 桌面上、替你看守 coding agent 的小猫。

Mochi shows what each agent conversation is doing, opens a card when one needs a
decision, explains what is being asked in plain words, and keeps your answer on
this Mac. It is a native Swift app for **macOS 26 or later**. There is no local
web server and nothing is sent to a cloud.

Mochi 显示每个 agent 会话在做什么，需要你决定时弹出一张卡片，用一句人话说明
它在请求什么，并把你的选择留在这台 Mac 上。原生 Swift 应用，仅支持
**macOS 26 及以上**；没有本地网页服务，不上传云端。

**Status:** early test build. The current GitHub Release
([`v1.0-build4`](https://github.com/AlbertQiu-design/mochi-V3/releases)) is for
trying the main loop, not a finished product. Read
[Known limitations](#known-limitations) before you install.

**状态：** 早期测试版。当前 Release
([`v1.0-build4`](https://github.com/AlbertQiu-design/mochi-V3/releases)) 用于试用
主链路，不是成品。安装前请先读[已知限制](#known-limitations--已知限制)。

## Screenshots / 截图

Screenshots are captured on macOS 26 and will be added with the next test build.
Until then, the release notes and `docs/PRODUCT-SUMMARY.md` describe each screen.

截图需要在 macOS 26 上采集，会随下一个测试版补上。在此之前，各界面说明见
Release 说明和 `docs/PRODUCT-SUMMARY.md`。

## Install the test build / 安装测试版

Requires macOS 26 on Apple silicon or Intel.
需要 macOS 26，Apple 芯片或 Intel 均可。

1. Download `MochiIsland-1.0-b4.zip` from the
   [latest release](https://github.com/AlbertQiu-design/mochi-V3/releases/latest)
   and unzip it.
2. Double-click **Mochi Island**.
3. If macOS says the app cannot be opened, click **Done**, not **Move to Trash**.
   Then open it again from Finder and confirm. The build is ad-hoc signed, not
   notarized yet.
4. When the kitten appears, open Settings → **Agents** and click **Connect** for
   Cursor, Codex, or Grok Bot.

Connect writes the hook configuration for you. Nothing is hand-edited, and
connecting only affects this Mac.

1. 从 [最新 Release](https://github.com/AlbertQiu-design/mochi-V3/releases/latest)
   下载 `MochiIsland-1.0-b4.zip` 并解压。
2. 双击 **Mochi Island**。
3. 系统提示「无法打开」时点 **完成**，不要点「移到废纸篓」；再从访达重新打开
   并确认。这一版是 ad-hoc 签名，尚未公证。
4. 小猫出现后，打开设置 → **Agents**，给 Cursor、Codex 或 Grok Bot 点
   **Connect**。

Connect 会自动写好 hook 配置，不需要手改文件；连接只影响这台 Mac。

## What works today / 目前可用的功能

| Area | State |
|-|-|
| Desktop kitten | Frameless, draggable, remembers its position; four-direction drag frames while moving |
| Capsule | Click to expand a Liquid Glass capsule with the kitten, one icon per conversation, plus, and settings |
| Task cards | Agent name, status, relative time, `Q：` and `A：` two lines each, expand, choice buttons |
| Status artwork | Seven states: idle, working, done, interrupted, needs-check, needs-input, needs-permission |
| Agent connections | Cursor, Codex, and Grok Bot connect from Settings; Claude, Gemini, Copilot, and OpenCode use the legacy installers |
| Permission copy | Short labels such as "Shell needs Allow", "MCP needs Allow", "Needs Allow or Deny" |
| Sound | Click sounds plus submit, done, and stuck cues; a muted Mac stays quiet |
| Feeding | One feed per finished task; hunger, energy, and happiness decay; the pet sleeps and wakes |

| 模块 | 现状 |
|-|-|
| 桌面小猫 | 无框、可拖动、记住位置；拖动时播放四方向帧 |
| 胶囊 | 点击展开液态玻璃胶囊，含小猫、每个会话一个图标、加号、设置 |
| 任务卡片 | agent 名、状态、相对时间、`Q：` 与 `A：` 各两行、展开、选择按钮 |
| 状态形象 | 七态：idle、working、done、interrupted、needs-check、needs-input、needs-permission |
| Agent 连接 | Cursor、Codex、Grok Bot 在设置页一键连接；Claude、Gemini、Copilot、OpenCode 走旧安装器 |
| 授权文案 | 短标签："Shell needs Allow"、"MCP needs Allow"、"Needs Allow or Deny" |
| 声音 | 点击音，以及提交 / 完成 / 卡住三组提示音；系统静音时不出声 |
| 喂食 | 每个完成任务喂一次；饥饿、精力、心情衰减；会睡会醒 |

## Known limitations / 已知限制

- **Allow / Deny does not unblock the agent.** Your choice is written to
  `~/.cursor/mochi-island-reply.json` and shown on the card, but nothing reads it
  back yet. For Cursor and Codex, shell and MCP permission hooks return allow
  immediately, so the card does not hold the agent. Treat Mochi as a reader and
  a place to record your decision, not as an approval gate.
- **macOS 26 only.** There is no Windows, Linux, or web build. The page in `web/`
  is a bridge for the native app, not a standalone demo.
- **Build 4 does not include the new pet artwork.** The seven-state images and
  drag frames are in `web/pets/` on `main` after PR #1 merges; build 4 still shows
  the older placeholder kitten.
- **The look picker is a placeholder.** Looks A–F are one drawing with a color
  tint and a letter badge, not six different pets.
- **Growth is not implemented.** There are no tiers, seniority, journal, or
  achievements yet; only the feeding stats and seven hover lines exist.
- **No notarized DMG.** `Scripts/notarize.sh` needs a Developer ID certificate and
  a notary profile; without them it prints the checklist and exits.

- **Allow / Deny 不会放行 agent。** 你的选择会写入
  `~/.cursor/mochi-island-reply.json` 并显示在卡片上，但目前没有任何代码把它
  读回给 agent。Cursor 与 Codex 的 shell 和 MCP 权限 hook 会立即返回 allow，
  卡片不会拦住 agent。现阶段 Mochi 是"读状态 + 记录决定"，不是审批闸门。
- **仅支持 macOS 26。** 没有 Windows、Linux 或网页版；`web/` 是原生应用的桥接
  页，不能单独演示。
- **build 4 不含新形象。** 七态图与拖拽帧在 PR #1 合并后才进 `main`；build 4
  里仍是旧的占位小猫。
- **外观选择是占位方案。** A–F 是同一张图换色加字母角标，不是六个不同形象。
- **养成未实现。** 没有档位、资历、日记、成就；只有喂食数值和七句悬浮文案。
- **没有公证的 DMG。** `Scripts/notarize.sh` 需要 Developer ID 证书和 notary
  profile，缺省时只打印清单后退出。

## Build from source / 从源码构建

You need macOS 26, Xcode command line tools, and Swift 6.2 or later.
需要 macOS 26、Xcode 命令行工具、Swift 6.2 及以上。

```sh
Scripts/build-app.sh
open island/MochiIsland.app
```

`island/build.sh` is a wrapper for the same script. The build copies `web/` into
the app bundle, so edit assets in `web/`, never inside
`island/MochiIsland.app/Contents/Resources/`.

`island/build.sh` 是同一个脚本的包装。构建会把 `web/` 拷进 app bundle，
所以素材只改 `web/`，不要改 bundle 里的副本。

## Checks / 自检

```sh
swift run -c debug MochiCoreChecks
```

The suite is a small custom harness, not XCTest: 46 named checks and about 900
assertions covering pet rules, hook installation and retargeting, task parsing,
privacy scrubbing, reply writing, icon rules, launch fallbacks, geometry, and
sound cues. CI runs the same command on `macos-latest`, builds both products, and
rejects localhost servers, `fetch(`, `python3`, `osascript`, and `killall` in the
source.

自检是一个小型自研 harness，不是 XCTest：46 个命名检查、约 900 条断言，覆盖
宠物规则、hook 安装与重定向、任务解析、隐私清洗、回复写入、图标规则、启动
回退、几何与声音。CI 在 `macos-latest` 上跑同一命令、构建两个产物，并拒绝
源码里出现 localhost 服务、`fetch(`、`python3`、`osascript` 和 `killall`。

## Connect agents / 连接 agent

All connections are configured in Settings → Agents with one click. Each product
is an `AgentConnector` in `Sources/MochiCore/AgentConnector.swift` that owns its
hook file, its permission contract, and connect / disconnect / retarget.

所有连接都在设置 → Agents 里一键配置。每个产品是
`Sources/MochiCore/AgentConnector.swift` 里的一个 `AgentConnector`，自己管理
hook 文件、权限契约和 connect / disconnect / retarget。

| Agent | Hook file |
|-|-|
| Cursor | `~/.cursor/hooks.json` |
| Codex | `~/.codex/hooks.json` (also enables `features.hooks` and stores the trust hash) |
| Grok Bot | `~/.grok/hooks/mochi-island.json` |
| Claude | `~/.claude/settings.json` |
| Copilot | `~/.copilot/hooks/mochi-island.json` |
| OpenCode | `~/.config/opencode/plugins/mochi-island.js` |
| Gemini | `~/.gemini/settings.json` |

Every agent writes into the same local task file,
`~/.cursor/mochi-island-tasks.json`. On launch the app retargets installed hooks
to the current copy of the app.

所有 agent 都写进同一个本机任务文件 `~/.cursor/mochi-island-tasks.json`。
每次启动时，应用会把已安装的 hook 重新指向当前这份 app。

## Privacy / 隐私

- Everything runs locally. Task state never leaves this Mac.
- Prompt text and command bodies are not stored. The card keeps a clipped
  question blurb and a clipped answer blurb.
- The optional local-file notice is off by default and records names and times,
  not chat text.
- Nothing drives Cursor through Accessibility or AppleScript.

- 全部本地运行，任务状态不出这台 Mac。
- 不存提示词和命令正文；卡片只保留截断的问题摘要与回答摘要。
- 可选的本机文件扫描默认关闭，只记文件名和时间，不记聊天正文。
- 不通过辅助功能或 AppleScript 驱动 Cursor。

## Docs and layout / 文档与结构

- `docs/PRODUCT-SUMMARY.md` — what the product does, screen by screen / 逐屏说明
- `docs/ARCHITECTURE.md` — the hook → task file → app pipeline / hook 到任务文件再到 app 的链路
- `web/pets/README.md` — the pet artwork, state priority, drag frames, placement / 形象素材、状态优先级、拖拽帧、对位
- `AGENTS.md` — rules for working on this codebase / 开发规则

```
Sources/MochiCore       pet rules, save files, hook install, event interpretation
Sources/MochiIsland     kitten shell, glass bubble, settings, sound
Sources/MochiHookCLI    the program agent hooks run
Tests/MochiCoreChecks   the check suite
web/                    WebView bridge page, agent icons, pet and status art
island/                 Info.plist and the built .app
Scripts/                build, notarize, diagnostics, mock task injection
```
