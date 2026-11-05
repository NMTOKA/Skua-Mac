using Skua.Core.Interfaces;

namespace Skua.Core.Scripts;

public class Handler : IHandler
{
    public Handler(string name, int ticks, Func<IScriptInterface, bool> function)
    {
        Name = name;
        Ticks = ticks;
        Function = function;
        // Built once: the bot timer used to allocate "handler_" + Name on every 20 ms tick.
        LimitKey = "handler_" + name;
    }

    public string Name { get; }
    public int Ticks { get; }
    public Func<IScriptInterface, bool> Function { get; }
    internal string LimitKey { get; }
}