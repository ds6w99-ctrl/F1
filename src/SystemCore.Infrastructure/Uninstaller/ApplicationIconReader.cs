using System.Globalization;
using System.Runtime.InteropServices;
using SystemCore.Core.Uninstaller;
using Windows.Graphics.Imaging;
using Windows.Management.Deployment;
using Windows.Storage.Streams;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed record IconFile(string Path, int Index);

public static class ApplicationIconReader
{
    public static IconFile? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl)) return null;
        var text = value.Trim(); var index = 0;
        var comma = text.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        { if (parsed is < -65535 or > 65535) return null; index = parsed; text = text[..comma].Trim(); }
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length > 2) text = text[1..^1];
        text = Environment.ExpandEnvironmentVariables(text);
        if (!SafePath(text)) return null;
        return new(text, index);
    }

    public static bool SafePath(string path)
    {
        if (path.Length is < 4 or > 240 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path[2..].Contains(':') || path.Contains('/') || path.Contains('"') || path.Contains('%') || path.Any(char.IsControl)
            || path.Split('\\').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.'))
            || !new[] { ".exe", ".dll", ".ico" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return false;
        try
        {
            if (new DriveInfo(path[..3]).DriveType != DriveType.Fixed || !File.Exists(path) || new FileInfo(path).Length > 256L * 1024 * 1024) return false;
            for (var node = path; node is not null; node = System.IO.Path.GetDirectoryName(node))
                if ((File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public static async Task<byte[]?> ReadAsync(InstalledApplication app, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (app.ApplicationType == ApplicationType.Msix)
            {
                var package = new PackageManager().FindPackageForUser(string.Empty, app.PackageFullName);
                if (package is null) return null;
                // Official AppListEntry API resolves manifest assets, including scale variants.
                var entries = await package.GetAppListEntriesAsync().AsTask(token).ConfigureAwait(false);
                var entry = entries.FirstOrDefault();
                if (entry is null) return null;
                using var logo = await entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(36, 36)).OpenReadAsync().AsTask(token).ConfigureAwait(false);
                if (logo.Size > 256 * 1024) return null;
                var decoder = await BitmapDecoder.CreateAsync(logo).AsTask(token).ConfigureAwait(false);
                if (decoder.PixelWidth is 0 or > 4096 || decoder.PixelHeight is 0 or > 4096) return null;
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = 36, ScaledHeight = 36 }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
                return await Encode(pixels.DetachPixelData(), 36, token).ConfigureAwait(false);
            }
            var source = Parse(app.IconLocation);
            var pixelsDesktop = source is null ? null : Extract(source);
            if (pixelsDesktop is null && UninstallCommandParser.Parse(app.RegisteredUninstallCommand) is { } command
                && app.InstallLocation is { } root && System.IO.Path.IsPathFullyQualified(root)
                && command.Executable.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                pixelsDesktop = Extract(new(command.Executable, 0));
            token.ThrowIfCancellationRequested();
            return pixelsDesktop is null ? null : await Encode(pixelsDesktop, 36, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private static async Task<byte[]> Encode(byte[] pixels, uint size, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(token).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, size, size, 96, 96, pixels);
        await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        stream.Seek(0); using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size).AsTask(token).ConfigureAwait(false);
        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); return bytes;
    }

    // Extract resources only: no executable launch, LoadLibrary or shell commands.
    private static byte[]? Extract(IconFile file)
    {
        if (!SafePath(file.Path)) return null;
        using var lease = Cleaner.PinnedCleanerPath.Open(file.Path, false, shareRead: true);
        var result = SHDefExtractIcon(file.Path, file.Index, 0, out var icon, out var small, 36);
        try
        {
            if (result < 0 || icon == IntPtr.Zero) return null;
            var dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            var info = new BitmapInfo { Size = 40, Width = 36, Height = -36, Planes = 1, BitCount = 32 };
            var bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            try
            {
                if (bitmap == IntPtr.Zero) return null;
                var pixels = new byte[36 * 36 * 4]; Marshal.Copy(pixels, 0, bits, pixels.Length);
                var old = SelectObject(dc, bitmap);
                try { if (!DrawIconEx(dc, 0, 0, icon, 36, 36, 0, IntPtr.Zero, 3)) return null; }
                finally { SelectObject(dc, old); }
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                if (!Enumerable.Range(0, 36 * 36).Any(i => pixels[i * 4 + 3] != 0))
                    for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                return pixels;
            }
            finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); DeleteDC(dc); }
        }
        finally { if (icon != IntPtr.Zero) DestroyIcon(icon); if (small != IntPtr.Zero) DestroyIcon(small); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int X, Y; public uint Used, Important; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHDefExtractIconW")] private static extern int SHDefExtractIcon(string path, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
