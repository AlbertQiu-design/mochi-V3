import Darwin
import Foundation

/// Watches one tasks file. Atomic replace changes the inode, so a delete or rename reopens the path.
/// The change callback runs on a background queue.
public final class TaskFileWatch {
    private let queue = DispatchQueue(label: "mochi.taskwatch")
    private let fileURL: URL
    private var source: DispatchSourceFileSystemObject?
    private var descriptor: CInt = -1
    private var mode: Mode = .directory
    private var debounce: DispatchWorkItem?
    private var onChange: (() -> Void)?

    private enum Mode {
        case file
        case directory
    }

    public init(fileURL: URL) {
        self.fileURL = fileURL
    }

    public func start(onChange: @escaping () -> Void) {
        queue.async { [weak self] in
            guard let self else { return }
            self.onChange = onChange
            self.arm()
        }
    }

    public func stop() {
        queue.sync {
            debounce?.cancel()
            debounce = nil
            onChange = nil
            closeSource()
        }
    }

    public func stamp() -> String {
        var info = stat()
        guard stat(fileURL.path, &info) == 0 else { return "missing" }
        return "\(info.st_ino)-\(info.st_mtimespec.tv_sec).\(info.st_mtimespec.tv_nsec)-\(info.st_size)"
    }

    private func arm() {
        closeSource()
        let directory = fileURL.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let exists = FileManager.default.fileExists(atPath: fileURL.path)
        let fd = open(exists ? fileURL.path : directory.path, O_EVTONLY)
        guard fd >= 0 else { return }
        descriptor = fd
        mode = exists ? .file : .directory
        let watchingFile = exists
        let mask: DispatchSource.FileSystemEvent = watchingFile
            ? [.write, .extend, .delete, .rename, .attrib]
            : [.write]
        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: fd,
            eventMask: mask,
            queue: queue
        )
        source.setEventHandler { [weak self] in
            guard let self else { return }
            if watchingFile {
                let flags = source.data
                if flags.contains(.delete) || flags.contains(.rename) {
                    self.arm()
                }
                self.schedule()
            } else if FileManager.default.fileExists(atPath: self.fileURL.path) {
                self.arm()
                self.schedule()
            }
        }
        source.setCancelHandler {
            if fd >= 0 { close(fd) }
        }
        self.source = source
        source.resume()
    }

    private func schedule() {
        debounce?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            if self.mode == .directory, FileManager.default.fileExists(atPath: self.fileURL.path) {
                self.arm()
            }
            self.onChange?()
        }
        debounce = work
        queue.asyncAfter(deadline: .now() + .milliseconds(40), execute: work)
    }

    private func closeSource() {
        guard let source else {
            if descriptor >= 0 {
                close(descriptor)
                descriptor = -1
            }
            return
        }
        let fd = descriptor
        descriptor = -1
        self.source = nil
        source.setEventHandler {}
        source.setCancelHandler {
            if fd >= 0 { close(fd) }
        }
        source.cancel()
    }
}
