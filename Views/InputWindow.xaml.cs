using ExCord.Services;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ExCord.Views;

public partial class InputWindow : Window
{
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string _draftTextBeforeHistory = string.Empty;

    public event EventHandler<string>? TextSubmitted;

    public InputWindow()
    {
        InitializeComponent();
        ApplyLocalizedTexts();

        Loaded += (_, _) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };

        Deactivated += (_, _) => Hide();

        KeyDown += InputWindow_KeyDown;
        InputTextBox.PreviewKeyDown += InputTextBox_PreviewKeyDown;
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

    public void ApplyLocalizedTexts()
    {
        var text = LocalizationService.Text;
        Title = text.InputWindowTitle;
        InputHeaderTextBlock.Text = text.InputHeader;
    }

    private void InputWindow_KeyDown(object sender, KeyEventArgs e)
    {
        HandleInputKey(e);
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleInputKey(e);
    }

    private void HandleInputKey(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

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
            NavigateHistory(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            NavigateHistory(-1);
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
        _draftTextBeforeHistory = string.Empty;
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
            if (direction > 0)
            {
                _draftTextBeforeHistory = InputTextBox.Text;
                _historyIndex = 0;
            }
            else
            {
                return;
            }
        }
        else
        {
            _historyIndex += direction;

            if (_historyIndex < 0)
            {
                _historyIndex = -1;
                InputTextBox.Text = _draftTextBeforeHistory;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
                return;
            }

            if (_historyIndex >= _history.Count)
            {
                _historyIndex = _history.Count - 1;
            }
        }

        InputTextBox.Text = _history[_historyIndex];
        InputTextBox.CaretIndex = InputTextBox.Text.Length;
    }
}
