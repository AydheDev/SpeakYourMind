using System;
using System.Security.Cryptography;
using System.Text;

namespace SpeakYourMind.Core;

// Editing or cancelling setup never mutates the active connection.
public sealed class ConnectionDraft
{
    public Connection Value { get; }
    public string Key = "";
    private string validated = "";
    public ConnectionDraft(Connection original) { Value = original.Copy(); Value.Normalize(); }
    public string Stamp => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SYMJson.Write(Value) + "\n" + Key)));
    public bool Tested => validated.Length > 0 && validated == Stamp;
    public bool ValidateTest(string requestedStamp)
    {
        if (requestedStamp != Stamp) return false;
        validated = requestedStamp; return true;
    }
    public void Apply(Connection target)
    {
        if (!Tested) throw new InvalidOperationException("Test this connection before saving it.");
        target.CopyFrom(Value);
    }
}
