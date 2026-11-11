using System;

namespace SpeakYourMind.Core;

public static class DialogueReadScope
{
    [ThreadStatic] private static int depth;
    public static bool Active => depth > 0;
    public static T Read<T>(Func<T> read)
    {
        depth++;
        try { return read(); }
        finally { depth--; }
    }
}
