using System.Runtime.InteropServices;
using AeroflyAcars.Core;

namespace AeroflyAcars.Mac;

/// <summary>
/// Räumt die Menüleiste auf: Photino legt Kopieren, Einfügen und Beenden in ein einziges Menü – macOS hängt dort
/// zusätzlich „Schreibtools“ und „Automatisch ausfüllen“ an, und „Flarelog beenden“ steht mitten in der Liste.
/// Mac-üblich ist: Programm-Menü mit „Beenden“ als letzter Zeile, Kopieren/Einfügen im Menü „Bearbeiten“.
/// Deshalb werden die Bearbeiten-Befehle in ein eigenes Menü verschoben (Wunsch ApfelFlieger).
/// Findet sich nichts Passendes, bleibt alles, wie es ist.
/// </summary>
internal static class MacMenu
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s, IntPtr a, IntPtr b, IntPtr c);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIndex(IntPtr r, IntPtr s, nint index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendCount(IntPtr r, IntPtr s);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendBool(IntPtr r, IntPtr s);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendInsert(IntPtr r, IntPtr s, IntPtr item, nint index);

    private static IntPtr Sel(string name) => sel_registerName(name);

    private static IntPtr NSString(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try { return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    /// <summary>Auf dem Hauptthread aufrufen, nachdem das Fenster erstellt ist.</summary>
    public static void Arrange()
    {
        var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
        var main = Send(app, Sel("mainMenu"));
        if (main == IntPtr.Zero || SendCount(main, Sel("numberOfItems")) < 1) return;
        var appMenu = Send(SendIndex(main, Sel("itemAtIndex:"), 0), Sel("submenu"));
        if (appMenu == IntPtr.Zero) return;

        var editActions = new[] { "undo:", "redo:", "cut:", "copy:", "paste:", "pasteAsPlainText:", "delete:", "selectAll:" }
            .Select(Sel).ToHashSet();
        var move = new List<IntPtr>();
        nint count = SendCount(appMenu, Sel("numberOfItems"));
        for (nint i = 0; i < count; i++)
        {
            var item = SendIndex(appMenu, Sel("itemAtIndex:"), i);
            if (editActions.Contains(Send(item, Sel("action")))) move.Add(item);
        }
        if (move.Count == 0) return;

        var title = NSString(Lang.T("Bearbeiten", "Edit"));
        var edit = Send(Send(objc_getClass("NSMenu"), Sel("alloc")), Sel("initWithTitle:"), title);
        foreach (var item in move)
        {
            Send(item, Sel("retain"));   // removeItem: gibt den Eintrag frei – vorher festhalten
            SendVoid(appMenu, Sel("removeItem:"), item);
            SendVoid(edit, Sel("addItem:"), item);
            Send(item, Sel("release"));
        }
        RemoveStraySeparators(appMenu);

        var editItem = Send(Send(objc_getClass("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"),
            title, IntPtr.Zero, NSString(""));
        SendVoid(editItem, Sel("setSubmenu:"), edit);
        SendInsert(main, Sel("insertItem:atIndex:"), editItem, 1);
    }

    /// <summary>Trennlinien am Anfang, am Ende und doppelte entfernen.</summary>
    private static void RemoveStraySeparators(IntPtr menu)
    {
        bool lastWasSeparator = true;   // Trennlinie ganz oben ist überflüssig
        for (nint i = 0; i < SendCount(menu, Sel("numberOfItems"));)
        {
            var item = SendIndex(menu, Sel("itemAtIndex:"), i);
            bool sep = SendBool(item, Sel("isSeparatorItem")) != 0;
            if (sep && lastWasSeparator) { SendVoid(menu, Sel("removeItem:"), item); continue; }
            lastWasSeparator = sep;
            i++;
        }
        nint n = SendCount(menu, Sel("numberOfItems"));
        if (n > 0)
        {
            var last = SendIndex(menu, Sel("itemAtIndex:"), n - 1);
            if (SendBool(last, Sel("isSeparatorItem")) != 0) SendVoid(menu, Sel("removeItem:"), last);
        }
    }
}
