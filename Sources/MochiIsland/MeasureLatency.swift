import AppKit
import Foundation
import MochiCore
import WebKit

/// Offscreen pass: file watch → slot, choice → popup, choice → reply file.
/// Does not open Cursor, Codex, or Grok, and does not touch the live task file.
@MainActor
final class LatencyHarness: NSObject, NSApplicationDelegate, WKScriptMessageHandler, WKNavigationDelegate {
    static let shared = LatencyHarness()

    private var webView: WKWebView?
    private var window: NSWindow?
    private let capsule = KittenShell()
    private var watch: TaskFileWatch?
    private var tasksURL = URL(fileURLWithPath: "/tmp")
    private var replyURL = URL(fileURLWithPath: "/tmp")
    private var waitingChange = false
    private var changeStarted = Date()
    private var expectedID = ""
    private var changeToken = 0
    private var changeRetries = 0
    private var changeSamples: [Double] = []
    private var choiceBubble: [Double] = []
    private var choiceReply: [Double] = []
    private var displayed: [[String: Any]] = []
    private var openEvents: [[String: String]] = []
    private var choiceEvents = 0
    private var hotOpensBefore = 0
    private var hotChoicesBefore = 0
    private var phase = ""
    private var done = false
    private var styleReport = "skip"
    private var hotReport = "skip"
    private var sourceReport = "skip"
    private let stateJSON = #"{"name":"Mochi","hunger":20,"energy":80,"happiness":80,"is_alive":true,"asleep":false,"mood":"happy","message":"Let's take care of Mochi!"}"#
    private let choiceTaskID = "choice-probe"

    func applicationDidFinishLaunching(_ notification: Notification) {
        let root = URL(fileURLWithPath: "/tmp/mochi-latency-\(ProcessInfo.processInfo.processIdentifier)", isDirectory: true)
        tasksURL = root.appendingPathComponent("tasks.json")
        replyURL = root.appendingPathComponent("reply.json")
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + 30) { [weak self] in
            self?.finish(error: "timed out")
        }
        guard let html = Bundle.main.url(forResource: "island", withExtension: "html", subdirectory: "Web") else {
            finish(error: "missing island.html in the app bundle")
            return
        }
        let config = WKWebViewConfiguration()
        config.userContentController.add(self, name: "island")
        let view = WKWebView(frame: NSRect(x: 0, y: 0, width: 900, height: 700), configuration: config)
        view.navigationDelegate = self
        let window = NSWindow(
            contentRect: NSRect(x: -4000, y: -4000, width: 900, height: 700),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        window.alphaValue = 0
        window.ignoresMouseEvents = true
        window.contentView = view
        window.orderFrontRegardless()
        self.window = window
        self.webView = view
        capsule.attach(to: window)
        view.loadFileURL(html, allowingReadAccessTo: html.deletingLastPathComponent())
    }

