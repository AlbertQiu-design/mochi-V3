# Mochi Island 产品功能总结

> 面向团队说明：当前能力、连接方式、交互规则，以及后续扩展方向。  
> 可直接转发；若需要演示视频，可按文末「建议演示脚本」录屏。

**产品一句话：** Mochi 是一只浮在 macOS 桌面上的小猫桌宠。它通过本机 Hooks 连接 Cursor、Codex 等 AI 工具，在胶囊侧栏里静默显示对话状态，需要时再弹出卡片做 Allow / Deny 或查看摘要。

---

## 1. 产品定位

| 维度 | 说明 |
|------|------|
| 形态 | 原生 macOS App（Swift + 薄 Web 桥），无本地 localhost 服务 |
| 视觉 | 无框小猫 → 点击弹出 Liquid Glass 胶囊 → 旁侧玻璃气泡卡片 |
| 价值 | 不打断工作流：任务在侧栏可见；权限在胶囊旁一键处理；不抄写完整提示词/命令 |
| 分发 | 内测可用本机构建的 `.app`；正式给用户需 Developer ID 签名 + 公证 DMG |

---

## 2. 当前支持的功能一览

### 2.1 桌面小猫与胶囊

- **拖动放置**：拖超过约 3pt 保存屏幕相对位置；收起时只有小猫，无常亮底盘。
- **点击展开胶囊**：弹簧动画；平时向下生长；靠近屏幕底部且空间不够时**向上生长**，小猫留在胶囊最下。
- **胶囊内容顺序**：小猫图标 → 各 Agent 对话图标 → 加号 → 齿轮。
- **选中态**：移动中为液态玻璃圆底，停下后为稍深平色。
- **尺寸**：设置里 Kitten 滑杆 0.5×～2×；拖动时小猫/胶囊即时缩放，**设置面板本身不跟着跳**（下次打开设置再更新位置）。

### 2.2 对话图标与显隐规则（侧栏）

- **一个对话一颗图标**（例如多个 Cursor 对话会有多颗 Cursor 图标）。
- **进行中 / 等待中**：图标**必须**留在胶囊里，**默认不自动弹窗**（静默显示，红点可提示等待）。
- **用户点击图标**：才展开详情卡片（含状态、时间、摘要等）。
- **已完成**：未点开过的可提示；用户点开看过、且**没有**待处理选项（如 Allow/Deny）后，图标从胶囊移除。
- **有待办选项**的项：即使已读，也先留在胶囊，直到用户处理完。

### 2.3 详情 / 权限卡片

卡片上可展示（与产品说明一致）：

- 动作类型：Shell / MCP / Permission 等  
- 来源（Cursor、Codex…）  
- 相对时间（如 `1m`）  
- 状态（Running / Waiting / Done…）  
- 提交内容**缩略**（约 40 字；无摘要时权限卡有兜底文案，如 `Shell needs Allow`）  
- Allow / Deny 等选项按钮  

交互约定：

- **Allow / Deny**：只负责答复权限，写入本机 reply 文件，卡片关闭。  
- **点击卡片上非选项区域**（标题、状态、摘要、空白等）：跳回对应软件（如唤起 Cursor / Codex），作为「回到原对话」入口；选项按钮功能不变。  
- 不自动 Allow Shell/MCP；超时默认 Deny（仍 fail-open，不卡死 Agent）。

### 2.4 设置面板

- 浅色圆角玻璃；**红黄绿三键在面板内部**（不挂在外面）。
- 标签：General / Kitten / Sound / Agents（整颗可点）。
- **General**：复位位置、登录打开、Version / Build。
- **Kitten**：一只小猫 + 尺寸。
- **Sound**：Boop / Chirp / Mew / Pop / Twinkle；系统静音时不响。
- **Agents**：各软件连接状态 + **Connect / Disconnect 一键配置**（用户不手改 hook 文件）；Cursor 隐私确认；可选本机文件名/时间扫描（默认关，不记聊天正文）；加号菜单里各图标开关。

### 2.5 小猫养成（后台规则）

- 无喂食按钮；完成的任务喂一次（按任务 id 去重，最多记 500）。
- 可玩耍；饿到阈值或没精力会睡觉；睡时不衰减；下次完成的任务叫醒。
- **不会死**；无「换新宠物」压力文案。

---

## 3. 通用连接层（多软件架构）

**需求：** 用户会连接多个产品（现有 Cursor、Codex，后续更多）。所有配置都在 Mochi **设置 → Agents** 里点 Connect 完成，**不要求用户手改任何配置文件**。

**实现方式：**

- 每个软件一个 **Connector（连接器）**，封装：
  - 该软件的 hook / 配置路径
  - 权限回执格式（如 Cursor 的 `permission`，Codex 的 `PermissionRequest` stdout）
  - `connect` / `disconnect` / `status` / `retarget`
