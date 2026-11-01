import CoreGraphics
import Foundation

/// Screen math for the kitten shell. Coordinates are y-up, origin at the bottom left.
public enum ShellGeometry {
    public static func clamp01(_ value: Double) -> Double {
        min(1, max(0, value))
    }

    public static func ratios(anchor: CGPoint, screen: CGRect) -> (x: Double, y: Double) {
        let width = Double(screen.width)
        let height = Double(screen.height)
        let x = width == 0 ? 0.5 : (Double(anchor.x) - Double(screen.minX)) / width
        let y = height == 0 ? 0.5 : (Double(anchor.y) - Double(screen.minY)) / height
        return (clamp01(x), clamp01(y))
    }

    public static func anchor(x: Double, y: Double, screen: CGRect) -> CGPoint {
        CGPoint(
            x: screen.minX + CGFloat(clamp01(x)) * screen.width,
            y: screen.minY + CGFloat(clamp01(y)) * screen.height
        )
    }

    /// 8pt in from the right edge, vertically centered, then clamped so the kitten stays visible.
    public static func defaultAnchor(screen: CGRect, visible: CGRect, scale: CGFloat = 1) -> CGPoint {
        let kitten = AppConfig.kittenSize * ShellScale.factor(scale)
        let raw = CGPoint(x: screen.maxX - AppConfig.screenInset - kitten / 2, y: screen.midY)
        return clampCenter(raw, size: kitten, visible: visible)
    }

    public static func leftAnchor(screen: CGRect, visible: CGRect, scale: CGFloat = 1) -> CGPoint {
        let kitten = AppConfig.kittenSize * ShellScale.factor(scale)
        let raw = CGPoint(x: screen.minX + AppConfig.screenInset + kitten / 2, y: screen.midY)
        return clampCenter(raw, size: kitten, visible: visible)
    }

    public static func clampCenter(_ center: CGPoint, size: CGFloat, visible: CGRect) -> CGPoint {
        let half = size / 2
        let x = min(max(center.x, visible.minX + half), visible.maxX - half)
        let y = min(max(center.y, visible.minY + half), visible.maxY - half)
        return CGPoint(x: x, y: y)
    }

    /// Height of the open capsule. The top is the kitten; the menu grows below it.
    public static func contentHeight(agentCount: Int, scale: CGFloat = 1) -> CGFloat {
        let s = ShellScale.factor(scale)
        let count = max(0, agentCount)
        let agents: CGFloat = count == 0
            ? 0
            : CGFloat(count) * AppConfig.agentRow + CGFloat(count - 1) * AppConfig.rowGap
        let beforePlus: CGFloat = count == 0 ? 0 : AppConfig.rowGap
        return (AppConfig.capsulePadY
            + AppConfig.agentRow
            + AppConfig.dividerGap
            + AppConfig.dividerHeight
            + AppConfig.dividerGap
            + agents
            + beforePlus
            + AppConfig.controlRow
            + AppConfig.rowGap
            + AppConfig.controlRow
            + AppConfig.capsulePadY) * s
    }

    /// Distances from the top of the capsule. The kitten occupies the top and does not move when the menu opens.
    public static func stack(agentCount: Int, scale: CGFloat = 1) -> (kitten: CGFloat, divider: CGFloat, agents: CGFloat, plus: CGFloat, gear: CGFloat) {
        let s = ShellScale.factor(scale)
        let kitten: CGFloat = 0
        let divider = (AppConfig.capsulePadY + AppConfig.agentRow + AppConfig.dividerGap) * s
        let agents = divider + (AppConfig.dividerHeight + AppConfig.dividerGap) * s
        let count = max(0, agentCount)
        let block: CGFloat = count == 0
            ? 0
            : (CGFloat(count) * AppConfig.agentRow + CGFloat(max(0, count - 1)) * AppConfig.rowGap) * s
        let plus = agents + block + (count == 0 ? 0 : AppConfig.rowGap * s)
        let gear = plus + (AppConfig.controlRow + AppConfig.rowGap) * s
        return (kitten, divider, agents, plus, gear)
    }