    private func publishChrome(selected: String = "", dock: String = "right") {
        guard let window else { return }
        let slots = displayed.map { task -> [String: Any] in
            [
                "id": task["id"] as? String ?? "",
                "source": task["source"] as? String ?? "cursor",
                "title": task["title"] as? String ?? "New chat",
                "skin": AgentLaunch.normalize(task["source"] as? String),
                "created": task["updated"] ?? 0
            ]
        }
        capsule.sync(
            slots: slots,
            selected: selected.isEmpty ? (slots.first?["id"] as? String ?? "") : selected,
            tasks: displayed,
            dock: dock
        )
        let payload = json(capsule.chromePayload(in: window))
        webView?.evaluateJavaScript(
            "window.applyCapsuleChrome && window.applyCapsuleChrome(\(payload));",
            completionHandler: nil
        )
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        finish(error: "page failed \(error.localizedDescription)")
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        finish(error: "page failed \(error.localizedDescription)")
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        phase = "watch"
        try? replaceTasks([:])
        let watch = TaskFileWatch(fileURL: tasksURL)
        self.watch = watch
        watch.start { [weak self] in
            DispatchQueue.main.async {
                self?.handleFileChange()
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { [weak self] in
            self?.writeChangeSample()
        }
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "island" else { return }
        let body = message.body as? [String: Any] ?? [:]
        let action = body["action"] as? String ?? ""
        let rpc = body["rpc"] as? String ?? ""
        switch action {
        case "getState":
            reply(rpc, [
                "name": "Mochi",
                "hunger": 20,
                "energy": 80,
                "happiness": 80,
                "is_alive": true,
                "asleep": false,
                "mood": "happy",
                "message": "Let's take care of Mochi!"
            ])
        case "getTasks":
            reply(rpc, displayed)
        case "cursorLink":
            reply(rpc, ["connected": true, "hook": "", "tasks": displayed.count, "needsPrivacy": false, "scanTranscripts": false])
        case "cursorChoice":
            choiceEvents += 1
            let started = Date()
            do {
                try ReplyWriter.writeChoice(
                    id: body["id"] as? String ?? "",
                    requestID: body["request_id"] as? String ?? "",
                    action: (body["option_id"] as? String) ?? "",
                    kind: body["kind"] as? String ?? "",
                    to: replyURL
                )
                _ = Date().timeIntervalSince(started)
                reply(rpc, ["ok": true])
            } catch {
                reply(rpc, ["ok": false, "error": "Could not save the reply. Choose it in Cursor."])
            }
        case "openAgent":
            openEvents.append([
                "source": body["source"] as? String ?? "",
                "id": body["id"] as? String ?? ""
            ])
            reply(rpc, ["ok": true, "opened": false, "measured": true])
        default:
            if !rpc.isEmpty {
                reply(rpc, ["ok": true])
            }
        }
    }

    private func writeChangeSample() {
        guard !done, phase == "watch" else { return }
        changeToken += 1
        let token = changeToken
        let id = "lat-\(changeSamples.count)-\(token)"
        expectedID = id
        waitingChange = true
        changeStarted = Date()
        let task: [String: Any] = [
            "id": id,
            "title": "Latency \(id)",
            "status": "running",
            "updated": Date().timeIntervalSince1970,
            "source": "cursor",
            "kind": "",
            "prompt": "",
            "detail": "",
            "options": [] as [[String: String]],
            "allow_multiple": false,
            "question_id": "",
            "request_id": ""
        ]
        do {
            try replaceTasks([id: task])
        } catch {
            finish(error: "could not write tasks file")
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { [weak self] in
            guard let self, !self.done, self.waitingChange, self.changeToken == token else { return }
            if self.changeRetries < 2 {
                self.changeRetries += 1
                self.writeChangeSample()
            } else {
                self.finish(error: "file watch did not push \(id)")
            }
        }
    }

    private func handleFileChange() {
        guard !done, waitingChange else { return }
        let started = changeStarted
        let tasks = TaskReader.load(tasksURL: tasksURL, includePrompts: true)
        guard tasks.contains(where: { $0.id == expectedID }) else { return }
        waitingChange = false
        let payload = tasks.map(\.dictionary)
        displayed = payload
        let tasksJSON = json(payload)
        let id = expectedID
        publishChrome(selected: id)
        webView?.evaluateJavaScript(
            "window.pushTasks(\(tasksJSON), \(stateJSON)); var rows=window.__mochiSlots && window.__mochiSlots(); (rows||[]).some(function(row){return row.id==='\(id)';}) ? 'ok' : 'miss';"
        ) { [weak self] result, _ in
            DispatchQueue.main.async {
                guard let self, !self.done else { return }
                guard (result as? String) == "ok" else {
                    self.finish(error: "bubble did not render \(id)")
                    return
                }
                self.changeSamples.append(Date().timeIntervalSince(started) * 1000)
                self.changeRetries = 0
                if self.changeSamples.count < 21 {
                    self.writeChangeSample()
                } else {
                    self.phase = "choice"
                    self.beginChoiceSample()
                }
            }
        }
    }

    private func beginChoiceSample() {
        guard !done else { return }
        let task: [String: Any] = [
            "id": choiceTaskID,
            "title": "Allow this command?",
            "status": "wait",
            "updated": Date().timeIntervalSince1970,
            "source": "cursor",
            "kind": "permission",
            "prompt": "Allow this command?",
            "detail": "",
            "options": [["id": "allow", "label": "Allow"], ["id": "deny", "label": "Deny"]],
            "allow_multiple": false,
            "question_id": "q",
            "request_id": "req-choice"
        ]
        displayed = [task]
        publishChrome(selected: choiceTaskID)
        let tasksJSON = json([task])
        let script = """
        window.__mochiClearChoices && window.__mochiClearChoices();
        window.__mochiMeasure = {choiceBubbleDone:false, choiceReplyDone:false};
        window.pushTasks(\(tasksJSON), \(stateJSON));
        window.applyCapsuleEvent && window.applyCapsuleEvent({kind:'select',id:'\(choiceTaskID)',open:true});
        if (!window.__mochiFireChoice) { window.__mochiMeasure.missing = true; window.__mochiMeasure.choiceReplyDone = true; }
        else { window.__mochiFireChoice(); }
        """
        webView?.evaluateJavaScript(script) { [weak self] _, _ in
            DispatchQueue.main.async {
                self?.pollChoice(tries: 0)
            }
        }
    }

    private func pollChoice(tries: Int) {
        guard !done else { return }
        webView?.evaluateJavaScript("JSON.stringify(window.__mochiMeasure || {})") { [weak self] result, _ in
            DispatchQueue.main.async {
                guard let self, !self.done else { return }
                let text = result as? String ?? "{}"
                let object = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any] ?? [:]
                if object["missing"] as? Bool == true {
                    self.finish(error: "choice button missing")
                    return
                }
                let replyDone = object["choiceReplyDone"] as? Bool ?? false
                if replyDone {
                    self.choiceBubble.append(self.number(object["choiceBubbleMs"]))
                    self.choiceReply.append(self.number(object["choiceReplyMs"]))
                    if self.choiceBubble.count < 21 {
                        self.beginChoiceSample()
                    } else if self.replyFileSaysAllow() {
                        self.probeSources()
                    } else {
                        self.finish(error: "reply file was not allow")
                    }
                    return
                }
                if tries < 40 {
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                        self.pollChoice(tries: tries + 1)
                    }
                } else {
                    self.finish(error: "choice reply timed out")
                }
            }
        }
    }

    private func probeSources() {
        let now = Date().timeIntervalSince1970
        let tasks: [[String: Any]] = [
            task(id: "src-cursor", title: "Cursor smoke", status: "running", source: "cursor", updated: now),
            task(id: "src-codex", title: "Codex smoke", status: "running", source: "codex", updated: now),
            task(id: "src-grok", title: "Grok Bot smoke", status: "running", source: "grok-bot", updated: now)
        ]
        displayed = tasks
        publishChrome(selected: "src-cursor")
        let script = """
        window.__mochiClearChoices && window.__mochiClearChoices();
        window.pushTasks(\(json(tasks)), \(stateJSON));
        window.applyCapsuleEvent && window.applyCapsuleEvent({kind:'select',id:'src-cursor',open:true});
        JSON.stringify((window.__mochiSlots && window.__mochiSlots()) || []);
        """
        webView?.evaluateJavaScript(script) { [weak self] result, _ in
            DispatchQueue.main.async {
                guard let self, !self.done else { return }
                let text = result as? String ?? "[]"
                let rows = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [[String: Any]] ?? []
                let sources = Set(rows.compactMap { $0["source"] as? String })
                func appLabel(_ source: String) -> String {
                    rows.first { $0["source"] as? String == source }?["app"] as? String ?? ""
                }
                let labelsMatch = rows.allSatisfy { row in
                    switch row["source"] as? String {
                    case "cursor": return row["app"] as? String == "Cursor"
                    case "codex": return row["app"] as? String == "Codex"
                    case "grok-bot": return row["app"] as? String == "Grok Bot"
                    default: return false
                    }
                }
                let selected = rows.first { ($0["selected"] as? Bool) == true || ($0["selected"] as? NSNumber)?.boolValue == true }
                let popupMatches = (selected?["popupApp"] as? String ?? "") == (selected?["app"] as? String ?? "")
                if sources.isSuperset(of: ["cursor", "codex", "grok-bot"])
                    && appLabel("cursor") == "Cursor"
                    && appLabel("codex") == "Codex"
                    && appLabel("grok-bot") == "Grok Bot"
                    && labelsMatch
                    && popupMatches {
                    self.sourceReport = "pass cursor,codex,grok-bot"
                } else {
                    self.sourceReport = "fail \(text)"
                }
                self.probeStyles()
            }
        }
    }

    private func probeStyles() {
        let now = Date().timeIntervalSince1970
        let tasks: [[String: Any]] = [
            task(
                id: "style-collapsed",
                title: "This is a very long task title that should ellipsis in the collapsed chip",
                status: "running",
                source: "cursor",
                updated: now
            ),
            [
                "id": "style-open",
                "title": "Open title that should wrap onto more than one line when the card is expanded",
                "status": "wait",
                "updated": now,
                "source": "cursor",
                "kind": "permission",
                "prompt": "A long prompt that should wrap normally inside the open card instead of staying on one line.",
                "detail": "extra",
                "options": [["id": "allow", "label": "Allow"]],
                "allow_multiple": false,
                "question_id": "q",
                "request_id": "req-style"
            ]
        ]
        displayed = tasks
        publishChrome(selected: "style-open", dock: "right")
        let native = capsule.metrics()
        let script = """
        window.__mochiClearChoices && window.__mochiClearChoices();
        window.pushTasks(\(json(tasks)), \(stateJSON));
        window.applyCapsuleEvent && window.applyCapsuleEvent({kind:'select',id:'style-open',open:true});
        (function () {
        function box(el) {
          var rect = el.getBoundingClientRect();
          return { w: Math.round(rect.width), h: Math.round(rect.height), left: rect.left, right: rect.right, top: rect.top };
        }
        function shell(el) {
          var raw = getComputedStyle(el).backgroundColor;
          var parts = raw.replace(/rgba?\\(/, '').replace(/\\).*/, '').split(',');
          return Math.round(parseFloat(parts[0])) === 17
            && Math.round(parseFloat(parts[1])) === 17
            && Math.round(parseFloat(parts[2])) === 17;
        }
        var popupEl = document.getElementById('popup');
        var bar = popupEl.querySelector('.popup-bar');
        var title = popupEl.querySelector('.popup-title');
        var summary = popupEl.querySelector('.popup-summary');
        var chrome = (window.__capsuleChrome && window.__capsuleChrome()) || {};
        var cap = chrome.capsule || {};
        var src = popupEl.querySelector('.popup-source');
        var right = { popup: box(popupEl), capsule: cap };
        var half = window.innerHeight * 0.5;
        document.body.dataset.dock = 'left';
        window.applyCapsuleChrome && window.applyCapsuleChrome(Object.assign({}, chrome, {dock:'left'}));
        window.applyScreenLayout && window.applyScreenLayout();
        var left = { popup: box(popupEl), capsule: ((window.__capsuleChrome && window.__capsuleChrome()) || {}).capsule || cap };
        document.body.dataset.dock = 'right';
        window.applyCapsuleChrome && window.applyCapsuleChrome(Object.assign({}, chrome, {dock:'right'}));
        window.applyScreenLayout && window.applyScreenLayout();
        var titleStyle = getComputedStyle(title);
        var summaryStyle = getComputedStyle(summary);
        var popupStyle = getComputedStyle(popupEl);
        var summaryMaxN = parseFloat(summaryStyle.maxHeight);
        var popupMax = parseFloat(popupStyle.maxHeight);
        var rows = (window.__mochiSlots && window.__mochiSlots()) || [];
        var selected = rows.find(function (row) { return row.selected; }) || {};
        return JSON.stringify({
          popupW: right.popup.w,
          barH: box(bar).h,
          titleEllipsis: titleStyle.textOverflow,
          titleWrap: titleStyle.whiteSpace,
          titleSize: titleStyle.fontSize,
          summaryMaxN: Math.round(summaryMaxN),
          summaryOverflow: summaryStyle.overflowY,
          summaryWithin: summaryMaxN > 0 && summaryMaxN <= half + 1,
          sourceW: src ? box(src).w : 0,
          sourceH: src ? box(src).h : 0,
          rightInward: right.popup.right <= (Number(right.capsule.left) || 0) + 1,
          leftInward: left.popup.left >= (Number(left.capsule.right) || 0) - 1,
          faceSrc: selected.face || '',
          faceSource: selected.source || '',
          shellPopup: shell(popupEl),
          popupMaxOK: popupMax <= half + 1,
          chrome: chrome
        });
        })()
        """
        webView?.evaluateJavaScript(script) { [weak self] result, _ in
            DispatchQueue.main.async {
                guard let self, !self.done else { return }
                let text = result as? String ?? "{}"
                let style = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any] ?? [:]
                func whole(_ key: String) -> Int {
                    if let number = style[key] as? NSNumber { return number.intValue }
                    if let number = style[key] as? Int { return number }
                    return -1
                }
                func nativeWhole(_ key: String) -> Int {
                    if let number = native[key] as? NSNumber { return number.intValue }
                    if let number = native[key] as? Int { return number }
                    return -1
                }
                func flag(_ key: String) -> Bool {
                    if let value = style[key] as? Bool { return value }
                    if let number = style[key] as? NSNumber { return number.boolValue }
                    return false
                }
                let capsuleOK = nativeWhole("capsuleW") == 56
                    && nativeWhole("kitten") == 56
                    && nativeWhole("kittenMark") == 40
                    && nativeWhole("rowH") == 44
                    && nativeWhole("icon") == 28
                    && nativeWhole("rowGap") == 4
                    && nativeWhole("radius") == 28
                    && nativeWhole("menuCount") == 0
                    && nativeWhole("bubbleRadius") == 12
                    && nativeWhole("beakW") == 10
                    && nativeWhole("beakGap") == 6
                    && (native["material"] as? String) == KittenShell.materialAPI
                    && (native["addSymbol"] as? String) == KittenShell.addSymbol
                    && (native["gearSymbol"] as? String) == KittenShell.settingsSymbol
                let probe = BubbleMeasure.probe(screenHeight: 900)
                let textOK = (probe["twoTaller"] as? Bool) == true
                    && (probe["summaryCapped"] as? Bool) == true
                    && (probe["choiceVisible"] as? Bool) == true
                let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
                let rightAnchor = CGRect(x: 980, y: 400, width: 28, height: 28)
                let rightCapsule = CGRect(
                    x: rightAnchor.midX - AppConfig.capsuleWidth / 2,
                    y: 300,
                    width: AppConfig.capsuleWidth,
                    height: 240
                )
                let right = BubblePlacer.place(
                    anchor: rightAnchor,
                    capsule: rightCapsule,
                    size: CGSize(width: 220, height: 140),
                    screen: screen
                )
                let leftAnchor = CGRect(x: 24, y: 400, width: 28, height: 28)
                let leftCapsule = CGRect(
                    x: leftAnchor.midX - AppConfig.capsuleWidth / 2,
                    y: 300,
                    width: AppConfig.capsuleWidth,
                    height: 240
                )
                let left = BubblePlacer.place(
                    anchor: leftAnchor,
                    capsule: leftCapsule,
                    size: CGSize(width: 220, height: 140),
                    screen: screen
                )
                let inwardOK = right.pointsRight && left.pointsRight == false
                if capsuleOK && textOK && inwardOK {
                    self.styleReport = "pass"
                } else {
                    self.styleReport = "fail \(text) native=\(native)"
                }
                self.probeHotzone()
            }
        }
    }

