using Microsoft.UI.Xaml;

namespace PixelDone.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 800));
    }
}
