using System.Collections.Generic;
using System.Drawing;

// Headless implementations: nothing is drawn. Event handlers and delegates still run, so script
// logic that waits on a form (Shown, Invoke, Close) behaves as it would on Windows.
namespace System.Windows.Forms;

public delegate void MethodInvoker();

public delegate void PaintEventHandler(object? sender, PaintEventArgs e);

public class PaintEventArgs : EventArgs
{
}

public enum DockStyle { None, Top, Bottom, Left, Right, Fill }

public enum FormStartPosition { Manual, CenterScreen, WindowsDefaultLocation, WindowsDefaultBounds, CenterParent }

public enum FormBorderStyle { None, FixedSingle, Fixed3D, FixedDialog, Sizable, FixedToolWindow, SizableToolWindow }

public enum ProgressBarStyle { Blocks, Continuous, Marquee }

public enum DialogResult { None, OK, Cancel, Abort, Retry, Ignore, Yes, No }

public class Control : IDisposable
{
    public string Text { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DockStyle Dock { get; set; }
    public Color ForeColor { get; set; }
    public Color BackColor { get; set; }
    public Size Size { get; set; }
    public int Width { get => Size.Width; set => Size = new Size(value, Size.Height); }
    public int Height { get => Size.Height; set => Size = new Size(Size.Width, value); }
    public bool Visible { get; set; } = true;
    public bool IsDisposed { get; private set; }
    public ControlCollection Controls { get; } = new();

    public event PaintEventHandler? Paint;

    public object? Invoke(Delegate method) => method.DynamicInvoke();

    public object? Invoke(Delegate method, params object?[]? args) => method.DynamicInvoke(args);

    public IAsyncResult BeginInvoke(Delegate method)
    {
        method.DynamicInvoke();
        return Threading.Tasks.Task.CompletedTask;
    }

    public void Refresh() => Paint?.Invoke(this, new PaintEventArgs());

    public void Invalidate() { }

    public void Show() => Visible = true;

    public void Hide() => Visible = false;

    public void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    public class ControlCollection : List<Control>
    {
    }
}

public class Form : Control
{
    public FormStartPosition StartPosition { get; set; }
    public FormBorderStyle FormBorderStyle { get; set; }
    public bool MaximizeBox { get; set; }
    public bool MinimizeBox { get; set; }
    public bool TopMost { get; set; }
    public bool ShowInTaskbar { get; set; } = true;

    internal bool IsClosed { get; private set; }

    public event EventHandler? Shown;
    public event EventHandler? FormClosed;

    internal void RaiseShown() => Shown?.Invoke(this, EventArgs.Empty);

    public void Close()
    {
        if (IsClosed)
            return;
        IsClosed = true;
        FormClosed?.Invoke(this, EventArgs.Empty);
    }

    public DialogResult ShowDialog()
    {
        Application.Run(this);
        return DialogResult.OK;
    }
}

public class ProgressBar : Control
{
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public int Value { get; set; }
    public int Step { get; set; } = 10;
    public ProgressBarStyle Style { get; set; }

    public void PerformStep() => Value = Math.Min(Maximum, Value + Step);
}

public class Label : Control
{
}

public static class Application
{
    /// <summary>Runs the form's Shown handlers on the calling thread, then closes it.</summary>
    public static void Run(Form form)
    {
        form.RaiseShown();
        form.Close();
    }

    public static void DoEvents() { }
}

public static class MessageBox
{
    public static DialogResult Show(string text) => Show(text, string.Empty);

    public static DialogResult Show(string text, string caption)
    {
        Diagnostics.Trace.WriteLine($"[MessageBox] {caption}: {text}");
        return DialogResult.OK;
    }
}