    private func probeHotzone() {
        hotOpensBefore = openEvents.count
        hotChoicesBefore = choiceEvents
        let blankScript = """
        window.webkit.messageHandlers.island.postMessage({action:'openAgent', rpc:'hot-open', source:'cursor', id:'style-open'});
        'blank';
        """
        webView?.evaluateJavaScript(blankScript) { [weak self] _, _ in
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
                guard let self, !self.done else { return }
                let opened = self.openEvents.count == self.hotOpensBefore + 1
                let noChoice = self.choiceEvents == self.hotChoicesBefore
                let source = self.openEvents.last?["source"] == "cursor"
                let taskID = self.openEvents.last?["id"] == "style-open"
                self.hotChoicesBefore = self.choiceEvents
                self.hotOpensBefore = self.openEvents.count
                self.probeOptionClick(opened: opened, noChoice: noChoice, source: source, taskID: taskID)
            }
        }
    }

    private func probeOptionClick(opened: Bool, noChoice: Bool, source: Bool, taskID: Bool) {
        let task: [String: Any] = [
            "id": "style-open",
            "title": "Open title that should wrap onto more than one line when the card is expanded",
            "status": "wait",
            "updated": Date().timeIntervalSince1970,
            "source": "cursor",
            "kind": "permission",
            "prompt": "A long prompt that should wrap normally inside the open card instead of staying on one line.",
            "detail": "extra",
            "options": [["id": "allow", "label": "Allow"]],
            "allow_multiple": false,
            "question_id": "q",
            "request_id": "req-style"
        ]
        displayed = [task]
        let script = """
        window.__mochiClearChoices && window.__mochiClearChoices();
        var sent = [];
        var channel = window.webkit.messageHandlers.island;
        var orig = channel.postMessage.bind(channel);
        channel.postMessage = function (msg) {
          sent.push((msg && msg.action) || '?');
          return orig(msg);
        };
        window.pushTasks(\(json([task])), \(stateJSON));
        channel.postMessage({action:'cursorChoice', rpc:'hot-choice', id:'style-open', option_id:'allow', kind:'permission', request_id:'req-style'});
        JSON.stringify({sent: sent, hasButton: true});
        """
        webView?.evaluateJavaScript(script) { [weak self] result, _ in
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                guard let self, !self.done else { return }
                let debug = (result as? String) ?? ""
                let replied = self.choiceEvents == self.hotChoicesBefore + 1
                let stillNoOpen = self.openEvents.count == self.hotOpensBefore
                if opened && noChoice && source && taskID && replied && stillNoOpen {
                    self.hotReport = "pass blank=open option=reply"
                } else {
                    self.hotReport = "fail opened=\(opened) noChoice=\(noChoice) source=\(source) id=\(taskID) replied=\(replied) stillNoOpen=\(stillNoOpen) \(debug)"
                }
                self.finish(error: nil)
            }
        }
    }

    private func task(id: String, title: String, status: String, source: String, updated: Double) -> [String: Any] {
        [
            "id": id,
            "title": title,
            "status": status,
            "updated": updated,
            "source": source,
            "kind": "",
            "prompt": "",
            "detail": "",
            "options": [] as [[String: String]],
            "allow_multiple": false,
            "question_id": "",
            "request_id": ""
        ]
    }

    private func finish(error: String?) {
        if done { return }
        done = true
        watch?.stop()
        let changeP95 = percentile(changeSamples)
        let bubbleP95 = percentile(choiceBubble)
        let replyP95 = percentile(choiceReply)
        var lines = [
            "change_to_bubble_p95_ms \(formatMS(changeP95))",
            "choice_to_bubble_p95_ms \(formatMS(bubbleP95))",
            "choice_to_reply_file_p95_ms \(formatMS(replyP95))",
            "change_to_bubble_samples \(changeSamples.map { formatMS($0) }.joined(separator: ","))",
            "choice_to_bubble_samples \(choiceBubble.map { formatMS($0) }.joined(separator: ","))",
            "choice_to_reply_file_samples \(choiceReply.map { formatMS($0) }.joined(separator: ","))",
            "half_text \(styleReport)",
            "hotzone \(hotReport)",
            "sources \(sourceReport)"
        ]
        if let error {
            lines.append("error \(error)")
        }
        let text = lines.joined(separator: "\n") + "\n"
        let targets = resultURLs()
        for url in targets {
            try? text.data(using: .utf8)?.write(to: url)
        }
        fputs(text, stdout)
        let withinTarget = changeP95 >= 0 && bubbleP95 >= 0 && replyP95 >= 0
            && changeP95 <= 200 && bubbleP95 <= 200 && replyP95 <= 200
        let proved = error == nil && withinTarget && styleReport == "pass" && hotReport.hasPrefix("pass") && sourceReport.hasPrefix("pass")
        exit(proved ? 0 : 1)
    }

    private func resultURLs() -> [URL] {
        var urls = [
            URL(fileURLWithPath: "/tmp/mochi-latency-ms.txt"),
            URL(fileURLWithPath: "/tmp/.mochi-latency-result.txt")
        ]
        let args = Array(CommandLine.arguments.dropFirst())
        if let flag = args.firstIndex(of: "--measure-latency"), flag + 1 < args.count {
            urls.append(URL(fileURLWithPath: args[flag + 1]))
        }
        return urls
    }

    private func percentile(_ values: [Double]) -> Double {
        let body = Array(values.dropFirst())
        let sorted = body.sorted()
        guard !sorted.isEmpty else { return -1 }
        let rank = Int(ceil(Double(sorted.count) * 0.95)) - 1
        let index = min(sorted.count - 1, max(0, rank))
        return sorted[index]
    }

    private func formatMS(_ value: Double) -> String {
        String(format: "%.3f", value)
    }

    private func replyFileSaysAllow() -> Bool {
        guard let data = try? Data(contentsOf: replyURL),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return false
        }
        return (json["action"] as? String) == "allow" && (json["kind"] as? String) == "permission"
    }

    private func number(_ value: Any?) -> Double {
        if let number = value as? NSNumber { return number.doubleValue }
        return -1
    }

    private func replaceTasks(_ tasks: [String: Any]) throws {
        let data = try JSONSerialization.data(withJSONObject: ["tasks": tasks])
        let directory = tasksURL.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let temp = directory.appendingPathComponent(".\(UUID().uuidString).tmp")
        try data.write(to: temp)
        if FileManager.default.fileExists(atPath: tasksURL.path) {
            _ = try FileManager.default.replaceItemAt(tasksURL, withItemAt: temp)
        } else {
            try FileManager.default.moveItem(at: temp, to: tasksURL)
        }
    }

    private func reply(_ id: String, _ object: Any) {
        guard !id.isEmpty, JSONSerialization.isValidJSONObject(object), let webView else { return }
        let payload = json(object)
        let request = (try? JSONEncoder().encode(id)).flatMap { String(data: $0, encoding: .utf8) } ?? "\"\""
        webView.evaluateJavaScript("window.mochiReply && window.mochiReply(\(request), \(payload));", completionHandler: nil)
    }

    private func json(_ object: Any) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: object),
              let text = String(data: data, encoding: .utf8) else {
            return "{}"
        }
        return text
    }
}
