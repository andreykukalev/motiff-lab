import AVFoundation
import SwiftUI

/// What the render thread and the screen share: the tempo, the beats to the bar, and the beat sounding now.
private final class ClickClock: @unchecked Sendable {
    let lock = NSLock()
    var bpm = 80.0
    var beats = 4
    var running = false
    /// Frames into the beat sounding now, and that beat counted from the start.
    var phase = 0.0
    var count = 0

    var beat: Int {
        lock.lock()
        defer { lock.unlock() }
        return running ? count % max(1, beats) : -1
    }
}

/// The PDF view's metronome (0023, the web app's `Scripts/metronome/metronome.ts`): it clicks with the playback engine's
/// click (a sine dying away fast, the first beat of the bar higher), on the audio clock, from 30 to 240 beats a minute
/// with 2, 3, 4 or 6 beats to the bar. It needs no notes and runs on the device, so it is free. The tempo and the beats
/// are kept per piece on the device.
@MainActor
final class Metronome: ObservableObject {
    static let minBpm = 30
    static let maxBpm = 240
    static let step = 4
    /// The beats to the bar the button goes through.
    static let beatChoices = [2, 3, 4, 6]

    @Published private(set) var running = false
    @Published private(set) var bpm = 80
    @Published private(set) var beats = 4
    /// The beat sounding now, from 0, or -1 when stopped.
    @Published private(set) var beat = -1

    private let clock = ClickClock()
    private var engine: AVAudioEngine?
    private var timer: Timer?
    private var taps: [Double] = []
    private var key: String?
    private let store: UserDefaults

    init(store: UserDefaults = .standard) {
        self.store = store
    }

    /// The tempo and beats kept for this piece: "bpm,beats" under `motiff.pdf.metronome.<id>`.
    func load(piece id: String) {
        key = "motiff.pdf.metronome.\(id)"
        guard let kept = store.string(forKey: key!)?.split(separator: ","), kept.count == 2,
              let bpm = Int(kept[0]), let beats = Int(kept[1]) else { return }
        self.bpm = min(Self.maxBpm, max(Self.minBpm, bpm))
        self.beats = Self.beatChoices.contains(beats) ? beats : 4
    }

    func toggle() {
        running ? stop() : start()
    }

    func start() {
        guard !running else { return }
        do {
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try AVAudioSession.sharedInstance().setActive(true)
            let engine = try self.engine ?? makeEngine()
            self.engine = engine
            clock.lock.lock()
            clock.bpm = Double(bpm)
            clock.beats = beats
            clock.phase = 0
            clock.count = 0
            clock.running = true
            clock.lock.unlock()
            if !engine.isRunning { try engine.start() }
            running = true
            let timer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.tick() }
            }
            RunLoop.main.add(timer, forMode: .common)
            self.timer = timer
        } catch {
            stop()
        }
    }

    func stop() {
        clock.lock.lock()
        clock.running = false
        clock.lock.unlock()
        timer?.invalidate()
        timer = nil
        engine?.pause()
        running = false
        beat = -1
    }

    /// A new tempo or beats to the bar, heard from the next beat and kept for the piece.
    func change(bpm: Int, beats: Int) {
        self.bpm = min(Self.maxBpm, max(Self.minBpm, bpm))
        self.beats = Self.beatChoices.contains(beats) ? beats : 4
        clock.lock.lock()
        clock.bpm = Double(self.bpm)
        clock.beats = self.beats
        clock.lock.unlock()
        if let key { store.set("\(self.bpm),\(self.beats)", forKey: key) }
    }

    func slower() { change(bpm: bpm - Self.step, beats: beats) }
    func faster() { change(bpm: bpm + Self.step, beats: beats) }

    func nextBeats() {
        let index = Self.beatChoices.firstIndex(of: beats) ?? 2
        change(bpm: bpm, beats: Self.beatChoices[(index + 1) % Self.beatChoices.count])
    }

    /// A tap on the tempo: the tempo of the last few taps, once there are two.
    func tap(at now: Double = Date().timeIntervalSince1970 * 1000) {
        taps = taps.filter { now - $0 <= 8000 } + [now]
        if let tapped = Self.tappedBpm(taps) { change(bpm: tapped, beats: beats) }
    }

    /// The mean gap of up to the last four taps (milliseconds), in beats a minute; nil with fewer than two taps or when the
    /// last gap is over two seconds (a new start). As the web app's `tappedBpm`.
    nonisolated static func tappedBpm(_ times: [Double]) -> Int? {
        guard times.count >= 2, times[times.count - 1] - times[times.count - 2] <= 2000 else { return nil }
        let recent = Array(times.suffix(5))
        let gaps = zip(recent.dropFirst(), recent).map { $0 - $1 }.filter { $0 > 0 && $0 <= 2000 }
        guard !gaps.isEmpty else { return nil }
        let bpm = Int((60000 / (gaps.reduce(0, +) / Double(gaps.count))).rounded())
        return min(240, max(30, bpm))
    }

    private func tick() {
        let now = clock.beat
        if now != beat { beat = now }
    }

    private func makeEngine() throws -> AVAudioEngine {
        let engine = AVAudioEngine()
        let rate = engine.outputNode.outputFormat(forBus: 0).sampleRate > 0 ? engine.outputNode.outputFormat(forBus: 0).sampleRate : 48000
        guard let format = AVAudioFormat(standardFormatWithSampleRate: rate, channels: 1) else {
            throw NSError(domain: "Metronome", code: 1)
        }
        let node = Self.clickNode(format: format, clock: clock, rate: rate)
        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
        return engine
    }
}

