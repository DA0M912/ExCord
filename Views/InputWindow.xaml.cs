using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ExCord.Views;

public partial class InputWindow : Window
{
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    public event EventHandler<string>? TextSubmitted;

    public InputWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };

        Deactivated += (_, _) => Hide();

        KeyDown += InputWindow_KeyDown;
    }

    public void ShowOverlay()
    {
        var workArea = SystemParameters.WorkArea;
        Left = (workArea.Width - Width) / 2;
        Top = workArea.Height - Height - 80;

        Show();
        Activate();
        InputTextBox.Focus();
        InputTextBox.SelectAll();
        Topmost = true;
    }

    private void InputWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            SubmitCurrentText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            NavigateHistory(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            NavigateHistory(1);
            e.Handled = true;
        }
    }

    private void SubmitCurrentText()
    {
        var text = InputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Hide();
            return;
        }

        _history.Remove(text);
        _history.Insert(0, text);
        if (_history.Count > 20)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        _historyIndex = -1;
        TextSubmitted?.Invoke(this, text);
        InputTextBox.Clear();
        Hide();
    }

    private void NavigateHistory(int direction)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex == -1)
        {
            _historyIndex = 0;
        }

        _historyIndex += direction;
        if (_historyIndex < 0)
        {
            _historyIndex = 0;
        }

        if (_historyIndex >= _history.Count)
        {
            _historyIndex = _history.Count - 1;
        }

        InputTextBox.Text = _history[_historyIndex];
        InputTextBox.CaretIndex = InputTextBox.Text.Length;
    }
}
