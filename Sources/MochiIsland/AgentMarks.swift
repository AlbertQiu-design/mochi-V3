import AppKit
import MochiCore
import SwiftUI

enum MarkPath {
    static func make(_ d: String) -> Path {
        var path = Path()
        var index = d.startIndex
        var command: Character = "M"
        var current = CGPoint.zero
        var start = CGPoint.zero

        func skip() {
            while index < d.endIndex, d[index].isWhitespace || d[index] == "," {
                index = d.index(after: index)
            }
        }

        func number() -> CGFloat? {
            skip()
            guard index < d.endIndex else { return nil }
            let begin = index
            if d[index] == "-" || d[index] == "+" { index = d.index(after: index) }
            var saw = false
            while index < d.endIndex, d[index].isNumber || d[index] == "." {
                saw = true
                index = d.index(after: index)
            }
            guard saw, let value = Double(d[begin..<index]) else { return nil }
            return CGFloat(value)
        }

        while index < d.endIndex {
            skip()
            if index >= d.endIndex { break }
            if d[index].isLetter {
                command = d[index]
                index = d.index(after: index)
            }
            if command == "Z" || command == "z" {
                path.closeSubpath()
                current = start
                continue
            }
            guard let x = number(), let y = number() else { break }
            switch command {
            case "M":
                current = CGPoint(x: x, y: y)
                start = current
                path.move(to: current)
                command = "L"
            case "L":
                current = CGPoint(x: x, y: y)
                path.addLine(to: current)
            case "Q":
                guard let x2 = number(), let y2 = number() else { return path }
                current = CGPoint(x: x2, y: y2)
                path.addQuadCurve(to: current, control: CGPoint(x: x, y: y))
            case "C":
                guard let x2 = number(), let y2 = number(), let x3 = number(), let y3 = number() else { return path }
                current = CGPoint(x: x3, y: y3)
                path.addCurve(to: current, control1: CGPoint(x: x, y: y), control2: CGPoint(x: x2, y: y2))
            default:
                break
            }
        }
        return path
    }
}

/// The earlier full kitten-v1 illustration (`kitten-wake.png`), not the simplified mark.
/// `look` is a letter id (`a`…`f`); until separate art ships, each look tints the same PNG
/// and stamps a capital letter badge so Settings preview and the desktop capsule match.
struct FullKitten: View {
    var look: String = "a"

    private var resolved: String { IslandPrefs.normalizeLook(look) }

    var body: some View {
        ZStack {
            base
                .colorMultiply(PetLookStyle.tint(for: resolved))
            letterBadge
        }
        .accessibilityLabel("Pet \(IslandPrefs.petLetter(for: resolved))")
    }

    @ViewBuilder
    private var base: some View {
        if let image = Self.artwork {
            Image(nsImage: image)
                .resizable()
                .interpolation(.high)
                .scaledToFit()
        } else {
            KittenMark()
        }
    }

    private var letterBadge: some View {
        GeometryReader { geo in
            let side = min(geo.size.width, geo.size.height)
            let badge = max(10, side * 0.28)
            Text(IslandPrefs.petLetter(for: resolved))
                .font(.system(size: badge * 0.62, weight: .bold, design: .rounded))
                .foregroundStyle(.white)
                .frame(width: badge, height: badge)
                .background(Circle().fill(PetLookStyle.badge(for: resolved)))
                .overlay(Circle().strokeBorder(Color.white.opacity(0.85), lineWidth: max(0.8, badge * 0.06)))
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)
                .padding(side * 0.04)
        }
        .allowsHitTesting(false)
    }

    static let artwork: NSImage? = {
        guard let url = Bundle.main.url(forResource: "kitten-wake", withExtension: "png", subdirectory: "Web/pets") else {
            return nil
        }
        return NSImage(contentsOf: url)
    }()
}

