using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SelfHealingPipeline.Controls;

public partial class ChatBubble : UserControl
{
    public ChatBubble()
    {
        InitializeComponent();
    }

    public void SetMessage(string sender, string message, bool isUser)
    {
        SenderText.Text = sender;
        MessageText.Text = message;

        if (isUser)
        {
            BubbleBorder.HorizontalAlignment = HorizontalAlignment.Right;
            BubbleBorder.Background = (FindResource("AccentBrush") as Brush)!;
            SenderText.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            MessageText.Foreground = Brushes.White;
        }
        else
        {
            BubbleBorder.HorizontalAlignment = HorizontalAlignment.Left;
            BubbleBorder.Background = (FindResource("CardBrush") as Brush)!;
            SenderText.Foreground = (FindResource("TextMutedBrush") as Brush)!;
            MessageText.Foreground = (FindResource("TextPrimaryBrush") as Brush)!;
        }
    }

    /// <summary>
    /// Appends text to the message (for streaming responses).
    /// </summary>
    public void AppendText(string text)
    {
        MessageText.Text += text;
    }
}
