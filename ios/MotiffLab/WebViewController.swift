import AVFoundation
import UIKit
import WebKit

/// Shows the Motiff Lab web app and gives it what the iPad offers a native app.
@MainActor
final class WebViewController: UIViewController {
    private var webView: WKWebView!
    private let pencil = PencilBridge()
    private let downloads = DownloadSaver()
    private var offline: UIView?

    override func loadView() {
        let configuration = WKWebViewConfiguration()
        // The web app is an app-bound domain (Info.plist, WKAppBoundDomains): WebKit then runs its service
        // worker, so the app opens without a connection once it has been opened with one.
        configuration.limitsNavigationsToAppBoundDomains = true
        configuration.allowsInlineMediaPlayback = true
        configuration.mediaTypesRequiringUserActionForPlayback = []
        configuration.applicationNameForUserAgent = "MotiffLabApp/\(AppConfig.version)"
        // Tells the page it runs in the iPad app, before any of its scripts.
        let marker = WKUserScript(
            source: "window.motiffNative = Object.freeze({ platform: 'ipad', version: '\(AppConfig.version)' });",
            injectionTime: .atDocumentStart,
            forMainFrameOnly: true)
        configuration.userContentController.addUserScript(marker)

        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.isOpaque = false
        webView.backgroundColor = UIColor(named: "Charcoal")
        webView.scrollView.backgroundColor = UIColor(named: "Charcoal")
        // The workspace lays itself out to the window; the page itself should not bounce or swipe back.
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.scrollView.bounces = false
        webView.allowsBackForwardNavigationGestures = false
        #if DEBUG
        // Safari on the Mac can inspect the page: Develop > (this iPad) > Motiff Lab.
        if #available(iOS 16.4, *) {
            webView.isInspectable = true
        }
        #endif
        pencil.attach(to: webView)
        downloads.presenter = self
        self.webView = webView
        view = webView
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        configureAudio()
        webView.load(URLRequest(url: AppConfig.webURL))
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        // The iPad stays on at the piano while the app is open.
        UIApplication.shared.isIdleTimerDisabled = true
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        UIApplication.shared.isIdleTimerDisabled = false
    }

    /// Playback is the point of the app: it sounds with the silent switch on, as a music app does.
    private func configureAudio() {
        do {
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try AVAudioSession.sharedInstance().setActive(true)
        } catch {
            print("Motiff Lab: the audio session could not be set up: \(error)")
        }
    }

    // MARK: Without a connection

    /// Shown when the web app could not be loaded at all: the first start needs a connection.
    private func showOffline(_ error: Error) {
        offline?.removeFromSuperview()
        let message = UILabel()
        message.text = "Motiff Lab could not be opened.\n\(error.localizedDescription)\n\nThe first start needs an internet connection; after that the app opens without one."
        message.numberOfLines = 0
        message.textAlignment = .center
        message.textColor = .white
        message.font = .preferredFont(forTextStyle: .body)

        var button = UIButton.Configuration.filled()
        button.title = "Try again"
        button.baseBackgroundColor = UIColor(named: "AccentColor")
        button.baseForegroundColor = UIColor(named: "Charcoal")
        let retry = UIButton(configuration: button, primaryAction: UIAction { [weak self] _ in
            self?.offline?.removeFromSuperview()
            self?.offline = nil
            if let webView = self?.webView {
                webView.load(URLRequest(url: AppConfig.webURL))
            }
        })

        let stack = UIStackView(arrangedSubviews: [message, retry])
        stack.axis = .vertical
        stack.spacing = 24
        stack.alignment = .center
        stack.translatesAutoresizingMaskIntoConstraints = false

        let cover = UIView()
        cover.backgroundColor = UIColor(named: "Charcoal")
        cover.translatesAutoresizingMaskIntoConstraints = false
        cover.addSubview(stack)
        view.addSubview(cover)
        NSLayoutConstraint.activate([
            cover.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            cover.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            cover.topAnchor.constraint(equalTo: view.topAnchor),
            cover.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            stack.centerXAnchor.constraint(equalTo: cover.centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: cover.centerYAnchor),
            stack.widthAnchor.constraint(lessThanOrEqualTo: cover.widthAnchor, multiplier: 0.7),
        ])
        offline = cover
    }
}

