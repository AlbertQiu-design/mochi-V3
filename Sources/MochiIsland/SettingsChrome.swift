import AppKit
import MochiCore
import ServiceManagement
import SwiftUI

enum SettingsTab: Int, Hashable, CaseIterable {
    case general
    case pet
    case sound
    case agents

    var title: String {
        switch self {
        case .general: return "General"
        case .pet: return "Pet"
        case .sound: return "Sound"
        case .agents: return "Agents"
        }
    }
}

/// Icons the plus picker is allowed to show. Settings writes it; the bubble reads it.
final class PickerMenu: ObservableObject {
    static let shared = PickerMenu()
    @Published private(set) var sources: [String] = IslandPrefs.pickerOrder

    func apply(_ prefs: IslandPrefs) {
        let next = prefs.pickerSources
        if next != sources {
            sources = next
        }
    }
}

final class SettingsPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        frameRect
    }
}

enum SettingsChromeMetrics {
    /// Room for the traffic lights inside the rounded glass, above the tabs.
    static let titleRow: CGFloat = 46
    static let cornerRadius: CGFloat = 28
}

@MainActor
final class SettingsChrome: NSObject, NSWindowDelegate {
    static let width: CGFloat = AppConfig.settingsWidth
    static let height: CGFloat = 540
    static let gap: CGFloat = 8
    static let titleRow: CGFloat = SettingsChromeMetrics.titleRow
    static let sounds = IslandPrefs.soundIDs

    let panel: SettingsPanel
    private let model: SettingsModel
    private let hosting: NSHostingView<SettingsRoot>
    private let buttonHost: SettingsButtonHost
    private let glass: NSGlassEffectView
    private weak var host: NSWindow?

    var onPrefs: (([String: Any]) -> Void)? {
        didSet { model.onPrefs = onPrefs }
    }
    var onLink: ((String, String, Bool) -> [String: Any])? {
        didSet { model.onLink = onLink }
    }
    var onScan: ((Bool) -> Void)? {
        didSet { model.onScan = onScan }
    }
    var onReset: (() -> Void)? {
        didSet { model.onReset = onReset }
    }

    var isVisible: Bool { panel.isVisible && panel.alphaValue > 0 }

