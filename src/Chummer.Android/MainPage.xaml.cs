using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Chummer.Android;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    public BlazorWebView WebView => ChummerWebView;
}
