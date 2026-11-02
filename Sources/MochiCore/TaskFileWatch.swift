import Darwin
import Foundation

/// Watches one tasks file. Atomic replace changes the inode, so the parent directory
/// is watched as well as the file. Unrelated writes in that directory do not notify.
/// The change callback runs on a background queue.
public final class TaskFileWatch {
    private let queue = DispatchQueue(label: "mochi.taskwatch")
    private let fileURL: URL
    private var fileSource: DispatchSourceFileSystemObject?
    private var directorySource: DispatchSourceFileSystemObject?
    private var fileDescriptor: CInt = -1
    private var directoryDescriptor: CInt = -1
    private var armedInode: UInt64 = 0
    private var lastStamp = ""
    private var debounce: DispatchWorkItem?
    private var onChange: (() -> Void)?

    /// One atomic replace emits several notes. Wait one frame so the new inode is linked.
    private static let coalesce: DispatchTimeInterval = .milliseconds(16)

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
            closeFile()
            closeDirectory()
        }
    }

    public func stamp() -> String {
        var info = stat()
        guard stat(fileURL.path, &info) == 0 else { return "missing" }
        return "\(info.st_ino)-\(info.st_mtimespec.tv_sec).\(info.st_mtimespec.tv_nsec)-\(info.st_size)"
    }

    private func arm() {
        closeFile()
        closeDirectory()
        let directory = fileURL.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let directoryFD = open(directory.path, O_EVTONLY)
        if directoryFD >= 0 {
            directoryDescriptor = directoryFD
            let source = DispatchSource.makeFileSystemObjectSource(
                fileDescriptor: directoryFD,
                eventMask: [.write, .rename, .delete, .attrib],
                queue: queue
            )
            source.setEventHandler { [weak self] in
                self?.noteDirectory()
            }
            source.setCancelHandler {
                if directoryFD >= 0 { close(directoryFD) }
            }
            directorySource = source
            source.resume()
        }
        reopenFile()
        lastStamp = stamp()
    }

    /// Directory notes also fire for every other file next to the tasks file.
    /// Only a new tasks-file stamp is worth a refresh.
    private func noteDirectory() {
        let stamp = self.stamp()
        guard stamp != lastStamp else { return }
        schedule()
    }

    private func reopenFile() {
        closeFile()
        let fd = open(fileURL.path, O_EVTONLY)
        guard fd >= 0 else {
            armedInode = 0
            return
        }
        fileDescriptor = fd
        var info = stat()
        if fstat(fd, &info) == 0 {
            armedInode = UInt64(info.st_ino)
        }
        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: fd,
            eventMask: [.write, .extend, .delete, .rename, .attrib],
            queue: queue
        )
        source.setEventHandler { [weak self] in
            self?.schedule()
        }
        source.setCancelHandler {
            if fd >= 0 { close(fd) }
        }
        fileSource = source
        source.resume()
    }

    private func reopenFileIfInodeChanged() {
        var info = stat()
        let exists = stat(fileURL.path, &info) == 0
        let inode = exists ? UInt64(info.st_ino) : 0
        let watching = fileSource != nil
        guard inode != armedInode || exists != watching else { return }
        reopenFile()
    }

    private func schedule() {
        debounce?.cancel()
        let work = DispatchWorkItem { [weak self] in
            self?.publishIfStampChanged(retryIfMissing: true)
        }
        debounce = work
        queue.asyncAfter(deadline: .now() + Self.coalesce, execute: work)
    }

    private func publishIfStampChanged(retryIfMissing: Bool) {
        guard onChange != nil else { return }
        reopenFileIfInodeChanged()
        let stamp = self.stamp()
        if retryIfMissing, stamp == "missing", !lastStamp.isEmpty, lastStamp != "missing" {
            queue.asyncAfter(deadline: .now() + Self.coalesce) { [weak self] in
                self?.publishIfStampChanged(retryIfMissing: false)
            }
            return
        }
        guard stamp != lastStamp else { return }
        lastStamp = stamp
        onChange?()
    }

    private func closeFile() {
        guard let source = fileSource else {
            if fileDescriptor >= 0 {
                close(fileDescriptor)
                fileDescriptor = -1
            }
            armedInode = 0
            return
        }
        let fd = fileDescriptor
        fileDescriptor = -1
        armedInode = 0
        fileSource = nil
        source.setEventHandler {}
        source.setCancelHandler {
            if fd >= 0 { close(fd) }
        }
        source.cancel()
    }

    private func closeDirectory() {
        guard let source = directorySource else {
            if directoryDescriptor >= 0 {
                close(directoryDescriptor)
                directoryDescriptor = -1
            }
            return
        }
        let fd = directoryDescriptor
        directoryDescriptor = -1
        directorySource = nil
        source.setEventHandler {}
        source.setCancelHandler {
            if fd >= 0 { close(fd) }
        }
        source.cancel()
    }
}