    override init() {
        let model = SettingsModel()
        // Borderless so the frame is the glass. Titled chrome drew traffic lights
        // outside the rounded SwiftUI plate; custom buttons sit inside instead.
        let panel = SettingsPanel(
            contentRect: NSRect(x: 0, y: 0, width: Self.width, height: Self.height),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.isMovable = false
        panel.isMovableByWindowBackground = false
        panel.ignoresMouseEvents = false
        panel.becomesKeyOnlyIfNeeded = false
        panel.acceptsMouseMovedEvents = true
        panel.isFloatingPanel = true
        panel.isRestorable = false
        panel.isReleasedWhenClosed = false
        panel.animationBehavior = .none
        panel.title = "Mochi Settings"
        panel.worksWhenModal = true

        let glass = NSGlassEffectView(frame: NSRect(x: 0, y: 0, width: Self.width, height: Self.height))
        glass.style = .regular
        glass.tintColor = nil
        glass.cornerRadius = SettingsChromeMetrics.cornerRadius
        glass.autoresizingMask = [.width, .height]
        let interactive = NSSelectorFromString("setEffectIsInteractive:")
        if glass.responds(to: interactive) {
            glass.setValue(true, forKey: "effectIsInteractive")
        }

        let hosting = SettingsHostingView(rootView: SettingsRoot(model: model))
        hosting.sizingOptions = []
        hosting.wantsLayer = true
        hosting.layer?.backgroundColor = NSColor.clear.cgColor
        let buttons = SettingsButtonHost(frame: NSRect(x: 0, y: 0, width: Self.width, height: Self.height))
        hosting.frame = buttons.bounds
        hosting.autoresizingMask = [NSView.AutoresizingMask.width, NSView.AutoresizingMask.height]
        buttons.addSubview(hosting)
        buttons.autoresizingMask = [.width, .height]
        glass.contentView = buttons
        panel.contentView = glass

        self.panel = panel
        self.model = model
        self.hosting = hosting
        self.buttonHost = buttons
        self.glass = glass
        super.init()
        panel.delegate = self
        revealWindowButtons()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        hide()
        return false
    }

    func windowDidBecomeKey(_ notification: Notification) {
        revealWindowButtons()
    }

    @objc func dismissSettings(_ sender: Any?) {
        hide()
    }

    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize {
        NSSize(width: Self.width, height: Self.height)
    }

    func windowWillUseStandardFrame(_ window: NSWindow, defaultFrame newFrame: NSRect) -> NSRect {
        var frame = window.frame
        frame.size = NSSize(width: Self.width, height: Self.height)
        return frame
    }

    func attach(to host: NSWindow) {
        if let current = self.host {
            current.removeChildWindow(panel)
        }
        self.host = host
        // Independent of the click-through host so the fullscreen WebView
        // cannot steal the tab bar.
        if host.childWindows?.contains(panel) == true {
            host.removeChildWindow(panel)
        }
        panel.level = NSWindow.Level(rawValue: host.level.rawValue + 2)
        panel.collectionBehavior = host.collectionBehavior
        hide()
    }

    func load(prefs: IslandPrefs, link: [String: Any]) {
        model.apply(prefs: prefs, link: link)
    }

    func select(_ tab: SettingsTab) {
        model.tab = tab
    }

    func toggle(capsule: NSRect, dock: String, host: NSWindow) {
        if isVisible {
            hide()
            return
        }
        place(capsule: capsule, dock: dock, host: host)
        panel.ignoresMouseEvents = false
        panel.alphaValue = 1
        panel.orderFrontRegardless()
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(hosting)
        revealWindowButtons()
    }

    func hide() {
        panel.alphaValue = 0
        panel.orderOut(nil)
    }

    /// Places the panel beside the capsule. Used for drag-reset and screen changes only.
    /// Pet-scale must not call this — the open window stays put until Settings opens again.
    func reposition(capsule: NSRect, dock: String, host: NSWindow) {
        guard isVisible else { return }
        place(capsule: capsule, dock: dock, host: host)
    }

    private func revealWindowButtons() {
        buttonHost.install(window: panel, closeTarget: self, closeAction: #selector(dismissSettings(_:)))
    }

    private func place(capsule: NSRect, dock: String, host: NSWindow) {
        let width = Self.width
        let height = Self.height
        var x = dock == "right" ? capsule.minX - Self.gap - width : capsule.maxX + Self.gap
        var y = capsule.minY + (capsule.height - height) / 2
        let canvas = host.frame
        x = min(max(x, canvas.minX + Self.gap), canvas.maxX - Self.gap - width)
        y = min(max(y, canvas.minY + Self.gap), canvas.maxY - Self.gap - height)
        panel.setFrame(NSRect(x: x, y: y, width: width, height: height), display: true)
        let bounds = NSRect(origin: .zero, size: NSSize(width: width, height: height))
        glass.frame = bounds
        glass.cornerRadius = SettingsChromeMetrics.cornerRadius
        buttonHost.frame = bounds
        hosting.frame = bounds
        buttonHost.placeButtons()
    }
}

@MainActor
final class SettingsModel: ObservableObject {
    @Published var tab: SettingsTab = .general
    @Published var selectionMoving = false
    @Published var sound = "boop"
    @Published var cursorConnected = false
    @Published var codexConnected = false
    @Published var claudeConnected = false
    @Published var copilotConnected = false
    @Published var opencodeConnected = false
    @Published var grokConnected = false
    @Published var geminiConnected = false
    @Published var needsPrivacy = false
    @Published var showPrivacyConfirm = false
    @Published var scanTranscripts = false
    @Published var cursorStatus = "Not connected"
    @Published var codexStatus = "Not connected"
    @Published var claudeStatus = "Not connected"
    @Published var copilotStatus = "Not connected"
    @Published var opencodeStatus = "Not connected"
    @Published var grokStatus = "Not connected"
    @Published var geminiStatus = "Not connected"
    @Published var cursorButton = "Connect Cursor"
    @Published var codexButton = "Connect Codex"
    @Published var claudeButton = "Connect Claude"
    @Published var copilotButton = "Connect Copilot"
    @Published var opencodeButton = "Connect OpenCode"
    @Published var grokButton = "Connect Grok"
    @Published var geminiButton = "Connect Gemini"
    @Published var petScale = 1.0
    @Published var look = "a"
    @Published var launchAtLogin = SMAppService.mainApp.status == .enabled
    @Published var pickerCursor = true
    @Published var pickerCodex = true
    @Published var pickerClaude = true
    @Published var pickerGemini = true
    @Published var pickerGrok = true
    @Published var pickerGPT = true

    var onPrefs: (([String: Any]) -> Void)?
    var onLink: ((String, String, Bool) -> [String: Any])?
    var onScan: ((Bool) -> Void)?
    var onReset: (() -> Void)?
    private var selectionTicket = 0

    func apply(prefs: IslandPrefs, link: [String: Any]) {
        sound = IslandPrefs.soundIDs.contains(prefs.sound) ? prefs.sound : "boop"
        petScale = prefs.petScale
        look = IslandPrefs.normalizeLook(prefs.look)
        launchAtLogin = SMAppService.mainApp.status == .enabled
        scanTranscripts = prefs.scanTranscripts
        pickerCursor = prefs.pickerCursor
        pickerCodex = prefs.pickerCodex
        pickerClaude = prefs.pickerClaude
        pickerGemini = prefs.pickerGemini
        pickerGrok = prefs.pickerGrok
        pickerGPT = prefs.pickerGPT
        PickerMenu.shared.apply(prefs)
        if let scan = link["scanTranscripts"] as? Bool {
            scanTranscripts = scan
        }
        applyAgents(link)
    }

    func applyAgents(_ link: [String: Any]) {
        let agents = link["agents"] as? [String: [String: Any]] ?? [:]
        cursorConnected = connectedFlag(agents["cursor"]) ?? (link["connected"] as? Bool ?? false)
        codexConnected = connectedFlag(agents["codex"]) ?? false
        claudeConnected = connectedFlag(agents["claude"]) ?? false
        copilotConnected = connectedFlag(agents["copilot"]) ?? false
        opencodeConnected = connectedFlag(agents["opencode"]) ?? false
        grokConnected = connectedFlag(agents["grok"]) ?? false
        geminiConnected = connectedFlag(agents["gemini"]) ?? false
        needsPrivacy = link["needsPrivacy"] as? Bool ?? false
        cursorStatus = cursorConnected ? "Connected" : "Not connected"
        codexStatus = codexConnected ? "Connected" : "Not connected"
        claudeStatus = claudeConnected ? "Connected" : "Not connected"
        copilotStatus = copilotConnected ? "Connected" : "Not connected"
        opencodeStatus = opencodeConnected ? "Connected" : "Not connected"
        grokStatus = grokConnected ? "Connected" : "Not connected"
        geminiStatus = geminiConnected ? "Connected" : "Not connected"
        cursorButton = needsPrivacy ? "Continue" : (cursorConnected ? "Disconnect" : "Connect Cursor")
        codexButton = codexConnected ? "Disconnect" : "Connect Codex"
        claudeButton = claudeConnected ? "Disconnect" : "Connect Claude"
        copilotButton = copilotConnected ? "Disconnect" : "Connect Copilot"
        opencodeButton = opencodeConnected ? "Disconnect" : "Connect OpenCode"
        grokButton = grokConnected ? "Disconnect" : "Connect Grok"
        geminiButton = geminiConnected ? "Disconnect" : "Connect Gemini"
    }

    private func connectedFlag(_ row: [String: Any]?) -> Bool? {
        guard let row else { return nil }
        if let value = row["connected"] as? Bool { return value }
        if let value = row["connected"] as? NSNumber { return value.boolValue }
        return nil
    }

    func setPetScale(_ value: Double) {
        petScale = min(2, max(0.5, value))
        persist()
    }

    func setLook(_ value: String) {
        let next = IslandPrefs.normalizeLook(value)
        guard look != next else { return }
        look = next
        persist()
    }

    func persist() {
        let snapshot = IslandPrefs(
            sound: sound,
            look: look,
            petScale: petScale,
            pickerCursor: pickerCursor,
            pickerCodex: pickerCodex,
            pickerClaude: pickerClaude,
            pickerGemini: pickerGemini,
            pickerGrok: pickerGrok,
            pickerGPT: pickerGPT
        )
        onPrefs?([
            "sound": sound,
            "look": look,
            "pet_scale": petScale,
            "picker_cursor": pickerCursor,
            "picker_codex": pickerCodex,
            "picker_claude": pickerClaude,
            "picker_gemini": pickerGemini,
            "picker_grok": pickerGrok,
            "picker_gpt": pickerGPT
        ])
        PickerMenu.shared.apply(snapshot)
    }

    @discardableResult
    func beginSelectionMove() -> Int {
        selectionMoving = true
        selectionTicket += 1
        let ticket = selectionTicket
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 700_000_000)
            self.endSelectionMove(ticket)
        }
        return ticket
    }

