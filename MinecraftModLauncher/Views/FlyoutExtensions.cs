using Avalonia.Controls;

namespace MinecraftModLauncher.Views;

public static class FlyoutExtensions {
    // Wires the slide-down + fade entrance (see App.axaml's "flyout-content" style)
    // for a Button's Flyout. Toggles "open" directly on Flyout.Content — that control
    // is one persistent instance for the Flyout's whole lifetime (unlike
    // FlyoutPresenterClasses, which PopupFlyoutBase.SetPresenterClasses only copies
    // onto a freshly-created presenter once, at open time — mutating it afterwards
    // never reaches what's on screen). Content's Classes always live-updates.
    public static void WireFlyoutSlide(this Button button) {
        if (button.Flyout is not Flyout flyout) return;
        if (flyout.Content is not Control content) return;

        flyout.Opened += (_, _) => content.Classes.Add("open");
        flyout.Closed += (_, _) => content.Classes.Remove("open");
    }
}