- 设置页一排 Connect；点哪个执行哪个连接器（把 hooks 指到**当前包内**的 `MochiHookCLI`）。
- 以后接新软件 = 新增一个连接器 + 设置行，UX 不变。
- 启动时自动 **retarget**，避免多份 `.app` 抢旧路径。

**当前一等连接器：**

| 软件 | 连接器 | 配置落点（由 Connect 自动写） |
|------|--------|------------------------------|
| Cursor | `CursorConnector` | `~/.cursor/hooks.json` |
| Codex | `CodexConnector` | `~/.codex/hooks.json` + `config.toml` 中 hooks 开关 |

其它（Claude、Copilot、OpenCode、Grok、Gemini 等）可在设置里连接或已有安装路径，后续可迁入同一 Connector 协议。

---

## 4. 技术链路（给研发 / 联调）

```
用户在 Cursor/Codex 里提交对话或触发工具
        ↓
该软件调用本机 Hook → MochiHookCLI（stdin JSON）
        ↓
写入 ~/.cursor/mochi-island-tasks.json（统一任务总线）
        ↓
Mochi 文件监听 → 更新胶囊图标 / 卡片
        ↓
用户 Allow/Deny → ~/.cursor/mochi-island-reply.json
        ↓
Hook CLI 读到结果 → stdout 回给 Cursor/Codex
```

要点：

- **不是** MCP 服务器，也**不是** localhost / socket 总线。  
- Cursor 的 Shell/MCP 审批走 `beforeShellExecution` / `beforeMCPExecution`；Mochi 只做本机 hook 与 UI。  
- Codex 审批走 `PermissionRequest` 等 lifecycle hooks。  
- 完整提示词与命令**不入库**；可保留短 summary 供卡片展示。

---

## 5. 隐私与安全（对外口径）

- Connect 前有隐私说明（Cursor）。  
- 聊天与命令正文默认不上传、不落完整副本。  
- 权限始终由用户在本机点 Allow/Deny（或超时拒绝）。  
- 不使用 AppleScript / 辅助功能去操控 Cursor。

---

## 6. 已支持 / 进行中 / 未做（状态表）

| 能力 | 状态 |
|------|------|
| 小猫 + 上下方向胶囊 + Liquid Glass | 已支持 |
| 多对话多图标、进行中静默、点击展开 | 已支持（规则已定） |
| 权限卡 + 摘要行 + Allow/Deny | 已支持 |
| 设置红绿灯在玻璃内、拖尺寸不卡设置 | 已支持 |
| Cursor / Codex 一键 Connect（连接器架构） | 已支持 / 落地中 |
| 点卡片非选项区回原对话 | 需求已定，实现对接唤起对应 App |
| 正式签名公证 DMG | 未做（缺 Developer ID） |
| Goose / Cline / Windsurf / Aider 等更长名单 | 未接 |

---

## 7. 建议回复组里的话术（可直接贴）

针对「以及当前可以支持的所有功能，需要你给大家有一个视频或者文档阐述一下」：

> 文档已整理好（本文件）。Mochi 当前是桌面小猫 + 胶囊侧栏：在设置里一键 Connect Cursor/Codex（用户不用手改 hook）；对话进行中只在侧栏亮图标、不打扰；点图标看摘要/状态/时间；需要权限时出 Allow/Deny；点卡片非按钮区域可回到对应软件。养成不喂食、不死亡。后续接更多软件走统一连接器，设置体验不变。若需要，我可以再按下面脚本补一段 1～2 分钟演示录屏。

---

## 8. 建议演示脚本（录视频用，约 90～120 秒）

1. 打开 Mochi，拖小猫到桌面合适位置。  
2. 点小猫 → 胶囊弹出；拖到屏幕底部再点 → 展示向上展开。  
3. 打开设置：展示红绿灯在玻璃内；Agents 里 Connect Cursor（及 Codex）。  
4. 在 Cursor 提交一句对话 → 胶囊出现 Cursor 图标（不自动弹大卡）。  
5. 点图标 → 卡片显示状态/时间/缩略；触发 Shell → Allow/Deny。  
6. 点卡片非按钮区域 → 唤起 Cursor。  
7. 完成后点开看过 → 图标从胶囊消失。  
8. （可选）拖 Kitten 尺寸：小猫变、设置面板不跟着抖。

---

## 9. 文档维护

- 工程约束与消息协议：仓库根目录 `AGENTS.md`  
- 架构与连接层：`docs/ARCHITECTURE.md`  
- 本产品说明（对外/对组）：`docs/PRODUCT-SUMMARY.md`（本文）

如有产品口径变更，请同步改本文第 2～6 节后再转发团队。
