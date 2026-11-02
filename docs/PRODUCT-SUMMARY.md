# Mochi Island

对应本机正在开发的版本：#**1.0（build 3）**，只跑 **macOS 26/27**。应用是原生 Swift，没有本地网页服务。

**github：[https://github.com/AlbertQiu-design/mochi-V3/tree/main](https://github.com/AlbertQiu-design/mochi-V3/tree/main)**

**简要介绍：** Mochi 是一只浮在 Mac 桌面上的小猫。它用本机 Hooks 连接 Cursor、Codex 等工具，在胶囊里看每个对话的状态，需要时在旁边处理 Allow / Deny。

内测用本机构建的 `island/MochiIsland.app`。发给外部用户还需要 Developer ID 签名和公证 DMG，这一步还没做。

---

## 产品描述

收起时只有一只无框小猫。拖动超过大约 3pt 会记住位置。点一下，向下弹出 Liquid Glass 胶囊；靠近屏幕底部、空间不够时改为向上长。

胶囊里从左到右是：小猫、正在进行或还没看过的对话图标、加号、齿轮。选中高亮会弹过去，停住后变成稍深的平色。尺寸可以在 0.5× 到 2× 之间调，小猫和胶囊一起变。外观暂时是 A–F 六个字母，共用一张图，只换色，右下角标着当前字母。

## 状态环 & icon

每个对话图标旁边有状态环：

| 状态 | 环 |
|---|---|
| 进行中 | 蓝色的点绕着图标转 |
| 等待选择，或失败 | 红色的环，右下角留缺口，缺口里是叉 |
| 完成、还没看过 | 绿色的环，右下角留缺口，缺口里是勾 |

一个对话一颗图标。进行中和还在等选择的会留在胶囊里，不自动弹出大卡片。完成后，用户点开看过、并且没有待选的 Allow / Deny，图标就离开。还在等选择的会留下。

旁边只有一个玻璃气泡，用来看任务、处理权限，或从加号里挑一个软件。加号菜单只显示设置里打开的图标，没有名字。卡片上有来源、相对时间、状态，以及大约 40 字的提交摘要。完整提示词和命令不保存。点 Allow 或 Deny 只写本机回复；点卡片其它地方会把对应软件带到前面，没装就打开网页。超时默认拒绝，不会把 Agent 卡死。

## 声音

系统静音时不响。点小猫、选图标时用一套点击音：Boop、Chirp、Mew、Pop、Twinkle。

任务还有三组提示音，每组三种，在设置里试听后选用：

| 时机 | 选项 | 默认 |
|---|---|---|
| 提交成功 | Glass、Ripple、Ping | Glass |
| 任务完成 | Chime、Bloom、Spark | Chime |
| 卡住或失败 | Drop、Thud、Hush | Drop |

同一次任务的同一种状态只响一次。点 Allow 不会再响提交音。

## 设置

独立玻璃面板，红绿灯在面板内部。四页：

| 页 | 能做的事 |
|---|---|
| General | 复位小猫位置、登录时打开、版本和 build |
| Pet | 实时预览、尺寸、A–F 外观 |
| Sound | 点击音，以及提交、完成、卡住三组提示音 |
| Agents | 每个软件一行 Connect / Disconnect、Cursor 隐私说明、可选的本机文件名和时间记录（默认关，不记聊天正文）、加号菜单开关 |

加号菜单可开关：Cursor、Codex、Claude、Gemini、Grok、GPT。

## 软件连接

配置都在设置里点 Connect，不用手改文件。真正按统一连接器做的是 **Cursor** 和 **Codex**。目前已测试完成cursor连接。启动时会把它们的 hook 指回当前这份应用。其余也能连接，仍是各自的安装方式：

- Claude → `~/.claude/settings.json`
- Copilot → `~/.copilot/hooks/mochi-island.json`
- OpenCode → `~/.config/opencode/plugins/mochi-island.js`
- Grok CLI → `~/.grok/hooks/mochi-island.json`
- Gemini → `~/.gemini/settings.json`

所有任务进同一份 `~/.cursor/mochi-island-tasks.json`。打开方式：Cursor、Claude、ChatGPT、Grok 优先打开本机 App，没有就打开网站。Codex 没有 App 时打开 Codex 网页。Copilot、OpenCode、Gemini 留在终端。

## 小猫养成

本机制待开发，目前仅完成文案显示。

没有喂食按钮，也不会死，也没有「换一只」的压力文案。完成一段对话喂一次，按任务 id 去重，最多记 500 次。失败的任务不喂。饥饿到 95 或精力到 0 就睡觉，睡觉时不再衰减；下一次完成的任务把它叫醒。可以玩耍。

鼠标停在小猫上，会按当前状态显示一句：

| 状态 | 文案 |
|---|---|
| 刚吃过 | 吃到啦。 |
| 心情好 | 心情正好。 |
| 想玩 | 想玩一会儿。 |
| 有点困 | 有点困了。 |
| 肚子空 | 肚子有点空。 |
| 不想被打扰 | 想自己待一会儿。 |
| 睡着 | 在睡觉。 |

## 隐私

Connect Cursor 前有一句本机说明。聊天和命令正文默认不上传，也不留完整副本。权限由用户在本机点 Allow / Deny，或超时拒绝。不用辅助功能去操作 Cursor。

## 待办

- Developer ID 签名和公证 DMG
- Goose、Cline、Windsurf、Aider等agent产品等连接
- A–F 各自独立的宠物图（现在是同一张图换色），需要更多的设计素材