// MARK: Navigation

extension WebViewController: WKNavigationDelegate {
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction) async -> WKNavigationActionPolicy {
        // Export (a project file, MIDI) is a link with a download attribute: saved through Files.
        if navigationAction.shouldPerformDownload {
            return .download
        }
        guard let url = navigationAction.request.url else {
            return .cancel
        }
        if AppConfig.isWebApp(url) || ["about", "blob", "data"].contains(url.scheme ?? "") {
            return .allow
        }
        // Other sites (the privacy notice's links, homr's source) open in Safari, not in place of the workspace.
        if navigationAction.targetFrame?.isMainFrame ?? true {
            _ = await UIApplication.shared.open(url)
            return .cancel
        }
        return .allow
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse) async -> WKNavigationResponsePolicy {
        navigationResponse.canShowMIMEType ? .allow : .download
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        download.delegate = downloads
    }

    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        download.delegate = downloads
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        // A navigation the app cancelled itself (another site opened in Safari) is not a failure.
        if (error as NSError).code == NSURLErrorCancelled {
            return
        }
        showOffline(error)
    }

    /// iPadOS ends a page that uses too much memory; open it again rather than show a blank screen.
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        webView.reload()
    }
}

// MARK: Dialogs, new windows and the microphone

extension WebViewController: WKUIDelegate {
    /// A link that asks for a new window opens in Safari.
    func webView(
        _ webView: WKWebView,
        createWebViewWith configuration: WKWebViewConfiguration,
        for navigationAction: WKNavigationAction,
        windowFeatures: WKWindowFeatures
    ) -> WKWebView? {
        if let url = navigationAction.request.url {
            UIApplication.shared.open(url)
        }
        return nil
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo) async {
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
            alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in continuation.resume() })
            present(alert, animated: true)
        }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo) async -> Bool {
        await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
            let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
            alert.addAction(UIAlertAction(title: "Cancel", style: .cancel) { _ in continuation.resume(returning: false) })
            alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in continuation.resume(returning: true) })
            present(alert, animated: true)
        }
    }

    /// The web app may use the camera and microphone (a photo of a page to scan, a recording to transcribe);
    /// iPadOS still asks the player once, with the reasons in Info.plist.
    func webView(
        _ webView: WKWebView,
        requestMediaCapturePermissionFor origin: WKSecurityOrigin,
        initiatedByFrame frame: WKFrameInfo,
        type: WKMediaCaptureType
    ) async -> WKPermissionDecision {
        origin.host.lowercased() == AppConfig.webURL.host?.lowercased() ? .grant : .deny
    }
}

// MARK: Export

/// Saves what the web app hands over for download (a project file, a MIDI file) and offers it to Files.
@MainActor
final class DownloadSaver: NSObject, WKDownloadDelegate {
    weak var presenter: UIViewController?
    private var destinations: [ObjectIdentifier: URL] = [:]

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String) async -> URL? {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        } catch {
            return nil
        }
        let destination = folder.appendingPathComponent(suggestedFilename.isEmpty ? "Motiff Lab export" : suggestedFilename)
        destinations[ObjectIdentifier(download)] = destination
        return destination
    }

    func downloadDidFinish(_ download: WKDownload) {
        guard let file = destinations.removeValue(forKey: ObjectIdentifier(download)) else {
            return
        }
        let picker = UIDocumentPickerViewController(forExporting: [file], asCopy: true)
        presenter?.present(picker, animated: true)
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        destinations.removeValue(forKey: ObjectIdentifier(download))
    }
}
