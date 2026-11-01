import Foundation

/// One product agent that Mochi can Connect from Settings in a single click.
/// Each connector owns that product's hook layout, config paths, and permission model.
/// Users never edit hook files by hand — Settings → Connect runs `connect`.
public protocol AgentConnector {
    /// Stable id used in prefs, tasks `source`, and Settings (`cursor`, `codex`, …).
    var id: String { get }
    var displayName: String { get }
    /// When true, Settings shows the privacy line before the first Connect.
    var needsPrivacyAck: Bool { get }

    func status(binaryURL: URL) -> HookInstallResult
    func connect(binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult
    func disconnect(binaryURL: URL) -> HookInstallResult
    /// Point installed hooks at this app's MochiHookCLI after a rebuild or move.
    @discardableResult
    func retarget(binaryURL: URL) -> Bool
}

/// Registry of first-class connectors. Add a new agent by appending a connector here;
/// Settings keeps the same Connect / Disconnect UX.
public enum AgentConnectorRegistry {
    public static let primary: [AgentConnector] = [
        CursorConnector(),
        CodexConnector()
    ]

    public static func connector(for id: String) -> AgentConnector? {
        let key = AgentLaunch.normalize(id)
        return primary.first { AgentLaunch.normalize($0.id) == key }
    }

    public static func retargetAll(binaryURL: URL) {
        for connector in primary {
            _ = connector.retarget(binaryURL: binaryURL)
        }
    }
}

/// Cursor: flat `~/.cursor/hooks.json` + beforeShellExecution / beforeMCPExecution allow-deny.
public struct CursorConnector: AgentConnector {
    public let id = "cursor"
    public let displayName = "Cursor"
    public let needsPrivacyAck = true

    public init() {}

    private var hooksURL: URL {
        AppConfig.cursorDirectory.appendingPathComponent("hooks.json")
    }

    private var tasksURL: URL {
        AppConfig.cursorDirectory.appendingPathComponent("mochi-island-tasks.json")
    }

    public func status(binaryURL: URL) -> HookInstallResult {
        HookInstaller.status(hooksURL: hooksURL, binaryURL: binaryURL, tasksURL: tasksURL)
    }

    public func connect(binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        HookInstaller.install(
            hooksURL: hooksURL,
            binaryURL: binaryURL,
            tasksURL: tasksURL,
            privacyAccepted: privacyAccepted
        )
    }

    public func disconnect(binaryURL: URL) -> HookInstallResult {
        HookInstaller.remove(hooksURL: hooksURL, binaryURL: binaryURL, tasksURL: tasksURL)
    }

    public func retarget(binaryURL: URL) -> Bool {
        HookInstaller.retargetCursor(hooksURL: hooksURL, binaryURL: binaryURL)
    }
}

/// Codex: nested `~/.codex/hooks.json` + `features.hooks` in config.toml.
/// Permission model is Codex `PermissionRequest` (stdout hookSpecificOutput).
public struct CodexConnector: AgentConnector {
    public let id = "codex"
    public let displayName = "Codex"
    public let needsPrivacyAck = false

    public init() {}

    private var configURL: URL {
        AppConfig.homeDirectory.appendingPathComponent(".codex/config.toml")
    }

    private var hooksURL: URL {
        AppConfig.homeDirectory.appendingPathComponent(".codex/hooks.json")
    }

    public func status(binaryURL: URL) -> HookInstallResult {
        HookInstaller.codexStatus(configURL: configURL, hooksURL: hooksURL, binaryURL: binaryURL)
    }

    public func connect(binaryURL: URL, privacyAccepted: Bool) -> HookInstallResult {
        HookInstaller.installCodex(
            configURL: configURL,
            hooksURL: hooksURL,
            binaryURL: binaryURL,
            privacyAccepted: privacyAccepted
        )
    }

    public func disconnect(binaryURL: URL) -> HookInstallResult {
        HookInstaller.removeNested(fileURL: hooksURL, binaryURL: binaryURL)
    }

    public func retarget(binaryURL: URL) -> Bool {
        HookInstaller.retargetNested(
            fileURL: hooksURL,
            binaryURL: binaryURL,
            source: "codex"
        )
    }
}
