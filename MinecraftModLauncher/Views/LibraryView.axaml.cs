using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MinecraftModLauncher.Views;

public partial class LibraryView : UserControl {
    public LibraryView() {
        InitializeComponent();

        // This view hand-writes InitializeComponent() (see below), which opts out
        // of Avalonia's auto-generated x:Name fields — so named elements are
        // looked up explicitly here instead of referenced as fields.
        this.FindControl<Button>("TargetInstanceButton")?.WireFlyoutSlide();
        this.FindControl<Button>("TypeButton")?.WireFlyoutSlide();
        this.FindControl<Button>("LoaderButton")?.WireFlyoutSlide();
        this.FindControl<Button>("LibraryCategoryButton")?.WireFlyoutSlide();
        this.FindControl<Button>("SortButton")?.WireFlyoutSlide();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}