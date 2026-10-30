import UIKit
import WebKit

/// Relays the Apple Pencil's double tap and squeeze to the score's pencil layer
/// (src/MotiffLab.Web/Scripts/score/pencil-layer.ts) as a "motiff-pencil" event on the page's window.
/// Writing itself needs nothing from here: the page reads the Pencil through pointer events.
@MainActor
final class PencilBridge: NSObject, UIPencilInteractionDelegate {
    private weak var webView: WKWebView?

    func attach(to webView: WKWebView) {
        self.webView = webView
        let interaction = UIPencilInteraction()
        interaction.delegate = self
        webView.addInteraction(interaction)
    }

    // A double tap on the second-generation Pencil, before iPadOS 17.5.
    func pencilInteractionDidTap(_ interaction: UIPencilInteraction) {
        tapped(UIPencilInteraction.preferredTapAction)
    }

    @available(iOS 17.5, *)
    func pencilInteraction(_ interaction: UIPencilInteraction, didReceiveTap tap: UIPencilInteraction.Tap) {
        tapped(UIPencilInteraction.preferredTapAction)
    }

    /// A squeeze on the Pencil Pro shows or hides the Pencil tools, as a squeeze shows a palette elsewhere.
    @available(iOS 17.5, *)
    func pencilInteraction(_ interaction: UIPencilInteraction, didReceiveSqueeze squeeze: UIPencilInteraction.Squeeze) {
        guard squeeze.phase == .ended, UIPencilInteraction.preferredSqueezeAction != .ignore else {
            return
        }
        send("toggle-tools")
    }

    /// Follows what the player chose for a double tap in Settings > Apple Pencil.
    private func tapped(_ preferred: UIPencilPreferredAction) {
        switch preferred {
        case .ignore:
            return
        case .switchEraser, .switchPrevious:
            send("switch-eraser")
        default:
            send("toggle-tools")
        }
    }

    private func send(_ action: String) {
        let script = "window.dispatchEvent(new CustomEvent('motiff-pencil', { detail: { action: '\(action)' } }));"
        webView?.evaluateJavaScript(script, completionHandler: nil)
    }
}
