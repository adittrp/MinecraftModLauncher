using Avalonia.Controls;

namespace MinecraftModLauncher.Views;

public static class FlyoutExtensions {
    // The "slide" FlyoutPresenter style (App.axaml) starts hidden/offset; adding
    // "open" on show and removing it on hide is what triggers its transitions.
    // FlyoutPresenterClasses lives on Flyout itself, not the FlyoutBase it derives from.
    public static void WireSlide(this Flyout flyout) {
        flyout.Opened += (_, _) => flyout.FlyoutPresenterClasses.Add("open");
        flyout.Closed += (_, _) => flyout.FlyoutPresenterClasses.Remove("open");
    }

    // Flyout isn't a StyledElement, so x:Name on a <Flyout> never registers a
    // code-behind field. Name the owning Button instead and reach .Flyout off it.
    public static void WireFlyoutSlide(this Button button) {
        if (button.Flyout is Flyout flyout) flyout.WireSlide();
    }
}
