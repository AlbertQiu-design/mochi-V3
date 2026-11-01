import Foundation

/// Turns on Codex's `features.hooks` flag without rewriting a file Codex would reject.
/// A document that already has both a `[features]` table and a root `features.hooks` key is left untouched.
public enum CodexFeatureFlag {
    public static func enablingHooks(in contents: String) -> String? {
        let newline = contents.contains("\r\n") ? "\r\n" : "\n"
        var lines = contents.split(omittingEmptySubsequences: false, whereSeparator: \.isNewline).map(String.init)
        if contents.hasSuffix("\n") || contents.hasSuffix("\r\n"), lines.last == "" {
            lines.removeLast()
        }
        var featuresHeader: Int?
        var featuresHooks: Int?
        var rootDotted: Int?
        var inFeatures = false
        var insideTable = false
        for index in lines.indices {
            let trimmed = lines[index].trimmingCharacters(in: .whitespaces)
            if trimmed.isEmpty || trimmed.hasPrefix("#") { continue }
            if trimmed.hasPrefix("[") {
                inFeatures = isFeaturesHeader(trimmed)
                insideTable = true
                if inFeatures {
                    if featuresHeader != nil { return nil }
                    featuresHeader = index
                }
                continue
            }
            guard let assignment = assignment(in: lines[index]) else { continue }
            if inFeatures {
                guard assignment.key == "hooks" else { continue }
                if featuresHooks != nil { return nil }
                guard assignment.value == "true" || assignment.value == "false" else { return nil }
                featuresHooks = index
                continue
            }
            if !insideTable && (assignment.key == "features.hooks" || assignment.key == "features.codex_hooks") {
                if rootDotted != nil { return nil }
                guard assignment.value == "true" || assignment.value == "false" else { return nil }
                rootDotted = index
            }
        }
        if featuresHeader != nil && rootDotted != nil { return nil }

        if let line = featuresHooks {
            lines[line] = replacingValue(in: lines[line], with: "true")
            return joined(lines, newline: newline, terminated: true)
        }
        if let line = rootDotted {
            let key = assignment(in: lines[line])?.key
            if key == "features.codex_hooks" {
                lines[line] = replacingKey(in: lines[line], with: "features.hooks", value: "true")
            } else {
                lines[line] = replacingValue(in: lines[line], with: "true")
            }
            return joined(lines, newline: newline, terminated: true)
        }
        if let header = featuresHeader {
            lines.insert("hooks = true", at: header + 1)
            return joined(lines, newline: newline, terminated: true)
        }
        var text = contents
        if !text.isEmpty && !text.hasSuffix("\n") { text += newline }
        if !text.isEmpty { text += newline }
        text += "[features]" + newline + "hooks = true" + newline
        return text
    }

    /// True only when the file already turns the hooks feature on and Codex can load it.
    public static func isEnabled(in contents: String) -> Bool {
        var inFeatures = false
        var insideTable = false
        var tableValue: String?
        var rootValue: String?
        var sawTable = false
        var sawRoot = false
        for raw in contents.split(omittingEmptySubsequences: false, whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            if line.hasPrefix("[") {
                inFeatures = isFeaturesHeader(line)
                insideTable = true
                if inFeatures { sawTable = true }
                continue
            }
            guard let assignment = assignment(in: String(raw)) else { continue }
            if inFeatures, assignment.key == "hooks" {
                tableValue = assignment.value
            } else if !insideTable, assignment.key == "features.hooks" || assignment.key == "features.codex_hooks" {
                rootValue = assignment.value
                sawRoot = true
            }
        }
        if sawTable && sawRoot { return false }
        return tableValue == "true" || rootValue == "true"
    }

    private static func isFeaturesHeader(_ line: String) -> Bool {
        var text = line.trimmingCharacters(in: .whitespaces)
        guard text.hasPrefix("["), !text.hasPrefix("[[") else { return false }
        text.removeFirst()
        guard let end = text.firstIndex(of: "]") else { return false }
        let name = text[..<end].trimmingCharacters(in: .whitespaces)
        return name == "features"
    }

    private static func assignment(in line: String) -> (key: String, value: String)? {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        guard let eq = trimmed.firstIndex(of: "=") else { return nil }
        let key = trimmed[..<eq].trimmingCharacters(in: .whitespaces)
        var value = trimmed[trimmed.index(after: eq)...].trimmingCharacters(in: .whitespaces)
        if let hash = value.firstIndex(of: "#") {
            value = value[..<hash].trimmingCharacters(in: .whitespaces)
        }
        guard !key.isEmpty, !value.isEmpty else { return nil }
        return (key, value)
    }

    private static func replacingValue(in line: String, with value: String) -> String {
        guard let eq = line.firstIndex(of: "=") else { return line }
        let head = line[..<line.index(after: eq)]
        let rest = line[line.index(after: eq)...]
        let spaces = rest.prefix { $0 == " " || $0 == "\t" }
        let body = rest.dropFirst(spaces.count)
        let token = body.hasPrefix("true") ? "true" : (body.hasPrefix("false") ? "false" : nil)
        guard let token else { return line }
        let tail = body.dropFirst(token.count)
        return String(head) + String(spaces) + value + String(tail)
    }

    private static func replacingKey(in line: String, with key: String, value: String) -> String {
        guard line.contains("=") else { return line }
        let indent = line.prefix { $0 == " " || $0 == "\t" }
        let tail = replacingValue(in: line, with: value)
        guard let tailEq = tail.firstIndex(of: "=") else { return line }
        return String(indent) + key + String(tail[tailEq...])
    }

    private static func joined(_ lines: [String], newline: String, terminated: Bool) -> String {
        var text = lines.joined(separator: newline)
        if terminated && !text.hasSuffix("\n") { text += newline }
        return text
    }
}
