import Foundation

/// The desktop app should follow the bundle on disk. A build replaces the executable
/// while the previous process is still the one on screen.
public enum BundleRefresh {
    /// Wait after the last write so copying and signing can finish before the relaunch.
    public static let settle: Double = 1.5

    /// True when the file on disk was written after this process started.
    public static func isNewer(processStarted: Double, executableModified: Double) -> Bool {
        executableModified > processStarted + 0.5
    }
}
