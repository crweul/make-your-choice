using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MakeYourChoice
{
    internal enum PromptChoice { Primary, Secondary, Cancel }

    // Small Fluent (Win11-styled) modal dialogs used across the app. Built in code so each stays a
    // one-liner at the call site; they pick up the app-wide WPF-UI theme automatically.
    internal static class Dialogs
    {
        private static (Wpf.Ui.Controls.FluentWindow win, StackPanel body, StackPanel buttons) Create(
            Window owner, string title, double width)
        {
            owner ??= Application.Current?.MainWindow;
            var win = new Wpf.Ui.Controls.FluentWindow
            {
                Title = title,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                SizeToContent = SizeToContent.Height,
                Width = width,
                // FluentWindow ships a large default MinHeight; drop it so SizeToContent can shrink
                // the window down to just the title bar + content (no phantom whitespace).
                MinHeight = 0,
                MinWidth = 0,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ExtendsContentIntoTitleBar = true,
                WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
                WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round,
            };
            if (owner != null)
                try { win.Owner = owner; } catch { /* owner not shown yet */ }

            // WPF-UI FluentWindow measures SizeToContent BEFORE its extended-title-bar chrome is
            // applied, so the caption height gets tacked on as phantom whitespace at the bottom.
            // Re-running the height pass once loaded collapses that gap.
            win.Loaded += (_, __) =>
            {
                win.SizeToContent = SizeToContent.Manual;
                win.SizeToContent = SizeToContent.Height;
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBar = new Wpf.Ui.Controls.TitleBar
            {
                Title = title,
                ShowMaximize = false,
                ShowMinimize = false,
            };
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            var body = new StackPanel { Margin = new Thickness(20, 8, 20, 16) };
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };

            win.Content = root;
            return (win, body, buttons);
        }

        private static Wpf.Ui.Controls.Button MakeButton(string text, bool primary = false)
        {
            return new Wpf.Ui.Controls.Button
            {
                Content = text,
                Appearance = primary
                    ? Wpf.Ui.Controls.ControlAppearance.Primary
                    : Wpf.Ui.Controls.ControlAppearance.Secondary,
                Margin = new Thickness(8, 0, 0, 0),
            };
        }

        private static TextBlock MakeText(string text) => new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };

        /// <summary>
        /// Windows 11-style replacement for System.Windows.MessageBox.Show: Fluent window, accent
        /// primary button, semantic icon colors, dark/light theme support. Signature-compatible with
        /// the classic MessageBox so call sites alias it via `using MessageBox = MakeYourChoice.Dialogs`.
        /// Closing with X returns MessageBoxResult.None (never a positive result).
        /// </summary>
        public static MessageBoxResult Show(Window owner, string text, string caption,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            var (win, body, buttons) = Create(owner, caption, 420);
            var result = MessageBoxResult.None;

            // Message row: semantic icon (if any) + wrapping text
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (glyph, brushKey) = icon switch
            {
                MessageBoxImage.Error => ("", "SystemFillColorCriticalBrush"),
                MessageBoxImage.Warning => ("", "SystemFillColorCautionBrush"),
                MessageBoxImage.Question => ("", "SystemFillColorAttentionBrush"),
                MessageBoxImage.Information => ("", "SystemFillColorAttentionBrush"),
                _ => ((string)null, (string)null),
            };
            if (glyph != null)
            {
                var iconBlock = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
                    FontSize = 26,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 14, 0),
                };
                iconBlock.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
                Grid.SetColumn(iconBlock, 0);
                row.Children.Add(iconBlock);
            }

            var message = MakeText(text);
            Grid.SetColumn(message, 1);
            row.Children.Add(message);
            body.Children.Add(row);

            // Buttons for the requested set; the default result (or the affirmative one) is accent
            var defs = button switch
            {
                MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel) },
                MessageBoxButton.YesNo => new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No) },
                MessageBoxButton.YesNoCancel => new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel) },
                _ => new[] { ("OK", MessageBoxResult.OK) },
            };
            var accent = defs.Any(d => d.Item2 == defaultResult) ? defaultResult : defs[0].Item2;
            foreach (var (label, res) in defs)
            {
                var btn = MakeButton(label, primary: res == accent);
                if (res == accent) { btn.IsDefault = true; }
                if (res is MessageBoxResult.Cancel or MessageBoxResult.No) btn.IsCancel = true;
                var captured = res;
                btn.Click += (_, __) => { result = captured; win.Close(); };
                buttons.Children.Add(btn);
            }
            if (buttons.Children.Count > 0 && buttons.Children[0] is Wpf.Ui.Controls.Button first)
                first.Margin = new Thickness(0);
            body.Children.Add(buttons);

            win.ShowDialog();
            return result;
        }

        /// <summary>Three-way prompt (primary action / secondary action / cancel), used by the
        /// custom-splash-art and auto-skip-trailer features.</summary>
        public static PromptChoice ShowChoicePrompt(Window owner, string title, string message,
            string primaryText, string secondaryText)
        {
            var (win, body, buttons) = Create(owner, title, 440);
            var choice = PromptChoice.Cancel;

            body.Children.Add(MakeText(message));

            var btnPrimary = MakeButton(primaryText, primary: true);
            btnPrimary.Margin = new Thickness(0);
            btnPrimary.Click += (_, __) => { choice = PromptChoice.Primary; win.Close(); };
            var btnSecondary = MakeButton(secondaryText);
            btnSecondary.Click += (_, __) => { choice = PromptChoice.Secondary; win.Close(); };
            var btnCancel = MakeButton("Cancel");
            btnCancel.Click += (_, __) => win.Close();

            buttons.Children.Add(btnPrimary);
            buttons.Children.Add(btnSecondary);
            buttons.Children.Add(btnCancel);
            body.Children.Add(buttons);

            win.ShowDialog();
            return choice;
        }

        /// <summary>Conflicting-hosts-entries dialog. Returns false when the user cancels;
        /// clearConflicts reports the chosen radio option.</summary>
        public static bool ShowConflictDialog(Window owner, out bool clearConflicts)
        {
            var (win, body, buttons) = Create(owner, "Conflicting Hosts Entries Detected", 500);
            var proceed = false;

            body.Children.Add(MakeText(
                "It seems like there are conflicting entries in your hosts file.\n\n" +
                "This is usually caused by another program, or by manual changes.\n\n" +
                "It's best to resolve these issues first before applying a new configuration.\n" +
                "Would you like to clear out all conflicting entries?"));

            var rbClear = new RadioButton
            {
                Content = "Clear out conflicts, and apply selection (recommended)",
                IsChecked = true,
                Margin = new Thickness(0, 14, 0, 0),
            };
            var rbKeep = new RadioButton
            {
                Content = "Apply selection without clearing out conflicts",
                Margin = new Thickness(0, 6, 0, 0),
            };
            body.Children.Add(rbClear);
            body.Children.Add(rbKeep);

            var btnContinue = MakeButton("Continue", primary: true);
            btnContinue.Margin = new Thickness(0);
            btnContinue.Click += (_, __) => { proceed = true; win.Close(); };
            var btnCancel = MakeButton("Cancel");
            btnCancel.Click += (_, __) => win.Close();

            buttons.Children.Add(btnContinue);
            buttons.Children.Add(btnCancel);
            body.Children.Add(buttons);

            win.ShowDialog();

            clearConflicts = rbClear.IsChecked == true;
            if (!proceed)
                return false;

            // If user chose to keep conflicts, show confirmation
            if (!clearConflicts)
            {
                var confirm = Show(owner,
                    "Not clearing out conflicting entries will cause unexpected behavior.\n\n" +
                    "Are you sure you want to continue?",
                    "Confirm",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (confirm != MessageBoxResult.Yes)
                    return false;
            }

            return true;
        }

        /// <summary>Update-available prompt. ok=false when dismissed; otherwise updateNow, or the
        /// number of days to pause the automatic check.</summary>
        public static (bool ok, bool updateNow, int days) ShowUpdatePrompt(
            Window owner, string newVersion, string currentVersion)
        {
            var (win, body, buttons) = Create(owner, "Update Available", 380);
            var ok = false;

            body.Children.Add(MakeText(
                $"A new version is available: {newVersion}.\nWould you like to update?\n\nYour version: {currentVersion}."));

            var cbAction = new ComboBox
            {
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            cbAction.Items.Add(new ComboBoxItem { Content = "Update now" });
            cbAction.Items.Add(new ComboBoxItem { Content = "Ask again in 3 days" });
            cbAction.Items.Add(new ComboBoxItem { Content = "Ask again in 14 days" });
            cbAction.Items.Add(new ComboBoxItem { Content = "Ask again in 21 days" });
            cbAction.SelectedIndex = 0;
            body.Children.Add(cbAction);

            var btnContinue = MakeButton("Continue", primary: true);
            btnContinue.Margin = new Thickness(0);
            btnContinue.Click += (_, __) => { ok = true; win.Close(); };
            var btnNotNow = MakeButton("Not now");
            btnNotNow.Click += (_, __) => win.Close();

            buttons.Children.Add(btnContinue);
            buttons.Children.Add(btnNotNow);
            body.Children.Add(buttons);

            win.ShowDialog();

            var updateNow = cbAction.SelectedIndex == 0;
            var days = cbAction.SelectedIndex switch { 1 => 3, 2 => 14, 3 => 21, _ => 0 };
            return (ok, updateNow, days);
        }

        /// <summary>About dialog, opened by clicking the version number in the sidebar.</summary>
        public static void ShowAbout(Window owner, string version, string developer)
        {
            var (win, body, buttons) = Create(owner, "About Make Your Choice", 480);

            body.Children.Add(new TextBlock
            {
                Text = "Make Your Choice (DbD Server Selector)",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
            });

            // Developer label. This must always refer to the original developer. Changing this breaks license compliance.
            if (developer != null)
            {
                var link = new Hyperlink(new Run(developer));
                link.Click += (_, __) => MainWindow.OpenUrl("https://github.com/" + developer);
                var devText = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
                devText.Inlines.Add(new Run("Developer: "));
                devText.Inlines.Add(link);
                body.Children.Add(devText);
            }
            else
            {
                body.Children.Add(new TextBlock
                {
                    Text = "Developer: (unknown)",
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }

            body.Children.Add(new TextBlock
            {
                Text = $"Version {version}\nWindows 10 or higher.",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 8, 0, 0),
            });

            body.Children.Add(new Border
            {
                Height = 1,
                Background = SystemColors.ActiveBorderBrush,
                Margin = new Thickness(0, 12, 0, 12),
                Opacity = 0.5,
            });

            body.Children.Add(new TextBlock { Text = "Copyright © 2026" });

            var license = MakeText(
                "This program is free software licensed under the terms of the GNU General Public License. " +
                "This program is distributed in the hope that it will be useful, but without any warranty. " +
                "See the GNU General Public License for more details.");
            license.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(license);

            var btnOk = MakeButton("Awesome!", primary: true);
            btnOk.Margin = new Thickness(0);
            btnOk.Click += (_, __) => win.Close();
            buttons.Children.Add(btnOk);
            body.Children.Add(buttons);

            win.ShowDialog();
        }
    }
}
