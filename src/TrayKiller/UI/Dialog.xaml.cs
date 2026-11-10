using System.Windows;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    /// <summary>Small themed message / confirmation dialog.</summary>
    public partial class Dialog : Window
    {
        Dialog(Window owner, string title, string body, string ok, string cancel, bool danger, bool info)
        {
            InitializeComponent();
            if (owner != null && owner.IsVisible) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = title;
            TitleText.Text = title;
            BodyText.Text = body;
            OkButton.Content = ok;
            if (cancel == null) CancelButton.Visibility = Visibility.Collapsed;
            else CancelButton.Content = cancel;
            if (info)
            {
                IconGlyph.Text = "";
                IconGlyph.Foreground = (System.Windows.Media.Brush)FindResource("B.Blue");
                IconBg.Background = (System.Windows.Media.Brush)FindResource("B.BlueSoft");
            }
            else if (!danger)
            {
                IconGlyph.Text = "";
                IconGlyph.Foreground = (System.Windows.Media.Brush)FindResource("B.Accent2");
                IconBg.Background = (System.Windows.Media.Brush)FindResource("B.AmberSoft");
            }
            SourceInitialized += (s, e) => WindowEffects.Apply(this);
        }

        public static bool Confirm(Window owner, string title, string body, string ok, string cancel, bool danger)
        {
            var d = new Dialog(owner, title, body, ok, cancel, danger, false);
            return d.ShowDialog() == true;
        }

        public static void Info(Window owner, string title, string body)
        {
            new Dialog(owner, title, body, Loc.T("btn.ok"), null, false, true).ShowDialog();
        }

        void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