    func endSelectionMove(_ ticket: Int) {
        guard selectionTicket == ticket else { return }
        selectionMoving = false
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            launchAtLogin = SMAppService.mainApp.status == .enabled
        } catch {
            launchAtLogin = SMAppService.mainApp.status == .enabled
        }
    }

    func connect(_ agent: String) {
        let connected: Bool
        switch agent {
        case "codex": connected = codexConnected
        case "claude": connected = claudeConnected
        case "copilot": connected = copilotConnected
        case "opencode": connected = opencodeConnected
        case "grok", "grok-bot": connected = grokConnected
        case "gemini": connected = geminiConnected
        default: connected = cursorConnected
        }
        let action = connected ? "disconnect" : "connect"
        // First Cursor connect needs an explicit sheet; later agents reuse that acceptance.
        if action == "connect" && (agent == "cursor" || agent.isEmpty) {
            let probe = onLink?(agent, "connect", false) ?? [:]
            if probe["needsPrivacy"] as? Bool == true {
                if let scan = probe["scanTranscripts"] as? Bool {
                    scanTranscripts = scan
                }
                applyAgents(probe)
                showPrivacyConfirm = true
                return
            }
        }
        finishLink(agent: agent, action: action, acknowledged: true)
    }

    func confirmPrivacyConnect() {
        showPrivacyConfirm = false
        finishLink(agent: "cursor", action: "connect", acknowledged: true)
    }

    func cancelPrivacyConnect() {
        showPrivacyConfirm = false
    }

    private func finishLink(agent: String, action: String, acknowledged: Bool) {
        let result = onLink?(agent, action, acknowledged) ?? [:]
        if let scan = result["scanTranscripts"] as? Bool {
            scanTranscripts = scan
        }
        applyAgents(result)
    }
}

