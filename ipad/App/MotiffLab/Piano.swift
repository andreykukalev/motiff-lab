import AVFoundation

/// The piano the web app plays (Salamander Grand Piano V3 by Alexander Holm, CC BY 3.0, tools/piano): its 30 MP3
/// samples and `piano.json`, copied into the app from the web app's files.
enum Piano {
    /// The attack starts where the sample first rises above this share of its peak; encoders add silence before it.
    private static let onsetThreshold: Float = 0.003
    private static let onsetLeadSeconds = 0.002

    static func load(bundle: Bundle = .main) throws -> [SampleRegion] {
        guard let folder = bundle.url(forResource: "piano", withExtension: nil),
              let manifest = try JSONSerialization.jsonObject(with: Data(contentsOf: folder.appendingPathComponent("piano.json"))) as? [String: Any],
              let samples = manifest["samples"] as? [[String: Any]], !samples.isEmpty
        else { throw PlayerError(message: "The piano samples are missing from the app.") }

        return try samples.map { sample in
            guard let file = sample["file"] as? String, let pitch = (sample["pitch"] as? NSNumber)?.intValue else {
                throw PlayerError(message: "piano.json lists a sample without its file or note.")
            }
            let audio = try AVAudioFile(forReading: folder.appendingPathComponent(file))
            let format = audio.processingFormat
            guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(audio.length)) else {
                throw PlayerError(message: "The piano sample \(file) could not be read.")
            }
            try audio.read(into: buffer)
            return SampleRegion(
                pitch: pitch,
                tuneCents: (sample["tuneCents"] as? NSNumber)?.doubleValue ?? 0,
                data: trimmedToOnset(buffer),
                sampleRate: format.sampleRate
            )
        }
    }

    /// A mono copy of the decoded sample from just before its attack, so every note starts on time.
    static func trimmedToOnset(_ buffer: AVAudioPCMBuffer) -> [Float] {
        guard let channels = buffer.floatChannelData else { return [] }
        let count = Int(buffer.frameLength)
        let channelCount = Int(buffer.format.channelCount)
        var peak: Float = 0
        for c in 0..<channelCount {
            for i in 0..<count { peak = max(peak, abs(channels[c][i])) }
        }

        let threshold = peak * onsetThreshold
        var onset = 0
        search: for i in 0..<count {
            for c in 0..<channelCount where abs(channels[c][i]) > threshold {
                onset = i
                break search
            }
        }

        let start = max(0, onset - Int((onsetLeadSeconds * buffer.format.sampleRate).rounded()))
        var mono = [Float](repeating: 0, count: count - start)
        for c in 0..<channelCount {
            for i in 0..<mono.count { mono[i] += channels[c][start + i] / Float(channelCount) }
        }
        return mono
    }
}
