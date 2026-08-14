namespace Chummer.Android.Native;

public static class TabletLayoutPolicy
{
    public const double ExpandedWidthDip = 840d;
    public const double WideInspectorWidthDip = 1180d;

    public static bool UseTabletComposition(DeviceIdiom idiom, double widthDip)
        => idiom == DeviceIdiom.Tablet || widthDip >= ExpandedWidthDip;

    public static bool UseWideInspector(double widthDip)
        => widthDip >= WideInspectorWidthDip;
}