enum PetLookStyle {
    static func tint(for look: String) -> Color {
        switch IslandPrefs.normalizeLook(look) {
        case "b": return Color(red: 1.0, green: 0.86, blue: 0.78)
        case "c": return Color(red: 0.78, green: 0.90, blue: 1.0)
        case "d": return Color(red: 0.78, green: 0.96, blue: 0.86)
        case "e": return Color(red: 0.90, green: 0.82, blue: 1.0)
        case "f": return Color(red: 1.0, green: 0.94, blue: 0.72)
        default: return .white
        }
    }

    static func badge(for look: String) -> Color {
        switch IslandPrefs.normalizeLook(look) {
        case "b": return Color(red: 0.86, green: 0.42, blue: 0.28)
        case "c": return Color(red: 0.28, green: 0.52, blue: 0.86)
        case "d": return Color(red: 0.22, green: 0.62, blue: 0.44)
        case "e": return Color(red: 0.52, green: 0.34, blue: 0.78)
        case "f": return Color(red: 0.78, green: 0.58, blue: 0.16)
        default: return Color(red: 0.35, green: 0.28, blue: 0.22)
        }
    }
}

struct KittenMark: View {
    private static let earL = "M17.5 20.0 L21.0 8.5 L26.5 18.5 Z"
    private static let earLInner = "M19.2 18.5 L21.2 11.5 L24.8 17.8 Z"
    private static let earR = "M46.5 20.0 L43.0 8.5 L37.5 18.5 Z"
    private static let earRInner = "M44.8 18.5 L42.8 11.5 L39.2 17.8 Z"
    private static let body = "M32.0 17.0 C34.8 16.4 37.5 17.2 39.5 19.2 L41.2 18.0 L42.5 20.2 C44.8 20.0 47.0 22.0 47.8 24.8 L49.5 25.5 L49.0 27.8 C50.8 29.5 51.5 32.5 50.8 35.5 C51.8 38.5 50.5 42.0 48.0 44.0 L49.0 46.5 L46.5 47.5 C45.0 50.5 41.0 53.0 36.5 54.0 C34.5 55.2 29.5 55.2 27.5 54.0 C23.0 53.0 19.0 50.5 17.5 47.5 L15.0 46.5 L16.0 44.0 C13.5 42.0 12.2 38.5 13.2 35.5 C12.5 32.5 13.2 29.5 15.0 27.8 L14.5 25.5 L16.2 24.8 C17.0 22.0 19.2 20.0 21.5 20.2 L22.8 18.0 L24.5 19.2 C26.5 17.2 29.2 16.4 32.0 17.0 Z"
    private static let mouth = "M28.5 36.2 Q32 39.0 35.5 36.2"
    private static let tail = "M42.0 46.5 C45.5 46.0 49.5 43.5 51.5 39.5 C53.5 35.0 53.0 30.0 50.0 27.5 C47.5 25.5 44.5 26.5 44.0 29.5 C43.6 31.8 45.2 33.5 47.0 34.2 C46.0 36.8 44.5 39.8 42.8 42.5 C41.5 44.5 40.8 46.8 42.0 46.5 Z"
    private static let ink = Color(red: 61 / 255, green: 41 / 255, blue: 20 / 255)