    /// Expanded frames keep the kitten where it is.
    /// Prefer growing downward while `natural` fits under the kitten top (`roomDown`).
    /// When it does not, grow upward from the kitten bottom (`roomUp`) so the kitten stays at the bottom of the capsule.
    public static func placeCapsule(
        anchorCenter: CGPoint,
        expanded: Bool,
        agentCount: Int,
        visible: CGRect,
        scale: CGFloat = 1
    ) -> (frame: CGRect, scrolls: Bool, upward: Bool) {
        let s = ShellScale.factor(scale)
        let kitten = AppConfig.kittenSize * s
        let center = clampCenter(anchorCenter, size: kitten, visible: visible)
        let top = center.y + kitten / 2
        let bottom = center.y - kitten / 2
        if !expanded {
            return (
                CGRect(x: center.x - kitten / 2, y: bottom, width: kitten, height: kitten),
                false,
                false
            )
        }
        let natural = contentHeight(agentCount: agentCount, scale: s)
        let width = AppConfig.capsuleWidth * s
        let x = center.x - width / 2
        // y-up: space below the kitten top down to the visible bottom, and above the kitten bottom up to the visible top.
        let roomDown = max(kitten, top - visible.minY)
        let roomUp = max(kitten, visible.maxY - bottom)
        if natural <= roomDown + 0.5 {
            return (CGRect(x: x, y: top - natural, width: width, height: natural), false, false)
        }
        if natural <= roomUp + 0.5 {
            return (CGRect(x: x, y: bottom, width: width, height: natural), false, true)
        }
        if roomDown >= roomUp {
            let height = min(natural, roomDown)
            return (CGRect(x: x, y: top - height, width: width, height: height), true, false)
        }
        let height = min(natural, max(kitten, visible.maxY - bottom))
        return (CGRect(x: x, y: bottom, width: width, height: height), natural > height + 0.5, true)
    }

    /// Distance from the top of the open capsule to the agent list, and the chrome below that list.
    public static func agentChrome(agentCount: Int, scale: CGFloat, upward: Bool) -> (agentsTop: CGFloat, tail: CGFloat) {
        let s = ShellScale.factor(scale)
        let count = max(0, agentCount)
        let before: CGFloat = count == 0 ? 0 : AppConfig.rowGap
        if !upward {
            let tops = stack(agentCount: count, scale: s)
            let tail = (before + AppConfig.controlRow + AppConfig.rowGap + AppConfig.controlRow + AppConfig.capsulePadY) * s
            return (tops.agents, tail)
        }
        let head = (AppConfig.capsulePadY + AppConfig.controlRow + AppConfig.rowGap + AppConfig.controlRow + before) * s
        let tail = (AppConfig.dividerGap + AppConfig.dividerHeight + AppConfig.dividerGap + AppConfig.agentRow + AppConfig.capsulePadY) * s
        return (head, tail)
    }
}

/// `pet_scale` from settings. 1 is the 56pt kitten. Zero means that default.
public enum ShellScale {
    public static func factor(_ value: CGFloat) -> CGFloat {
        let number: CGFloat = value == 0 ? 1 : value
        return min(2, max(0.5, number))
    }

    public static func factor(_ value: Double) -> CGFloat {
        factor(CGFloat(value))
    }
}

public struct BubblePlacement: Equatable {
    public var frame: CGRect
    public var beakY: CGFloat
    public var pointsRight: Bool

    public init(frame: CGRect, beakY: CGFloat, pointsRight: Bool) {
        self.frame = frame
        self.beakY = beakY
        self.pointsRight = pointsRight
    }
}

public enum BubblePlacer {
    /// Puts the bubble beside the capsule, not the icon.
    /// The kitten's screen half picks the side. The beak aims at `anchor` and stops `beakGap` short of the capsule.
    /// Beak y stays inside `[radius+8, height-radius-8]` after the bubble is clamped.
    public static func place(anchor: CGRect, capsule: CGRect, size: CGSize, screen: CGRect) -> BubblePlacement {
        let width = max(1, size.width)
        let height = max(1, size.height)
        let onLeftHalf = capsule.midX < screen.midX
        let pointsRight = !onLeftHalf
        var x = pointsRight
            ? capsule.minX - AppConfig.beakGap - width
            : capsule.maxX + AppConfig.beakGap
        var y = anchor.midY - height / 2
        y = min(max(y, screen.minY), max(screen.minY, screen.maxY - height))
        x = min(max(x, screen.minX), max(screen.minX, screen.maxX - width))
        var frame = CGRect(x: x, y: y, width: width, height: height)
        if frame.intersects(capsule) {
            frame.origin.x = pointsRight
                ? capsule.minX - AppConfig.beakGap - width
                : capsule.maxX + AppConfig.beakGap
        }
        let fromBottom = anchor.midY - frame.minY
        var beakFromTop = height - fromBottom
        let lower = AppConfig.bubbleRadius + 8
        let upper = height - AppConfig.bubbleRadius - 8
        if upper >= lower {
            beakFromTop = min(max(beakFromTop, lower), upper)
        } else {
            beakFromTop = height / 2
        }
        return BubblePlacement(frame: frame, beakY: beakFromTop, pointsRight: pointsRight)
    }
}

