using System.Runtime.InteropServices;

namespace AeroflyAcars.Mac;

/// <summary>
/// Fragt vor dem Schließen bzw. Beenden nach – und „Nein“ bricht wirklich ab.
///
/// Photino meldet das Schließen auf dem Mac erst in <c>windowWillClose:</c>; dann ist das Fenster schon zu und eine
/// Antwort „Nein“ wirkungslos (Testbericht beta.6: Flarelog beendete sich trotzdem, der Flug war verloren).
/// Deshalb werden in Photinos eigene Objective-C-Klassen die beiden Methoden eingehängt, die macOS VORHER fragt:
/// <list type="bullet">
/// <item><c>WindowDelegate windowShouldClose:</c> – roter Knopf, Cmd+W</item>
/// <item><c>AppDelegate applicationShouldTerminate:</c> – Cmd+Q, „Beenden“ im Dock, Abmelden</item>
/// </list>
/// </summary>
internal static unsafe class MacQuitGuard
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern byte class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(ObjC)] private static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    private static Func<bool>? _mayQuit;

    /// <summary>
    /// Hängt die Rückfrage ein. <paramref name="mayQuit"/> läuft auf dem Hauptthread und gibt zurück, ob beendet
    /// werden darf (und speichert dann bereits). Gibt false zurück, wenn Photinos Klassen nicht gefunden wurden.
    /// </summary>
    public static bool Install(Func<bool> mayQuit)
    {
        _mayQuit = mayQuit;
        IntPtr window = objc_getClass("WindowDelegate"), app = objc_getClass("AppDelegate");
        if (window == IntPtr.Zero || app == IntPtr.Zero) return false;

        // BOOL ist auf Apple Silicon „B“ (bool), auf Intel „c“ (signed char) – beides 1 Byte.
        string boolType = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "B" : "c";
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte> shouldClose = &WindowShouldClose;
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint> shouldTerminate = &ApplicationShouldTerminate;
        Add(window, "windowShouldClose:", (IntPtr)shouldClose, boolType + "@:@");
        Add(app, "applicationShouldTerminate:", (IntPtr)shouldTerminate, "Q@:@");
        return true;
    }

    private static void Add(IntPtr cls, string selector, IntPtr imp, string types)
    {
        var sel = sel_registerName(selector);
        if (class_addMethod(cls, sel, imp, types) == 0) class_replaceMethod(cls, sel, imp, types);   // gibt es schon
    }

    [UnmanagedCallersOnly]
    private static byte WindowShouldClose(IntPtr self, IntPtr cmd, IntPtr sender) => Ask() ? (byte)1 : (byte)0;

    /// <summary>NSTerminateCancel = 0, NSTerminateNow = 1.</summary>
    [UnmanagedCallersOnly]
    private static nuint ApplicationShouldTerminate(IntPtr self, IntPtr cmd, IntPtr sender) => Ask() ? 1u : 0u;

    private static bool Ask()
    {
        try
        {
            return _mayQuit?.Invoke() ?? true;
        }
        catch (Exception)
        {
            return true;   // lieber beenden als hängen bleiben – gespeichert wird beim Prozessende trotzdem
        }
    }
}