    var body: some View {
        Canvas { context, size in
            let scale = min(size.width, size.height) / 64
            context.scaleBy(x: scale, y: scale)
            func paint(_ d: String, fill: Color, line: CGFloat = 0) {
                let path = MarkPath.make(d)
                context.fill(path, with: .color(fill))
                if line > 0 {
                    context.stroke(path, with: .color(Self.ink), style: StrokeStyle(lineWidth: line, lineCap: .round, lineJoin: .round))
                }
            }
            paint(Self.tail, fill: .white, line: 2.1)
            paint(Self.earL, fill: .white, line: 2.1)
            paint(Self.earLInner, fill: Color(red: 1, green: 182 / 255, blue: 193 / 255))
            paint(Self.earR, fill: .white, line: 2.1)
            paint(Self.earRInner, fill: Color(red: 1, green: 182 / 255, blue: 193 / 255))
            paint(Self.body, fill: .white, line: 1.9)
            context.fill(Path(ellipseIn: CGRect(x: 30.4, y: 32, width: 3.2, height: 2.4)), with: .color(Color(red: 244 / 255, green: 160 / 255, blue: 176 / 255)))
            context.stroke(MarkPath.make(Self.mouth), with: .color(Self.ink), style: StrokeStyle(lineWidth: 1.4, lineCap: .round))
            context.fill(Path(ellipseIn: CGRect(x: 22.3, y: 24.6, width: 6.4, height: 6.8)), with: .color(Color(red: 74 / 255, green: 144 / 255, blue: 226 / 255)))
            context.stroke(Path(ellipseIn: CGRect(x: 22.3, y: 24.6, width: 6.4, height: 6.8)), with: .color(Self.ink), lineWidth: 0.9)
            context.fill(Path(ellipseIn: CGRect(x: 35.3, y: 24.6, width: 6.4, height: 6.8)), with: .color(Color(red: 245 / 255, green: 166 / 255, blue: 35 / 255)))
            context.stroke(Path(ellipseIn: CGRect(x: 35.3, y: 24.6, width: 6.4, height: 6.8)), with: .color(Self.ink), lineWidth: 0.9)
        }
        .accessibilityLabel("Pet")
    }
}

struct AgentMark: View {
    var source: String

    var body: some View {
        Canvas { context, size in
            let scale = min(size.width, size.height) / 28
            context.scaleBy(x: scale, y: scale)
            switch AgentLaunch.normalize(source) {
            case "copilot":
                context.fill(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(white: 0.12)))
                context.stroke(Path(ellipseIn: CGRect(x: 7, y: 7, width: 14, height: 14)), with: .color(.white), lineWidth: 1.6)
            case "opencode":
                context.fill(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(red: 92 / 255, green: 124 / 255, blue: 92 / 255)))
                var slash = Path()
                slash.move(to: CGPoint(x: 10, y: 8))
                slash.addLine(to: CGPoint(x: 18, y: 20))
                context.stroke(slash, with: .color(.white), style: StrokeStyle(lineWidth: 1.8, lineCap: .round))
            case "gemini":
                context.fill(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(red: 66 / 255, green: 133 / 255, blue: 244 / 255)))
                context.fill(Path(ellipseIn: CGRect(x: 8, y: 8, width: 12, height: 12)), with: .color(.white))
            case "claude":
                context.fill(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(red: 204 / 255, green: 122 / 255, blue: 92 / 255)))
                var mark = Path()
                mark.move(to: CGPoint(x: 18, y: 8))
                mark.addQuadCurve(to: CGPoint(x: 10, y: 14), control: CGPoint(x: 8, y: 8))
                mark.addQuadCurve(to: CGPoint(x: 18, y: 20), control: CGPoint(x: 8, y: 20))
                context.stroke(mark, with: .color(.white), style: StrokeStyle(lineWidth: 1.8, lineCap: .round))
            case "codex":
                for point in [CGPoint(x: 8, y: 8), CGPoint(x: 20, y: 8), CGPoint(x: 8, y: 20), CGPoint(x: 20, y: 20)] {
                    context.fill(Path(ellipseIn: CGRect(x: point.x - 6.5, y: point.y - 6.5, width: 13, height: 13)), with: .color(Color(red: 43 / 255, green: 181 / 255, blue: 160 / 255)))
                }
                context.fill(Path(ellipseIn: CGRect(x: 10, y: 10, width: 8, height: 8)), with: .color(Color(red: 26 / 255, green: 143 / 255, blue: 125 / 255)))
            case "grok-bot":
                context.fill(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(red: 10 / 255, green: 10 / 255, blue: 10 / 255)))
                context.stroke(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(white: 0.23)), lineWidth: 1)
                var ring = Path(ellipseIn: CGRect(x: -2, y: 8.5, width: 32, height: 11))
                let transform = CGAffineTransform(translationX: 14, y: 14).rotated(by: -28 * .pi / 180).translatedBy(x: -14, y: -14)
                ring = ring.applying(transform)
                context.stroke(ring, with: .color(Color(red: 245 / 255, green: 245 / 255, blue: 247 / 255)), lineWidth: 1.4)
                var cross = Path()
                cross.move(to: CGPoint(x: 10, y: 10))
                cross.addLine(to: CGPoint(x: 18, y: 18))
                cross.move(to: CGPoint(x: 18, y: 10))
                cross.addLine(to: CGPoint(x: 10, y: 18))
                context.stroke(cross, with: .color(Color(red: 245 / 255, green: 245 / 255, blue: 247 / 255)), style: StrokeStyle(lineWidth: 1.6, lineCap: .round))
            case "gpt":
                context.fill(Path(ellipseIn: CGRect(x: 1, y: 1, width: 26, height: 26)), with: .color(Color(white: 0.1)))
                for step in 0..<6 {
                    var petal = Path(ellipseIn: CGRect(x: 10.8, y: 2.8, width: 6.4, height: 8.4))
                    let turn = CGAffineTransform(translationX: 14, y: 14).rotated(by: CGFloat(step) * .pi / 3).translatedBy(x: -14, y: -14)
                    petal = petal.applying(turn)
                    context.fill(petal, with: .color(.white))
                }
                context.fill(Path(ellipseIn: CGRect(x: 11, y: 11, width: 6, height: 6)), with: .color(Color(white: 0.1)))
            default:
                let cube = MarkPath.make("M2 8 L14 2 L26 8 L26 18 L14 24 L2 18 Z")
                context.fill(cube, with: .color(.white))
                context.stroke(cube, with: .color(Color(white: 0.9)), lineWidth: 1)
                var seams = Path()
                seams.move(to: CGPoint(x: 14, y: 2))
                seams.addLine(to: CGPoint(x: 14, y: 14))
                seams.addLine(to: CGPoint(x: 26, y: 8))
                seams.move(to: CGPoint(x: 14, y: 14))
                seams.addLine(to: CGPoint(x: 2, y: 8))
                context.stroke(seams, with: .color(Color(white: 0.68)), lineWidth: 0.9)
            }
        }
        .accessibilityLabel(SlotFace.displayName(for: source))
    }
}

