import AppKit
import MochiCore
import SwiftUI

struct BubbleShape: Shape {
    var radius: CGFloat = AppConfig.bubbleRadius
    var beakY: CGFloat
    var pointsRight: Bool

    func path(in rect: CGRect) -> Path {
        let beak = AppConfig.beakWidth
        let body = pointsRight
            ? CGRect(x: rect.minX, y: rect.minY, width: max(1, rect.width - beak), height: rect.height)
            : CGRect(x: rect.minX + beak, y: rect.minY, width: max(1, rect.width - beak), height: rect.height)
        let corner = min(radius, min(body.width, body.height) / 2)
        let edgeX = pointsRight ? body.maxX : body.minX
        let sign: CGFloat = pointsRight ? 1 : -1
        let lower = corner + 8
        let upper = max(lower, rect.height - corner - 8)
        let mid = min(max(beakY, lower), upper)
        var path = Path()
        path.move(to: CGPoint(x: body.minX + corner, y: body.minY))
        path.addLine(to: CGPoint(x: body.maxX - corner, y: body.minY))
        path.addArc(
            center: CGPoint(x: body.maxX - corner, y: body.minY + corner),
            radius: corner,
            startAngle: .degrees(-90),
            endAngle: .degrees(0),
            clockwise: false
        )
        if pointsRight {
            appendBeak(&path, edgeX: edgeX, sign: sign, mid: mid, downward: true)
        }
        path.addLine(to: CGPoint(x: body.maxX, y: body.maxY - corner))
        path.addArc(
            center: CGPoint(x: body.maxX - corner, y: body.maxY - corner),
            radius: corner,
            startAngle: .degrees(0),
            endAngle: .degrees(90),
            clockwise: false
        )
        path.addLine(to: CGPoint(x: body.minX + corner, y: body.maxY))
        path.addArc(
            center: CGPoint(x: body.minX + corner, y: body.maxY - corner),
            radius: corner,
            startAngle: .degrees(90),
            endAngle: .degrees(180),
            clockwise: false
        )
        if !pointsRight {
            appendBeak(&path, edgeX: edgeX, sign: sign, mid: mid, downward: false)
        }
        path.addLine(to: CGPoint(x: body.minX, y: body.minY + corner))
        path.addArc(
            center: CGPoint(x: body.minX + corner, y: body.minY + corner),
            radius: corner,
            startAngle: .degrees(180),
            endAngle: .degrees(270),
            clockwise: false
        )
        path.closeSubpath()
        return path
    }

    /// Beak root curves and a 2.8pt tip, taken from the liquid-glass popup SVG.
    private func appendBeak(_ path: inout Path, edgeX: CGFloat, sign: CGFloat, mid: CGFloat, downward: Bool) {
        func point(_ dx: CGFloat, _ dy: CGFloat) -> CGPoint {
            CGPoint(x: edgeX + sign * dx, y: mid + (downward ? dy : -dy))
        }
        path.addLine(to: point(0, -9.5))
        path.addCurve(to: point(3.5, -5.5), control1: point(0, -7.5), control2: point(1.5, -6.5))
        path.addCurve(to: point(9.2, -1.2), control1: point(6.5, -4), control2: point(8.5, -2.5))
        let chord: CGFloat = 2.53
        let center = CGPoint(x: edgeX + sign * (9.2 - chord), y: mid)
        let start = point(9.2, -1.2)
        let end = point(9.2, 1.2)
        let startAngle = Angle.radians(atan2(start.y - center.y, start.x - center.x))
        let endAngle = Angle.radians(atan2(end.y - center.y, end.x - center.x))
        path.addArc(
            center: center,
            radius: AppConfig.beakTipRadius,
            startAngle: startAngle,
            endAngle: endAngle,
            clockwise: downward ? false : true
        )
        path.addCurve(to: point(3.5, 5.5), control1: point(8.5, 2.5), control2: point(6.5, 4))
        path.addCurve(to: point(0, 9.5), control1: point(1.5, 6.5), control2: point(0, 7.5))
    }
}

struct BubbleChoice: Identifiable, Equatable {
    var id: String
    var label: String
}

enum BubbleKind {
    case card, alert, picker, label
}