struct SettingsRoot: View {
    @ObservedObject var model: SettingsModel
    @Namespace private var tabNS

    private static let barFill = Color(red: 246 / 255, green: 246 / 255, blue: 247 / 255)
    private static let selectedFill = Color(red: 232 / 255, green: 232 / 255, blue: 234 / 255)
    private static let selectedInk = Color(red: 28 / 255, green: 28 / 255, blue: 30 / 255)
    private static let idleInk = Color(red: 110 / 255, green: 110 / 255, blue: 115 / 255)

    var body: some View {
        VStack(spacing: 8) {
            Color.clear
                .frame(height: SettingsChrome.titleRow)
                .accessibilityHidden(true)
                .allowsHitTesting(false)
            settingsTabBar
                .padding(.horizontal, 16)
            Group {
                switch model.tab {
                case .general: generalPane
                case .pet: petPane
                case .sound: soundPane
                case .agents: agentsPane
                }
            }
            .id(model.tab)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .padding(.bottom, 6)
        .frame(width: SettingsChrome.width, height: SettingsChrome.height, alignment: .top)
        .confirmationDialog(
            "Connect Cursor?",
            isPresented: $model.showPrivacyConfirm,
            titleVisibility: .visible
        ) {
            Button("Connect") { model.confirmPrivacyConnect() }
            Button("Cancel", role: .cancel) { model.cancelPrivacyConnect() }
        } message: {
            Text("Mochi will write its hook into ~/.cursor/hooks.json on this Mac only. Chats are not uploaded. Shell and MCP still need Allow or Deny beside the kitten.")
        }
        // Glass is the AppKit NSGlassEffectView panel container; keep layout only here.
        .contentShape(RoundedRectangle(cornerRadius: SettingsChromeMetrics.cornerRadius, style: .continuous))
    }

    private var settingsTabBar: some View {
        HStack(spacing: 0) {
            ForEach(SettingsTab.allCases, id: \.self) { item in
                tabButton(item)
            }
        }
        .padding(2)
        .frame(height: AppConfig.settingsBarHeight)
        .background {
            RoundedRectangle(cornerRadius: AppConfig.settingsBarRadius, style: .continuous)
                .fill(Self.barFill)
        }
    }

    private func tabButton(_ item: SettingsTab) -> some View {
        let selected = model.tab == item
        return ZStack {
            if selected { selectionPlate }
            Text(item.title)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(selected ? Self.selectedInk : Self.idleInk)
                .allowsHitTesting(false)
        }
        .frame(maxWidth: .infinity, minHeight: 28, maxHeight: .infinity)
        .frame(height: max(28, AppConfig.settingsBarHeight - 4))
        .contentShape(Rectangle())
        .onTapGesture { choose(item) }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(item.title)
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : .isButton)
    }

