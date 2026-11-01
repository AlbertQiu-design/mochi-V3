# Mochi Island — 架构

## 目标产品

Mac 原生桌宠；养成 + 多 Agent 胶囊状态；可分发 DMG。

## 数据流

```
Agent hooks ──► MochiHookCLI ──► ~/.cursor/mochi-island-tasks.json
                                         │
                    TaskFileWatch 推送 ◄──┘（1s 轮询兜底）
                                         ▼
MochiIsland.app ── KittenShell / GlassBubble / SettingsChrome
  MochiCore: PetEngine / TaskReader / AgentConnector / HookInstaller
```

- **不要**再加 localhost Python / Electron / socket 总线。
- 每种 Agent 只写统一 schema 的 tasks.json；UI 按 `source` 画图标。

## 通用连接层（需求）

用户会连接多个软件。**配置全部在 Settings → Agents 一键 Connect 完成**，不要求手改任何文件。

- 每个软件一个 `AgentConnector`（hook 写法、配置路径、权限回执格式）
- Settings 一排 Connect；点哪个跑哪个连接器
- 新软件 = 新连接器进 `AgentConnectorRegistry`，UX 不变
- 已落地：`CursorConnector`、`CodexConnector`（`Sources/MochiCore/AgentConnector.swift`）
- 启动时 `retargetAll`，hooks 始终指向当前 `.app` 内 `MochiHookCLI`

## 现状

| 项 | 状态 |
|----|------|
| Swift 原生 `.app`（无 8765） | ✅ |
| Cursor / Codex Connect 连接器 | ✅ |
| 统一 tasks.json + 权限卡 | ✅ |
| 养成（睡非死；完成任务喂食） | ✅ |
| DMG / notarize 脚本 | ✅ |
| Developer ID + 公证 | ❌ 需本机证书 |
| 更多 Agent 连接器 | ⏳ Claude 等仍用 legacy install，可按同协议迁移 |

## 打通延迟（优先）

1. Hook 写盘后，App 用 `DispatchSource`/`FSEvents` 监视 `mochi-island-tasks.json`，立刻 `getTasks` 推给 WebView。
2. 轮询降为兜底（≥5s）或关掉。
3. 禁止再扫大段 transcript 当主路径（仅用户显式开关）。

## 上线缺口清单

1. Apple Developer ID Application 证书 + notarytool profile  
2. 降 `LSMinimumSystemVersion` / platform 到合理档（若要覆盖更多 Mac）  
3. 推远程仓 → 套用 `docs/VERSION.md`  
4. Codex / Cloud 同 schema adapter  
5. 公证 DMG 冒烟：未签名机器能打开