extension Metronome {
    /// The node that renders the clicks. It is made outside the main actor: its block runs on the audio thread.
    nonisolated fileprivate static func clickNode(format: AVAudioFormat, clock: ClickClock, rate: Double) -> AVAudioSourceNode {
        let length = 0.05 * rate
        let decay = 1 / (0.012 * rate)
        return AVAudioSourceNode(format: format) { _, _, frameCount, audioBufferList in
            let buffers = UnsafeMutableAudioBufferListPointer(audioBufferList)
            guard let first = buffers.first, let data = first.mData?.assumingMemoryBound(to: Float.self) else { return noErr }
            clock.lock.lock()
            defer { clock.lock.unlock() }
            let framesPerBeat = rate * 60 / max(1, clock.bpm)
            for frame in 0..<Int(frameCount) {
                var sample: Float = 0
                if clock.running {
                    if clock.phase < length {
                        let accent = clock.count % max(1, clock.beats) == 0
                        let omega = 2 * Double.pi * (accent ? 1760 : 1320) / rate
                        sample = Float(sin(omega * clock.phase) * exp(-clock.phase * decay) * (accent ? 0.5 : 0.3))
                    }
                    clock.phase += 1
                    if clock.phase >= framesPerBeat {
                        clock.phase -= framesPerBeat
                        clock.count += 1
                    }
                }
                data[frame] = sample
            }
            for other in buffers.dropFirst() {
                other.mData?.copyMemory(from: data, byteCount: Int(other.mDataByteSize))
            }
            return noErr
        }
    }
}

/// The metronome's pill (the web app's `.metronome-pill`): start and stop, slower, the tempo tapped in time on its number,
/// faster, the beats to the bar and a dot for each beat, the one sounding now filled and the first a little larger.
struct MetronomePill: View {
    @ObservedObject var metronome: Metronome
    let text: UIText
    let close: () -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                metronome.toggle()
            } label: {
                Icon(name: metronome.running ? "pause" : "playf")
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(metronome.running ? text["metronome.stop"] : text["metronome.start"])
            Button("−") { metronome.slower() }
                .buttonStyle(DSItemStyle())
                .disabled(metronome.bpm <= Metronome.minBpm)
                .accessibilityLabel(text["metronome.slower"])
            Button {
                metronome.tap()
            } label: {
                HStack(spacing: 4) {
                    Text(String(metronome.bpm)).monospacedDigit()
                    Text(text["metronome.bpm"]).foregroundStyle(Color.ds("ink-2"))
                }
            }
            .buttonStyle(DSItemStyle())
            .accessibilityLabel(text.format("metronome.tempoLabel", String(metronome.bpm)))
            .accessibilityHint(text["metronome.tapHint"])
            Button("+") { metronome.faster() }
                .buttonStyle(DSItemStyle())
                .disabled(metronome.bpm >= Metronome.maxBpm)
                .accessibilityLabel(text["metronome.faster"])
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                metronome.nextBeats()
            } label: {
                HStack(spacing: 4) {
                    Text(String(metronome.beats)).monospacedDigit()
                    Text(text["metronome.beatsShort"])
                }
            }
            .buttonStyle(DSItemStyle())
            .accessibilityLabel(text.format("metronome.beatsLabel", String(metronome.beats)))
            .accessibilityHint(text["metronome.beatsHint"])
            HStack(spacing: 6) {
                ForEach(0..<metronome.beats, id: \.self) { index in
                    let size: CGFloat = index == 0 ? 10 : 8
                    Circle()
                        .fill(index == metronome.beat ? Color.ds("accent") : Color.clear)
                        .overlay(Circle().strokeBorder(index == metronome.beat ? Color.ds("accent") : Color.ds("ink-3"), lineWidth: 1.5))
                        .frame(width: size, height: size)
                }
            }
            .padding(.horizontal, CGFloat.ds("s-2"))
            .accessibilityHidden(true)
            Button(action: close) {
                Icon(name: "close").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["metronome.close"])
        }
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["metronome.label"])
    }
}
