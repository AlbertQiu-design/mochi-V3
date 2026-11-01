import Foundation

public enum AppConfig {
    public static let bundleID = "com.mochiisland.mac"
    public static let hookMarker = "mochi-island"
    public static let hookVersion = 1
    public static let fedTaskLimit = 500
    /// Unopened finished tasks stay listed for two weeks, including across a restart.
    public static let doneKeep: Double = 14 * 24 * 60 * 60
    public static let liveKeep: Double = 30 * 60

    public static var homeDirectory: URL {
        #if os(macOS)
        FileManager.default.homeDirectoryForCurrentUser
        #else
        URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true)
        #endif
    }

    public static var supportDirectory: URL {
        homeDirectory
            .appendingPathComponent("Library/Application Support", isDirectory: true)
            .appendingPathComponent(bundleID, isDirectory: true)
    }

    public static var cursorDirectory: URL {
        homeDirectory
            .appendingPathComponent(".cursor", isDirectory: true)
    }

    // SPEC tokens for the kitten shell. CSS blur and fill do not apply to native glass.
    public static let materialAPI = "glassEffect.regular"
    public static let bubbleRadius: CGFloat = 12
    public static let beakWidth: CGFloat = 10
    public static let beakHeight: CGFloat = 16
    public static let beakGap: CGFloat = 6
    public static let beakTipRadius: CGFloat = 2.8
    public static let bubbleMinWidth: CGFloat = 160
    public static let bubbleMaxWidth: CGFloat = 220
    public static let bubblePadX: CGFloat = 12
    public static let bubblePadY: CGFloat = 8
    public static let bubbleMinHeight: CGFloat = 44
    public static let kittenSize: CGFloat = 56
    public static let capsuleWidth: CGFloat = 56
    public static let capsuleRadius: CGFloat = 28
    public static let capsulePadY: CGFloat = 8
    public static let capsulePadX: CGFloat = 6
    public static let kittenMark: CGFloat = 40
    public static let dividerWidth: CGFloat = 32
    public static let dividerHeight: CGFloat = 1
    public static let dividerGap: CGFloat = 8
    public static let agentRow: CGFloat = 44
    public static let agentIcon: CGFloat = 28
    public static let rowGap: CGFloat = 4
    public static let controlRow: CGFloat = 44
    public static let selectedRadius: CGFloat = 14
    public static let unreadDot: CGFloat = 8
    public static let screenInset: CGFloat = 8
    public static let dragThreshold: CGFloat = 3
    public static let settingsBarHeight: CGFloat = 32
    public static let settingsBarRadius: CGFloat = 16
    public static let settingsSelectRadius: CGFloat = 14
    public static let settingsTabMinWidth: CGFloat = 64
    public static let settingsWidth: CGFloat = 420
    public static let glassTintRed: CGFloat = 28
    public static let glassTintGreen: CGFloat = 28
    public static let glassTintBlue: CGFloat = 30
    public static let symbolSize: CGFloat = 20
}
