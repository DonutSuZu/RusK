namespace RusK.API;

/// <summary>IRuskMod の基本実装。Context を保持しておくだけの薄いクラス</summary>
public abstract class RuskMod : IRuskMod
{
    protected IModContext Context { get; private set; }

    void IRuskMod.OnLoad(IModContext context)
    {
        Context = context;
        OnLoad();
    }

    void IRuskMod.OnUnload()
    {
        OnUnload();
        Context = null;
    }

    protected abstract void OnLoad();
    protected virtual void OnUnload() { }
}