/// Status ring around one agent icon. Drawn in the icon row, inside the 44pt row.
/// `running` is a ring of blue dots that turns only while this view is on screen.
/// `wait` and `fail` share a red ring with a bottom-right gap and an X in that gap.
/// `done` is the same ring in green with a check. The stroke does not pass through the mark.
struct AgentStatusRing: View {
    var status: String

    var body: some View {
        switch status {
        case "running":
            TimelineView(.animation(minimumInterval: 1.0 / 30.0)) { context in
                AgentThinkingDots(date: context.date)
            }
        case "wait", "fail":
            AgentCornerRing(mark: .cross, color: Self.stuck)
        case "done":
            AgentCornerRing(mark: .check, color: Self.done)
        default:
            EmptyView()
        }
    }

    static func voice(for status: String) -> String {
        switch status {
        case "running": return "Thinking"
        case "wait", "fail": return "Stuck"
        case "done": return "Done"
        default: return ""
        }
    }

    static let thinking = Color(red: 0.20, green: 0.50, blue: 1)
    static let stuck = Color(red: 1, green: 59 / 255, blue: 48 / 255)
    static let done = Color(red: 0.18, green: 0.73, blue: 0.36)
}

private struct AgentThinkingDots: View {
    var date: Date

    var body: some View {
        Canvas { context, size in
            let layout = AgentRingLayout(size: size)
            let spin = date.timeIntervalSinceReferenceDate
                .truncatingRemainder(dividingBy: layout.period) / layout.period
            let turn = CGFloat(spin) * 2 * .pi
            for index in 0..<layout.dotCount {
                let angle = turn + (CGFloat(index) / CGFloat(layout.dotCount)) * 2 * .pi
                let point = layout.point(angle: angle, radius: layout.radius)
                let dot = layout.dot
                let rect = CGRect(x: point.x - dot / 2, y: point.y - dot / 2, width: dot, height: dot)
                context.fill(Path(ellipseIn: rect), with: .color(AgentStatusRing.thinking))
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

private struct AgentCornerRing: View {
    var mark: AgentCornerMark
    var color: Color

    var body: some View {
        Canvas { context, size in
            let layout = AgentRingLayout(size: size)
            context.stroke(
                layout.ringPath(),
                with: .color(color),
                style: StrokeStyle(lineWidth: layout.stroke, lineCap: .round, lineJoin: .round)
            )
            let badge = layout.point(angle: layout.gapCenter, radius: layout.radius)
            var path = Path()
            let arm = layout.mark
            switch mark {
            case .cross:
                path.move(to: CGPoint(x: badge.x - arm, y: badge.y - arm))
                path.addLine(to: CGPoint(x: badge.x + arm, y: badge.y + arm))
                path.move(to: CGPoint(x: badge.x + arm, y: badge.y - arm))
                path.addLine(to: CGPoint(x: badge.x - arm, y: badge.y + arm))
            case .check:
                path.move(to: CGPoint(x: badge.x - arm, y: badge.y + arm * 0.08))
                path.addLine(to: CGPoint(x: badge.x - arm * 0.28, y: badge.y + arm * 0.78))
                path.addLine(to: CGPoint(x: badge.x + arm * 1.08, y: badge.y - arm * 0.82))
            }
            context.stroke(
                path,
                with: .color(color),
                style: StrokeStyle(lineWidth: layout.stroke, lineCap: .round, lineJoin: .round)
            )
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

private enum AgentCornerMark {
    case cross
    case check
}

/// Circle just outside the 28pt icon, inside the 44pt row. Angles are y-down: 0 is right, π/4 is bottom-right.
private struct AgentRingLayout {
    var size: CGSize

    var unit: CGFloat { min(size.width, size.height) / AppConfig.agentRow }
    var center: CGPoint { CGPoint(x: size.width / 2, y: size.height / 2) }
    var radius: CGFloat { 19.15 * unit }
    var stroke: CGFloat { 1.9 * unit }
    var dot: CGFloat { 2.7 * unit }
    var dotCount: Int { 10 }
    var period: Double { 2.4 }
    var gapCenter: CGFloat { .pi / 4 }
    var gap: CGFloat { 58 * .pi / 180 }
    var mark: CGFloat { 2.85 * unit }

    func point(angle: CGFloat, radius: CGFloat) -> CGPoint {
        CGPoint(x: center.x + cos(angle) * radius, y: center.y + sin(angle) * radius)
    }

    /// Long arc that stops before the bottom-right, leaving the mark's notch.
    func ringPath() -> Path {
        let start = gapCenter + gap / 2
        let sweep = 2 * CGFloat.pi - gap
        let steps = 96
        var path = Path()
        for step in 0...steps {
            let angle = start + sweep * CGFloat(step) / CGFloat(steps)
            let next = point(angle: angle, radius: radius)
            if step == 0 {
                path.move(to: next)
            } else {
                path.addLine(to: next)
            }
        }
        return path
    }
}

enum AgentIcon {
    static func image(source: String) -> NSImage? {
        let plan = AgentLaunch.plan(source: source)
        if let path = plan.appPath {
            let icon = NSWorkspace.shared.icon(forFile: path)
            icon.size = NSSize(width: 32, height: 32)
            return icon
        }
        let name = SlotFace.agentMark(for: source)
        let url = Bundle.main.url(forResource: name, withExtension: "svg", subdirectory: "Web/pets")
        guard let url, let image = NSImage(contentsOf: url) else { return nil }
        image.size = NSSize(width: 28, height: 28)
        return image
    }
}
