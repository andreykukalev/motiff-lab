import Foundation

/// How many parts of a recording go to the scan server at once (0014 item 7), as the web app's Pacer
/// (Scripts/import/hearing.ts): it starts at two and goes up by one once as many parts as are allowed came back quickly,
/// up to `most`. A busy answer drops it to one and caps it one below where it was; parts heard in a row lift the cap
/// again. Answers to requests sent before the last change do not count, so one burst of busy answers drops it once.
struct PartPacer: Sendable {
    static let startAtOnce = 2
    /// How often one part is sent before its hearing fails.
    static let mostTries = 6
    private static let slow = 1.6
    private static let raiseAfter = 6

    let most: Int
    private(set) var allowed: Int
    private(set) var ceiling: Int
    /// The mark a request is sent with.
    private(set) var ticket = 0
    private var streak = 0
    private var fastest = Double.infinity

    init(most: Int) {
        self.most = most
        ceiling = max(1, most)
        allowed = min(Self.startAtOnce, ceiling)
    }

    /// A part of `seconds` heard `elapsed` seconds after it was sent with `sent`.
    mutating func heard(_ sent: Int, elapsed: Double, seconds: Double) {
        let pace = elapsed / max(1, seconds)
        let slow = fastest.isFinite && pace > fastest * Self.slow && elapsed > 30
        fastest = min(fastest, pace)
        guard sent == ticket else { return }
        if slow {
            // It waited behind another part: more at once would wait longer still.
            streak = 0
            return
        }
        streak += 1
        if allowed < ceiling && streak >= allowed {
            allowed += 1
            ticket += 1
            streak = 0
        } else if allowed == ceiling && ceiling < most && streak >= Self.raiseAfter {
            ceiling += 1
            streak = 0
        }
    }

    /// A busy answer, a lost connection or a timeout for a request sent with `sent`.
    mutating func failed(_ sent: Int) {
        guard sent == ticket else { return }
        ceiling = max(1, allowed - 1)
        allowed = 1
        ticket += 1
        streak = 0
    }

    /// How long to wait before a part is sent again after its `tries`-th busy answer, in seconds: 4 doubling to 60, and
    /// up to a second more so parts do not all come back at once.
    static func backoff(_ tries: Int) -> Double {
        min(60, 4 * pow(2, Double(max(0, tries - 1)))) + Double.random(in: 0..<1)
    }
}

/// The parts of a recording heard, or why not all of them were.
struct PartsHeard: Sendable {
    var parts: [(index: Int, midi: Data)] = []
    var engine = "unknown engine"
    /// Parts that had no notes to hear.
    var quiet = 0
    var failure: String?
}

/// Sending the parts music-core planned to the scan server, several at once as `PartPacer` allows, a busy part again
/// after a wait, as the web app does (0014 item 7).
@MainActor
enum PartHearing {
    /// What one part's request came to.
    private struct Finished: Sendable {
        let part: RecordingParts.Part
        let ticket: Int
        let elapsed: Double
        let heard: Transcriber.Heard
    }

    /// Hears `parts` with `send`; `progress` is told how many are done of how many, and whether one waits to be sent again.
    static func hear(
        _ parts: [RecordingParts.Part],
        most: Int,
        send: @escaping @Sendable @MainActor (RecordingParts.Part) async -> Transcriber.Heard,
        wait: @escaping @Sendable (Double) async -> Void = { seconds in _ = try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000)) },
        progress: (Int, Int, Bool) -> Void
    ) async -> PartsHeard {
        var result = PartsHeard()
        var pacer = PartPacer(most: most)
        var waiting = parts.map { (part: $0, delay: 0.0) }
        var tries: [Int: Int] = [:]
        var running = 0
        var retrying = 0
        var done = 0
        progress(0, parts.count, false)
        await withTaskGroup(of: Finished.self) { group in
            while result.failure == nil && (!waiting.isEmpty || running > 0) {
                while result.failure == nil && !waiting.isEmpty && running < pacer.allowed {
                    let next = waiting.removeFirst()
                    let ticket = pacer.ticket
                    running += 1
                    if next.delay > 0 { retrying -= 1 }
                    group.addTask {
                        if next.delay > 0 { await wait(next.delay) }
                        // Another part's failure ended the hearing while this one waited: it is not sent.
                        if Task.isCancelled { return Finished(part: next.part, ticket: ticket, elapsed: 0, heard: .failed("")) }
                        let sent = Date()
                        let heard = await send(next.part)
                        return Finished(part: next.part, ticket: ticket, elapsed: Date().timeIntervalSince(sent), heard: heard)
                    }
                }
                guard let finished = await group.next() else { break }
                running -= 1
                let part = finished.part
                switch finished.heard {
                case .notes(let midi, let engine):
                    pacer.heard(finished.ticket, elapsed: finished.elapsed, seconds: Double(part.sendTo - part.sendFrom) / 1000)
                    result.parts.append((part.index, midi))
                    result.engine = engine
                    done += 1
                case .nothing:
                    result.quiet += 1
                    done += 1
                case .busy(let why, let after, let reached):
                    let count = (tries[part.index] ?? 0) + 1
                    // A server never reached is not asked again and again: only one that answered before is.
                    if count < PartPacer.mostTries && (reached || done > 0) {
                        pacer.failed(finished.ticket)
                        tries[part.index] = count
                        retrying += 1
                        waiting.insert((part, after ?? PartPacer.backoff(count)), at: 0)
                    } else {
                        result.failure = why
                    }
                case .failed(let why):
                    result.failure = why
                }
                progress(done, parts.count, retrying > 0)
            }
            group.cancelAll()
        }
        result.parts.sort { $0.index < $1.index }
        return result
    }
}