    @ViewBuilder
    private var selectionPlate: some View {
        let shape = RoundedRectangle(cornerRadius: AppConfig.settingsSelectRadius, style: .continuous)
        Group {
            if model.selectionMoving {
                GlassEffectContainer {
                    shape
                        .fill(.clear)
                        .glassEffect(.regular, in: shape)
                }
            } else {
                shape.fill(Self.selectedFill)
            }
        }
        .matchedGeometryEffect(id: "settings-tab", in: tabNS)
        .padding(1)
        .allowsHitTesting(false)
    }

    private func choose(_ item: SettingsTab) {
        guard model.tab != item else { return }
        let ticket = model.beginSelectionMove()
        withAnimation(.spring(response: 0.32, dampingFraction: 0.78)) {
            model.tab = item
        } completion: {
            model.endSelectionMove(ticket)
        }
    }

    private var generalPane: some View {
        Form {
            Section {
                Button("Reset kitten position") { model.onReset?() }
                Toggle("Open at login", isOn: $model.launchAtLogin)
                    .onChange(of: model.launchAtLogin) { _, enabled in
                        model.setLaunchAtLogin(enabled)
                    }
            }
            Section {
                LabeledContent("Version", value: Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0")
                LabeledContent("Build", value: Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "2")
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
    }

    private var petPane: some View {
        Form {
            // Size card: live preview + scale only.
            Section("Size") {
                ZStack {
                    // scaleEffect keeps Form/glass layout fixed while the preview grows.
                    // Changing .frame with petScale was reflowing the open settings plate.
                    FullKitten(look: model.look)
                        .frame(width: 56, height: 56)
                        .scaleEffect(model.petScale)
                }
                .frame(maxWidth: .infinity)
                .frame(height: 112)
                Slider(value: Binding(
                    get: { model.petScale },
                    set: { model.setPetScale($0) }
                ), in: 0.5...2) {
                    Text("Size")
                } minimumValueLabel: {
                    Text("S")
                } maximumValueLabel: {
                    Text("L")
                }
                LabeledContent("Size", value: String(format: "%.2f×", model.petScale))
            }
            // Separate card under Size: pick which desktop pet (A–F placeholders for now).
            Section("Pet") {
                HStack(spacing: 8) {
                    ForEach(IslandPrefs.petLooks, id: \.self) { id in
                        petLetterChip(id)
                    }
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
                Text("Letters stand in for more pets. The Size preview and desktop pet change together.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
    }

    private func petLetterChip(_ id: String) -> some View {
        let selected = model.look == id
        let letter = IslandPrefs.petLetter(for: id)
        return Button {
            model.setLook(id)
        } label: {
            Text(letter)
                .font(.system(size: 17, weight: .semibold, design: .rounded))
                .foregroundStyle(selected ? Color.primary : Color.primary.opacity(0.75))
                .frame(maxWidth: .infinity)
                .frame(height: 44)
                .background(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .fill(selected ? PetLookStyle.badge(for: id).opacity(0.22) : Color.primary.opacity(0.06))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .strokeBorder(selected ? PetLookStyle.badge(for: id) : Color.primary.opacity(0.12), lineWidth: selected ? 2 : 1)
                )
                .contentShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Pet \(letter)")
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : .isButton)
    }

    private var soundPane: some View {
        Form {
            Section("Click sound") {
                Picker("Sound", selection: Binding(
                    get: { model.sound },
                    set: { value in
                        model.sound = value
                        model.persist()
                        SoundPlayer.shared.playPreview(value)
                    }
                )) {
                    ForEach(SettingsChrome.sounds, id: \.self) { id in
                        Text(id.capitalized).tag(id)
                    }
                }
                .pickerStyle(.segmented)
                Text("Plays when you click the kitten, pick an agent, or hear an alert. A muted Mac stays quiet.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
    }

    private var agentsPane: some View {
        Form {
            Section("Cursor") {
                LabeledContent("Status", value: model.cursorStatus)
                Text("Connect one-click installs Mochi's hook for this app. You do not edit hook files. Chats are not uploaded. Shell and MCP still need Allow or Deny beside the kitten.")
                    .foregroundStyle(.secondary)
                Button(model.cursorButton) { model.connect("cursor") }
                Toggle("Notice recent local agent files", isOn: $model.scanTranscripts)
                    .onChange(of: model.scanTranscripts) { _, enabled in
                        model.onScan?(enabled)
                    }
                Text("Off by default. It records names and times on this Mac, not chat text.")
                    .foregroundStyle(.secondary)
            }
            Section("Codex") {
                LabeledContent("Status", value: model.codexStatus)
                Text("Connect one-click enables Codex hooks and points them at this app. A submitted chat adds the Codex icon. Prompts stay in Codex.")
                    .foregroundStyle(.secondary)
                Button(model.codexButton) { model.connect("codex") }
            }
            Section("Claude") {
                LabeledContent("Status", value: model.claudeStatus)
                Text("Connect installs Mochi's command hook in ~/.claude/settings.json.")
                    .foregroundStyle(.secondary)
                Button(model.claudeButton) { model.connect("claude") }
            }
            Section("Copilot") {
                LabeledContent("Status", value: model.copilotStatus)
                Text("Connect writes ~/.copilot/hooks/mochi-island.json. Copilot keeps its own permission prompt.")
                    .foregroundStyle(.secondary)
                Button(model.copilotButton) { model.connect("copilot") }
            }
            Section("OpenCode") {
                LabeledContent("Status", value: model.opencodeStatus)
                Text("Connect writes the OpenCode plugin ~/.config/opencode/plugins/mochi-island.js.")
                    .foregroundStyle(.secondary)
                Button(model.opencodeButton) { model.connect("opencode") }
            }
            Section("Grok") {
                LabeledContent("Status", value: model.grokStatus)
                Text("Connect writes ~/.grok/hooks/mochi-island.json for the grok CLI.")
                    .foregroundStyle(.secondary)
                Button(model.grokButton) { model.connect("grok") }
            }
            Section("Gemini") {
                LabeledContent("Status", value: model.geminiStatus)
                Text("Connect writes Mochi's command hook in ~/.gemini/settings.json. Gemini keeps its own permission prompt.")
                    .foregroundStyle(.secondary)
                Button(model.geminiButton) { model.connect("gemini") }
            }
            Section("Plus picker") {
                Toggle("Cursor", isOn: $model.pickerCursor)
                    .onChange(of: model.pickerCursor) { _, _ in model.persist() }
                Toggle("Codex", isOn: $model.pickerCodex)
                    .onChange(of: model.pickerCodex) { _, _ in model.persist() }
                Toggle("Claude", isOn: $model.pickerClaude)
                    .onChange(of: model.pickerClaude) { _, _ in model.persist() }
                Toggle("Gemini", isOn: $model.pickerGemini)
                    .onChange(of: model.pickerGemini) { _, _ in model.persist() }
                Toggle("Grok", isOn: $model.pickerGrok)
                    .onChange(of: model.pickerGrok) { _, _ in model.persist() }
                Toggle("GPT", isOn: $model.pickerGPT)
                    .onChange(of: model.pickerGPT) { _, _ in model.persist() }
                Text("The plus picker shows icons only. GPT launches Codex.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
    }
}

#Preview("Settings") {
    SettingsRoot(model: SettingsModel())
        .frame(width: SettingsChrome.width, height: SettingsChrome.height)
}

/// Close, minimize, and zoom drawn inside the glass, clear of the rounded corner.
final class SettingsButtonHost: NSView {
    private var traffic: [NSButton] = []
    private let topInset: CGFloat = 16
    private let leading: CGFloat = 20

    override func layout() {
        super.layout()
        placeButtons()
    }

    func install(window: NSWindow, closeTarget: AnyObject, closeAction: Selector) {
        // Borderless panel has no system titlebar; mint standard buttons into the glass.
        let mask: NSWindow.StyleMask = [.titled, .closable, .miniaturizable, .resizable]
        if traffic.isEmpty {
            for kind in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] {
                guard let button = NSWindow.standardWindowButton(kind, for: mask) else { continue }
                button.isHidden = false
                button.isEnabled = true
                addSubview(button)
                traffic.append(button)
            }
        }
        if let close = traffic.first {
            close.target = closeTarget
            close.action = closeAction
        }
        if traffic.count > 1 {
            traffic[1].target = window
            traffic[1].action = #selector(NSWindow.miniaturize(_:))
        }
        if traffic.count > 2 {
            traffic[2].target = window
            traffic[2].action = #selector(NSWindow.zoom(_:))
        }
        placeButtons()
    }

    func placeButtons() {
        var x = leading
        for button in traffic {
            let fitted = button.fittingSize
            let width = max(fitted.width, 14)
            let height = max(fitted.height, 14)
            let y = bounds.height - topInset - height
            button.frame = NSRect(x: x, y: y, width: width, height: height)
            x += width + 6
        }
    }
}

/// Full-size host inside the glass. Clicks in the traffic-light strip pass
/// through so Close, miniaturize, and zoom stay live.
final class SettingsHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override var acceptsFirstResponder: Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        let stripHeight = SettingsChromeMetrics.titleRow
        let strip = NSRect(x: 0, y: bounds.height - stripHeight, width: 130, height: stripHeight)
        if strip.contains(local) {
            return nil
        }
        return super.hitTest(point)
    }
}
