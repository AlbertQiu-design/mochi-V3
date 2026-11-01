import Foundation

public final class PetStore {
    public let directory: URL
    public let fileURL: URL

    public init(directory: URL) {
        self.directory = directory
        self.fileURL = directory.appendingPathComponent("pet_state.json")
    }

    public static func live() -> PetStore {
        PetStore(directory: AppConfig.supportDirectory)
    }

    public func load(migrating legacyURLs: [URL] = []) -> PetState {
        if let state = read(fileURL) {
            return state
        }
        for legacy in legacyURLs where legacy != fileURL {
            if let state = read(legacy) {
                save(state)
                return state
            }
        }
        let fresh = PetState()
        save(fresh)
        return fresh
    }

    public func save(_ state: PetState) {
        var next = state
        next.clamp()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(next) else { return }
        try? AtomicFile.write(data, to: fileURL)
    }

    private func read(_ url: URL) -> PetState? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(PetState.self, from: data)
    }
}
