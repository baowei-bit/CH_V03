using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace CH_V03.App;

/// <summary>Prevents whitespace from being entered into editable controls for the JiaLianYi account.</summary>
internal static class JiaLianYiInputFilter
{
    internal static bool Enabled { get; set; }

    internal static void Register()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler(OnPreviewTextInput));
        EventManager.RegisterClassHandler(typeof(PasswordBox), TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler(OnPreviewTextInput));
        EventManager.RegisterClassHandler(typeof(TextBox), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(PasswordBox), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(TextBox), DataObject.PastingEvent,
            new DataObjectPastingEventHandler(OnPasting));
        EventManager.RegisterClassHandler(typeof(PasswordBox), DataObject.PastingEvent,
            new DataObjectPastingEventHandler(OnPasting));
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Enabled && ContainsForbiddenWhitespace(e.Text))
            e.Handled = true;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Enabled && e.Key is Key.Space or Key.Enter or Key.Return)
            e.Handled = true;
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!Enabled || !e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            return;

        var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string;
        if (pasted is null || !ContainsForbiddenWhitespace(pasted))
            return;

        e.CancelCommand();
        if (sender is TextBox textBox && !textBox.IsReadOnly)
            textBox.SelectedText = RemoveForbiddenWhitespace(pasted);
    }

    private static bool ContainsForbiddenWhitespace(string value) => value.Any(IsForbiddenWhitespace);

    private static string RemoveForbiddenWhitespace(string value) => new(value.Where(c => !IsForbiddenWhitespace(c)).ToArray());

    private static bool IsForbiddenWhitespace(char value) => value is ' ' or '\u3000' or '\r' or '\n';
}
