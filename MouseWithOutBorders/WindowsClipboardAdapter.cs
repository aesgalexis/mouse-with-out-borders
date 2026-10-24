using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseWithOutBorders;

internal interface IClipboardAdapter
{
    uint Sequence { get; }
    ClipboardContent? Read();
    void Write(ClipboardContent content);
}

internal sealed class WindowsClipboardAdapter : IClipboardAdapter
{
    private const uint FileDropFormat = 15;
    private const uint UnicodeTextFormat = 13;
    private const int MaxPathCharacters = 32767;
    private const int ClipboardBusy = unchecked((int)0x800401D0);
    private readonly IntPtr _owner;

    internal WindowsClipboardAdapter(IntPtr owner = default) => _owner = owner;

    public uint Sequence => GetClipboardSequenceNumber();

    public ClipboardContent? Read()
    {
        if (!OpenClipboard(_owner)) throw new ExternalException("The clipboard is busy.", ClipboardBusy);
        try
        {
            // File managers also expose a text representation of copied paths.
            // Preserve the files rather than sharing that secondary text format.
            if (IsClipboardFormatAvailable(FileDropFormat)) return ReadFiles();
            if (IsClipboardFormatAvailable(UnicodeTextFormat)) return ReadText();
            return null;
        }
        finally { CloseClipboard(); }
    }

    private static ClipboardContent ReadFiles()
    {
        IntPtr handle = ReadHandle(FileDropFormat);
        ulong bytes = GlobalSize(handle).ToUInt64();
        const long maxBytes = 20L + 2L * (1L + ClipboardLimits.MaxFiles * (MaxPathCharacters + 1L));
        if (bytes < 20 || bytes > maxBytes)
            throw new InvalidDataException("Clipboard file list is too large or invalid.");

        uint count = DragQueryFile(handle, uint.MaxValue, null, 0);
        if (count is < 1 or > ClipboardLimits.MaxFiles)
            throw new InvalidDataException("Share between 1 and 20 files at a time.");
        string[] paths = new string[count];
        for (uint index = 0; index < count; index++)
        {
            uint length = DragQueryFile(handle, index, null, 0);
            if (length is < 1 or > MaxPathCharacters)
                throw new InvalidDataException("Clipboard file path is too long or invalid.");
            StringBuilder path = new(checked((int)length + 1));
            if (DragQueryFile(handle, index, path, (uint)path.Capacity) != length || path.Length != length)
                throw new InvalidDataException("Clipboard file path changed while reading.");
            paths[index] = path.ToString();
        }
        return new(Files: paths);
    }

    private static ClipboardContent ReadText()
    {
        IntPtr handle = ReadHandle(UnicodeTextFormat);
        ulong bytes = GlobalSize(handle).ToUInt64();
        if (bytes < sizeof(char) || bytes > 2UL * ClipboardLimits.MaxTextBytes + sizeof(char) || bytes % sizeof(char) != 0)
            throw new InvalidDataException("Clipboard text exceeds the 1 MB limit or is invalid.");
        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) throw new ExternalException("Could not read clipboard text.");
        try
        {
            char[] characters = new char[checked((int)(bytes / sizeof(char)))];
            Marshal.Copy(pointer, characters, 0, characters.Length);
            int length = Array.IndexOf(characters, '\0');
            if (length < 0) throw new InvalidDataException("Clipboard text is not terminated.");
            string text = new(characters, 0, length);
            if (ClipboardLimits.Utf8.GetByteCount(text) > ClipboardLimits.MaxTextBytes)
                throw new InvalidDataException("Clipboard text exceeds the 1 MB limit.");
            return new(Text: text);
        }
        finally { GlobalUnlock(handle); }
    }

    private static IntPtr ReadHandle(uint format)
    {
        IntPtr handle = GetClipboardData(format);
        return handle != IntPtr.Zero ? handle : throw new ExternalException("Could not read the clipboard.");
    }

    public void Write(ClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if ((content.Text is null) == (content.Files is null))
            throw new ArgumentException("Choose text or files to share.", nameof(content));
        DataObject data = new();
        if (content.Text is string text)
        {
            if (text.Contains('\0') || ClipboardLimits.Utf8.GetByteCount(text) > ClipboardLimits.MaxTextBytes)
                throw new ArgumentException("Clipboard text exceeds the 1 MB limit or is invalid.", nameof(content));
            data.SetData(DataFormats.UnicodeText, false, text);
            Clipboard.SetDataObject(data, copy: true, retryTimes: 3, retryDelay: 100);
            return;
        }

        if (content.Files is not { Length: >= 1 and <= ClipboardLimits.MaxFiles } files ||
            files.Any(path => string.IsNullOrEmpty(path) || path.Length > MaxPathCharacters || path.Contains('\0')))
            throw new ArgumentException("Clipboard file list is too large or invalid.", nameof(content));
        StringCollection paths = new();
        paths.AddRange(files);
        data.SetFileDropList(paths);
        // Received files are copies. Never expose a move operation to Explorer.
        using MemoryStream effect = new(BitConverter.GetBytes((int)DragDropEffects.Copy));
        data.SetData("Preferred DropEffect", false, effect);
        Clipboard.SetDataObject(data, copy: true, retryTimes: 3, retryDelay: 100);
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? path, uint length);
}
