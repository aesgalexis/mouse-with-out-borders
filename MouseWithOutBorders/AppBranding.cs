using System.Reflection;

namespace MouseWithOutBorders;

internal static class AppBranding
{
    internal static Icon LoadIcon(Size size)
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MouseWithOutBorders.AppIcon.ico")
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        using Icon source = new(stream, size);
        return (Icon)source.Clone();
    }
}