@MainActor
final class BubbleModel: ObservableObject {
    @Published var kind: BubbleKind = .card
    @Published var title = ""
    @Published var appName = ""
    @Published var source = "cursor"
    @Published var status = ""
    @Published var summary = ""
    @Published var updated: Double = 0
    @Published var options: [BubbleChoice] = []
    @Published var icon: NSImage?
    @Published var beakY: CGFloat = 28
    @Published var pointsRight = true
    @Published var bodyWidth: CGFloat = 210
    @Published var summaryHeight: CGFloat = 14
    @Published var computedHeight: CGFloat = 44
    var taskID = ""
    var requestID = ""
    var questionID = ""
    var taskKind = ""
    var onChoice: ((BubbleChoice) -> Void)?
    var onOpen: (() -> Void)?
    var onPick: ((String) -> Void)?
}

struct BubbleFace: View {
    @ObservedObject var model: BubbleModel
    @ObservedObject private var pickerMenu = PickerMenu.shared

    var body: some View {
        let plate = RoundedRectangle(cornerRadius: AppConfig.bubbleRadius, style: .continuous)
        HStack(spacing: 0) {
            if !model.pointsRight {
                Color.clear.frame(width: AppConfig.beakWidth)
            }
            content
                .frame(width: model.bodyWidth, height: model.computedHeight, alignment: .top)
            if model.pointsRight {
                Color.clear.frame(width: AppConfig.beakWidth)
            }
        }
        .background {
            // Same regular liquid glass as the capsule. Standard shapes merge into the beak.
            // A custom shape here was painting a solid blue plate.
            GlassEffectContainer(spacing: 24) {
                plate
                    .fill(Color.clear)
                    .glassEffect(.regular.tint(nil), in: plate)
                    .padding(model.pointsRight ? .trailing : .leading, 2)
                Capsule()
                    .fill(Color.clear)
                    .frame(width: AppConfig.beakHeight, height: AppConfig.beakWidth + 8)
                    .glassEffect(.regular.tint(nil), in: Capsule())
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: model.pointsRight ? .trailing : .leading)
                    .offset(y: model.beakY - model.computedHeight / 2)
            }
            .allowsHitTesting(false)
        }
    }

    @ViewBuilder
    private var content: some View {
        switch model.kind {
        case .picker:
            picker
        case .label:
            Text(model.title)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(.primary)
                .lineLimit(2)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
                .padding(.horizontal, AppConfig.bubblePadX)
                .padding(.vertical, AppConfig.bubblePadY)
        case .card, .alert:
            card
        }
    }

    /// Header / status / summary (and empty card chrome) open the source chat.
    /// Allow / Deny and other choice buttons keep their own actions.
    private var card: some View {
        VStack(alignment: .leading, spacing: 6) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(alignment: .center, spacing: 6) {
                    icon(model.icon, source: model.source, side: 16)
                    Text(model.title.isEmpty ? model.appName : model.title)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(.primary)
                        .lineLimit(1)
                        .truncationMode(.tail)
                    Spacer(minLength: 4)
                    if !model.appName.isEmpty {
                        Text(model.appName)
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundStyle(.primary)
                            .lineLimit(1)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Capsule().fill(Color.primary.opacity(0.1)))
                    }
                    if !ageLabel.isEmpty {
                        Text(ageLabel)
                            .font(.system(size: 11, weight: .regular))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                if !model.status.isEmpty {
                    Text(model.status)
                        .font(.system(size: 11, weight: .regular))
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                if !model.summary.isEmpty {
                    Text(model.summary)
                        .font(.system(size: 11, weight: .regular))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.tail)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .contentShape(Rectangle())
            .onTapGesture { model.onOpen?() }
            if !model.options.isEmpty {
                VStack(spacing: 6) {
                    ForEach(model.options) { option in
                        Button {
                            model.onChoice?(option)
                        } label: {
                            Text(option.label)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundStyle(.primary)
                                .multilineTextAlignment(.leading)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, 10)
                                .padding(.vertical, 8)
                                .background {
                                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                                        .fill(Color.primary.opacity(0.1))
                                }
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
        // Extra inset on the beak side, toward the kitten, so the blurb stays in the solid glass.
        .padding(.leading, AppConfig.bubblePadX + (model.pointsRight ? 0 : 16))
        .padding(.trailing, AppConfig.bubblePadX + (model.pointsRight ? 16 : 0))
        .padding(.vertical, AppConfig.bubblePadY)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var ageLabel: String {
        BubbleAge.relative(updated: model.updated, now: Date().timeIntervalSince1970)
    }

    private var picker: some View {
        HStack(spacing: 8) {
            ForEach(pickerMenu.sources, id: \.self) { source in
                Button {
                    model.onPick?(source)
                }                 label: {
                    icon(AgentIcon.image(source: source), source: source, side: 28)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(SlotFace.pickerName(for: source))
            }
        }
        .padding(.horizontal, AppConfig.bubblePadX)
        .padding(.vertical, AppConfig.bubblePadY)
    }

    private func icon(_ image: NSImage?, source: String, side: CGFloat) -> some View {
        Group {
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .interpolation(.high)
                    .frame(width: side, height: side)
            } else {
                AgentMark(source: source)
                    .frame(width: side, height: side)
            }
        }
    }
}

enum BubbleMeasure {
    static func textHeight(_ text: String, width: CGFloat, font: NSFont, lines: Int) -> CGFloat {
        let field = NSTextField(wrappingLabelWithString: text)
        field.font = font
        field.maximumNumberOfLines = lines
        field.preferredMaxLayoutWidth = width
        let measured = field.cell?.cellSize(forBounds: NSRect(x: 0, y: 0, width: width, height: 8_000)).height ?? font.boundingRectForFont.height
        let cap = ceil(font.boundingRectForFont.height * CGFloat(lines) + 4)
        return min(max(font.boundingRectForFont.height, measured), cap)
    }

    static func probe(screenHeight: CGFloat) -> [String: Any] {
        let titleFont = NSFont.systemFont(ofSize: 13, weight: .semibold)
        let summaryFont = NSFont.systemFont(ofSize: 11, weight: .regular)
        let width = AppConfig.bubbleMaxWidth - AppConfig.beakWidth - AppConfig.bubblePadX * 2
        let one = textHeight("Hello", width: width, font: titleFont, lines: 2)
        let two = textHeight(
            "Open title that should wrap onto more than one line when the card is expanded",
            width: width,
            font: titleFont,
            lines: 2
        )
        let summary = textHeight(String(repeating: "A long prompt that stays inside the bubble. ", count: 40), width: width, font: summaryFont, lines: 4)
        let choice = textHeight("Choose the third long option without clipping the label", width: width - 20, font: titleFont, lines: 3)
        let half = screenHeight / 2
        return [
            "oneLine": one,
            "twoLine": two,
            "summary": summary,
            "choice": choice,
            "twoTaller": two > one + 1,
            "summaryCapped": summary > 0 && summary <= half + 1,
            "choiceVisible": choice > 12
        ]
    }
}

@MainActor
final class BubblePanel: NSObject {
    let panel: CapsulePanel
    let model: BubbleModel
    private let hosting: NSHostingView<BubbleFace>
    private(set) var visible = false

    override init() {
        let model = BubbleModel()
        let panel = CapsulePanel(
            contentRect: NSRect(x: 0, y: 0, width: 180, height: 80),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.isFloatingPanel = true
        panel.isMovable = false
        panel.ignoresMouseEvents = true
        panel.becomesKeyOnlyIfNeeded = true
        panel.isRestorable = false
        panel.animationBehavior = .none
        panel.title = "Mochi Bubble"
        panel.level = .floating
        let hosting = BubbleHostingView(rootView: BubbleFace(model: model))
        hosting.sizingOptions = []
        hosting.wantsLayer = true
        hosting.layer?.isOpaque = false
        hosting.layer?.backgroundColor = NSColor.clear.cgColor
        panel.contentView = hosting
        self.panel = panel
        self.model = model
        self.hosting = hosting
        super.init()
    }

    func attach(to host: NSWindow) {
        panel.level = NSWindow.Level(rawValue: host.level.rawValue + 3)
        panel.collectionBehavior = host.collectionBehavior
        if host.childWindows?.contains(panel) != true {
            host.addChildWindow(panel, ordered: .above)
        }
        hide()
    }

    func hide() {
        visible = false
        panel.alphaValue = 0
        panel.ignoresMouseEvents = true
        panel.orderOut(nil)
    }

    func show(anchor: CGRect, capsule: CGRect, screen: CGRect) {
        let body = measuredBody()
        let size = CGSize(width: body.width + AppConfig.beakWidth, height: body.height)
        let placed = BubblePlacer.place(anchor: anchor, capsule: capsule, size: size, screen: screen)
        model.beakY = placed.beakY
        model.pointsRight = placed.pointsRight
        model.bodyWidth = body.width
        hosting.frame = NSRect(origin: .zero, size: NSSize(width: size.width, height: size.height))
        panel.setFrame(placed.frame, display: true)
        panel.alphaValue = 1
        panel.ignoresMouseEvents = false
        visible = true
        panel.orderFrontRegardless()
    }

    private func measuredBody() -> CGSize {
        let maxBody = AppConfig.bubbleMaxWidth - AppConfig.beakWidth
        let minBody = AppConfig.bubbleMinWidth - AppConfig.beakWidth
        prepare(width: maxBody)
        let host = NSHostingController(rootView: BubbleFace(model: model))
        let fitted = host.sizeThatFits(in: CGSize(width: maxBody, height: 10_000))
        var height = max(AppConfig.bubbleMinHeight, model.computedHeight)
        if fitted.height.isFinite, fitted.height >= AppConfig.bubbleMinHeight, fitted.height < 4_000 {
            height = max(height, fitted.height)
        }
        model.computedHeight = height
        let width = model.kind == .label
            ? min(maxBody, max(minBody, fitted.width))
            : maxBody
        return CGSize(width: width, height: height)
    }

    private func prepare(width: CGFloat) {
        let textWidth = max(40, width - AppConfig.bubblePadX * 2)
        let screen = NSScreen.main?.frame.height ?? 900
        let summaryCap = max(14, screen / 2)
        let summaryFont = NSFont.systemFont(ofSize: 11, weight: .regular)
        let titleFont = NSFont.systemFont(ofSize: 13, weight: .semibold)
        model.summaryHeight = model.summary.isEmpty
            ? 0
            : min(summaryCap, BubbleMeasure.textHeight(model.summary, width: textWidth, font: summaryFont, lines: 1))
        let title = BubbleMeasure.textHeight(model.title.isEmpty ? model.appName : model.title, width: textWidth - 22, font: titleFont, lines: 1)
        var choices: CGFloat = 0
        if !model.options.isEmpty {
            for option in model.options {
                choices += BubbleMeasure.textHeight(option.label, width: textWidth - 20, font: titleFont, lines: 4) + 16 + 6
            }
        }
        let status: CGFloat = model.status.isEmpty ? 0 : 14
        let preview: CGFloat = model.summary.isEmpty ? 0 : 18
        switch model.kind {
        case .picker:
            model.computedHeight = max(AppConfig.bubbleMinHeight, 28 + AppConfig.bubblePadY * 2)
        case .label:
            model.computedHeight = max(AppConfig.bubbleMinHeight, title + AppConfig.bubblePadY * 2)
        case .card, .alert:
            let gaps: CGFloat = (status > 0 ? 6 : 0) + (preview > 0 ? 6 : 0)
            let body = AppConfig.bubblePadY * 2 + 18 + preview + status + gaps + choices + 8
            model.computedHeight = max(AppConfig.bubbleMinHeight, body)
        }
    }
}

/// Drops the legacy visual-effect backdrop. That view is the solid light-blue fill.
final class BubbleHostingView<Content: View>: NSHostingView<Content> {
    private var strippedVisualEffects = false
    override var isOpaque: Bool { false }

    override func layout() {
        super.layout()
        layer?.isOpaque = false
        layer?.backgroundColor = NSColor.clear.cgColor
        guard !strippedVisualEffects else { return }
        strippedVisualEffects = true
        stripVisualEffects(self)
    }

    private func stripVisualEffects(_ view: NSView) {
        for subview in view.subviews {
            if let material = subview as? NSVisualEffectView {
                material.isHidden = true
                material.removeFromSuperview()
            } else {
                stripVisualEffects(subview)
            }
        }
    }
}