/// Relative time beside the source chip. Empty when the task has no timestamp.
public enum BubbleAge {
    public static func relative(updated: Double, now: Double) -> String {
        guard updated > 1 else { return "" }
        let seconds = max(0, now - updated)
        if seconds < 60 { return "now" }
        let minutes = Int(seconds / 60)
        if minutes < 60 { return "\(minutes)m" }
        let hours = minutes / 60
        if hours < 48 { return "\(hours)h" }
        return "\(hours / 24)d"
    }
}

/// Constant-width capsule in y-up coordinates, origin at the bottom left.
public enum CapsuleOutline {
    public static func contains(_ point: CGPoint, width: CGFloat, height: CGFloat) -> Bool {
        let w = max(width, 1)
        let h = max(height, 1)
        guard point.x >= 0, point.y >= 0, point.x <= w, point.y <= h else { return false }
        let radius = min(w, h) / 2
        if point.y >= radius, point.y <= h - radius { return true }
        let centerY: CGFloat = point.y < radius ? radius : h - radius
        let dx = point.x - w / 2
        let dy = point.y - centerY
        return dx * dx + dy * dy <= radius * radius + 0.25
    }
}

public struct AgentIconCandidate: Equatable {
    public var id: String
    public var source: String
    public var title: String
    public var status: String
    public var read: Bool
    public var updated: Double
    /// True when the card still needs a choice such as Allow / Deny.
    public var hasPendingOptions: Bool

    public init(
        id: String,
        source: String,
        title: String,
        status: String,
        read: Bool,
        updated: Double,
        hasPendingOptions: Bool = false
    ) {
        self.id = id
        self.source = source
        self.title = title
        self.status = status
        self.read = read
        self.updated = updated
        self.hasPendingOptions = hasPendingOptions
    }
}

public struct AgentIconRow: Equatable {
    public var id: String
    public var source: String
    public var title: String
    public var status: String
    public var read: Bool
    public var showsDot: Bool
    public var memberIDs: [String]

    public init(id: String, source: String, title: String, status: String, read: Bool, showsDot: Bool, memberIDs: [String]) {
        self.id = id
        self.source = source
        self.title = title
        self.status = status
        self.read = read
        self.showsDot = showsDot
        self.memberIDs = memberIDs
    }
}

public enum AgentIconList {
    public static let order = ["cursor", "codex", "claude", "gemini", "copilot", "opencode", "grok-bot", "gpt"]

    /// One row per conversation.
    /// - `running` / `wait`: always stay in the capsule (silent icon; click opens the card).
    /// - `done` / `fail`: stay while unread, while the bubble is holding them, or while choices remain.
    /// - Read finished chats with no pending options leave after the bubble closes.
    public static func rows(_ items: [AgentIconCandidate], holding: Set<String> = []) -> [AgentIconRow] {
        var visible: [AgentIconCandidate] = []
        for item in items {
            let source = AgentLaunch.normalize(item.source)
            if source == "mochi" { continue }
            guard shouldShow(item, holding: holding) else { continue }
            var copy = item
            copy.source = source
            visible.append(copy)
        }
        visible.sort { lhs, rhs in
            let left = order.firstIndex(of: lhs.source) ?? 99
            let right = order.firstIndex(of: rhs.source) ?? 99
            if left != right { return left < right }
            if lhs.updated != rhs.updated { return lhs.updated > rhs.updated }
            return lhs.id > rhs.id
        }
        return visible.map { item in
            let needsAttention = item.status == "wait" || item.hasPendingOptions
                || item.status == "done" || item.status == "fail"
            let dot = !item.read && needsAttention
            return AgentIconRow(
                id: item.id,
                source: item.source,
                title: item.title,
                status: item.status,
                read: item.read,
                showsDot: dot,
                memberIDs: [item.id]
            )
        }
    }

    /// Live work never leaves the capsule. Finished work leaves only after a read with no choices.
    public static func shouldShow(_ item: AgentIconCandidate, holding: Set<String> = []) -> Bool {
        switch item.status {
        case "running", "wait":
            return true
        case "done", "fail":
            if holding.contains(item.id) { return true }
            if item.hasPendingOptions { return true }
            return !item.read
        default:
            return false
        }
    }

    /// The open permission card stays only while that wait is still this conversation's face.
    public static func permissionStaysOpen(taskStatus: String, isFace: Bool) -> Bool {
        taskStatus == "wait" && isFace
    }
}

public struct AlertSoundGate: Equatable {
    public var last: Double?
    public init() {}

    public mutating func shouldPlay(now: Double) -> Bool {
        if let last, now - last < 1 { return false }
        self.last = now
        return true
    }
}
