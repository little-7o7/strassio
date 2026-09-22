#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Strassio.Core.Localization;
using Strassio.Corel.Themes;
using Strassio.Licensing;
using CorelApplication = Corel.Interop.VGCore.Application;

namespace Strassio.Corel
{
    /// <summary>
    /// Окно «Лицензия» (docs/SPEC.md, раздел 13): состояние, код компьютера, ввод ключа, пробный
    /// период, перенос на этот компьютер, освобождение, проверка и офлайн-файл от автора.
    /// Строится кодом, как NameDialog; тексты — из файлов языков, цвета — тема CorelDRAW.
    /// </summary>
    internal sealed class LicenseWindow : Window
    {
        private readonly PluginContext context = PluginContext.Instance;
        private readonly TextBlock stateText = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock detailText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock betaText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock computerCaption = new TextBlock { Margin = new Thickness(0, 14, 0, 4) };
        private readonly TextBox computerBox = new TextBox { IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button copyButton = new Button { Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        private readonly TextBlock serialCaption = new TextBlock { Margin = new Thickness(0, 14, 0, 4) };
        private readonly TextBox serialBox = new TextBox { FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button activateButton = new Button { Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        private readonly Button trialButton = new Button { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) };
        private readonly Button checkButton = new Button { Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3) };
        private readonly Button releaseButton = new Button { Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3) };
        private readonly Button importButton = new Button { Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3) };
        private readonly TextBlock messageText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly Button closeButton = new Button { IsCancel = true, MinWidth = 80 };
        private bool busy;
        private string? messageKey;
        private object[] messageArgs = Array.Empty<object>();

        public LicenseWindow(CorelApplication? app)
        {
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Strassio.Corel;component/Themes/Styles.xaml", UriKind.Relative),
            });
            SetResourceReference(BackgroundProperty, "Strassio.Background");
            SetResourceReference(ForegroundProperty, "Strassio.Foreground");
            try
            {
                if (app != null)
                {
                    new WindowInteropHelper(this).Owner = new IntPtr(app.AppWindow.Handle);
                }
            }
            catch (Exception)
            {
                // Без владельца окно просто откроется посреди экрана.
            }

            betaText.SetResourceReference(StyleProperty, "Strassio.Muted");
            detailText.SetResourceReference(StyleProperty, "Strassio.Muted");
            activateButton.SetResourceReference(StyleProperty, "Strassio.PrimaryButton");

            var computerRow = new DockPanel();
            DockPanel.SetDock(copyButton, Dock.Right);
            computerRow.Children.Add(copyButton);
            computerRow.Children.Add(computerBox);

            var serialRow = new DockPanel();
            DockPanel.SetDock(activateButton, Dock.Right);
            serialRow.Children.Add(activateButton);
            serialRow.Children.Add(serialBox);

            var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            actions.Children.Add(checkButton);
            actions.Children.Add(releaseButton);
            actions.Children.Add(importButton);

            var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
            DockPanel.SetDock(closeButton, Dock.Right);
            bottom.Children.Add(closeButton);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(stateText);
            root.Children.Add(detailText);
            root.Children.Add(betaText);
            root.Children.Add(computerCaption);
            root.Children.Add(computerRow);
            root.Children.Add(serialCaption);
            root.Children.Add(serialRow);
            root.Children.Add(trialButton);
            root.Children.Add(actions);
            root.Children.Add(messageText);
            root.Children.Add(bottom);
            Content = root;

            copyButton.Click += (s, e) => CopyComputerCode();
            activateButton.Click += async (s, e) => await ActivateKey();
            serialBox.KeyDown += async (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    await ActivateKey();
                }
            };
            trialButton.Click += async (s, e) => await Run(() => context.License.StartTrialAsync(), "license.done.trial");
            checkButton.Click += async (s, e) => await Check();
            releaseButton.Click += async (s, e) => await Release();
            importButton.Click += (s, e) => ImportFile();

            Loc.PropertyChanged += Localizer_PropertyChanged;
            context.License.Changed += License_Changed;
            ThemeManager.Attach(this, app);
            Closed += (s, e) =>
            {
                Loc.PropertyChanged -= Localizer_PropertyChanged;
                context.License.Changed -= License_Changed;
                ThemeManager.Detach(this);
            };

            busy = true;
            Refresh();
            Loaded += async (s, e) => await LoadComputerCode();
        }

        private Localizer Loc => context.Localizer;

        private void Localizer_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

        private void License_Changed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh));

        /// <summary>Код компьютера считается через WMI — не в потоке окна, чтобы оно не замирало.</summary>
        private async Task LoadComputerCode()
        {
            try
            {
                await Task.Run(() => context.License.Computer);
            }
            catch (Exception)
            {
                // Нечитаемые признаки становятся «неизвестно» внутри WmiHardwareSource; сюда попасть не должны.
            }

            busy = false;
            Refresh();
        }

        /// <summary>Все надписи и доступность кнопок — по текущему состоянию лицензии.</summary>
        private void Refresh()
        {
            Title = Loc["license.title"];
            computerCaption.Text = Loc["license.computer"];
            copyButton.Content = Loc["license.copy"];
            copyButton.ToolTip = Loc["license.copy.tooltip"];
            serialCaption.Text = Loc["license.serial"];
            activateButton.Content = Loc["license.activate"];
            trialButton.Content = Loc["license.trial"];
            trialButton.ToolTip = Loc["license.trial.tooltip"];
            checkButton.Content = Loc["license.check"];
            checkButton.ToolTip = Loc["license.check.tooltip"];
            releaseButton.Content = Loc["license.release"];
            releaseButton.ToolTip = Loc["license.release.tooltip"];
            importButton.Content = Loc["license.import"];
            importButton.ToolTip = Loc["license.import.tooltip"];
            closeButton.Content = Loc["license.close"];
            betaText.Text = Loc["license.beta"];
            betaText.Visibility = PluginContext.LicenseEnforced ? Visibility.Collapsed : Visibility.Visible;
            foreach (Button b in new[] { activateButton, trialButton, checkButton, releaseButton, importButton, copyButton })
            {
                b.IsEnabled = !busy;
            }

            messageText.Text = messageKey == null ? string.Empty : Loc.Format(messageKey, messageArgs);
            messageText.Visibility = messageText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (computerBox.Text.Length == 0 && busy)
            {
                // Код компьютера ещё считается (первое открытие за сеанс).
                stateText.Text = Loc["license.working"];
                detailText.Visibility = Visibility.Collapsed;
                trialButton.Visibility = checkButton.Visibility = releaseButton.Visibility = Visibility.Collapsed;
                return;
            }

            LicenseStatus status = context.License.Status;
            computerBox.Text = context.License.Computer.Display;
            stateText.Text = StateText(status);
            detailText.Text = DetailText(status);
            detailText.Visibility = detailText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            bool hasLicense = status.License != null && status.State != LicenseState.BadSignature;
            bool full = hasLicense && !status.License!.IsTrial;
            trialButton.Visibility = status.State == LicenseState.None ? Visibility.Visible : Visibility.Collapsed;
            checkButton.Visibility = hasLicense ? Visibility.Visible : Visibility.Collapsed;
            releaseButton.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
            if (full && serialBox.Text.Length == 0)
            {
                serialBox.Text = status.License!.Serial;
            }
        }

        private string StateText(LicenseStatus status)
        {
            switch (status.State)
            {
                case LicenseState.Valid:
                    return Loc["license.state.valid"];
                case LicenseState.Trial:
                    return Loc.Format("license.state.trial", status.DaysLeft ?? 0);
                case LicenseState.Expired:
                    return Loc[status.License?.IsTrial == true ? "license.state.trialExpired" : "license.state.expired"];
                case LicenseState.WrongComputer:
                    return Loc["license.state.wrongComputer"];
                case LicenseState.BadSignature:
                    return Loc["license.state.bad"];
                case LicenseState.OfflineTooLong:
                    return Loc["license.state.offline"];
                case LicenseState.ClockTampered:
                    return Loc["license.state.clock"];
                default:
                    return context.License.LastProblem != null ? Loc["license.error." + context.License.LastProblem] : Loc["license.state.none"];
            }
        }

        private string DetailText(LicenseStatus status)
        {
            LicenseData? license = status.License;
            if (license == null || license.IsTrial || status.State == LicenseState.BadSignature)
            {
                return string.Empty;
            }

            string until = license.ExpiresUtc.HasValue
                ? license.ExpiresUtc.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)
                : Loc["license.forever"];
            return Loc.Format("license.detail", license.Serial, until);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            Cursor = value ? System.Windows.Input.Cursors.Wait : null;
            if (value)
            {
                messageKey = "license.working";
                messageArgs = Array.Empty<object>();
            }

            Refresh();
        }

        private void ShowMessage(string? key, params object[] args)
        {
            messageKey = key;
            messageArgs = args;
            Refresh();
        }

        /// <summary>Запрос к серверу с «занято» на время ожидания и сообщением об итоге.</summary>
        private async Task<LicenseActionResult?> Run(Func<Task<LicenseActionResult>> action, string doneKey)
        {
            if (busy)
            {
                return null;
            }

            SetBusy(true);
            LicenseActionResult result;
            try
            {
                result = await action();
            }
            catch (Exception)
            {
                result = new LicenseActionResult(false, ApiReply.NoConnection, null);
            }

            SetBusy(false);
            ShowMessage(result.Ok ? doneKey : "license.error." + result.Error);
            return result;
        }

        private async Task ActivateKey()
        {
            string serial = serialBox.Text;
            LicenseActionResult? result = await Run(() => context.License.ActivateAsync(serial), "license.done.activated");
            if (result == null || !result.CanTransfer)
            {
                return;
            }

            // Ключ занят другим компьютером — предлагаем перенос (SPEC 13.4).
            MessageBoxResult answer = MessageBox.Show(
                this, Loc.Format("license.transfer.ask", result.TransfersLeft ?? 0), Loc["license.title"], MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                await Run(() => context.License.TransferAsync(serial), "license.done.transferred");
            }
        }

        private async Task Check()
        {
            if (busy)
            {
                return;
            }

            SetBusy(true);
            bool changed = false;
            try
            {
                changed = await context.License.CheckAsync(force: true);
            }
            catch (Exception)
            {
                // Нет связи — состояние не меняется.
            }

            SetBusy(false);
            ShowMessage(context.License.LastProblem != null ? "license.error." + context.License.LastProblem :
                context.License.Status.State == LicenseState.ClockTampered ? "license.error.clock" :
                changed ? "license.done.checked" : "license.error.no_connection");
        }

        private async Task Release()
        {
            MessageBoxResult answer = MessageBox.Show(
                this, Loc["license.release.ask"], Loc["license.title"], MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                await Run(() => context.License.DeactivateAsync(), "license.done.released");
            }
        }

        /// <summary>
        /// В буфер — полный код (4 части через точку): его автор вставляет в админку для офлайн-активации.
        /// Короткий XXXX-XXXX-XXXX-XXXX в окне — только чтобы узнать компьютер на глаз.
        /// </summary>
        private void CopyComputerCode()
        {
            try
            {
                Clipboard.SetText(context.License.Computer.ToStorage());
                ShowMessage("license.copied");
            }
            catch (Exception)
            {
                // Буфер обмена занят другой программой — пользователь нажмёт ещё раз.
            }
        }

        private void ImportFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc["license.import.filter"] };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string text;
            try
            {
                text = File.ReadAllText(dialog.FileName);
            }
            catch (Exception)
            {
                ShowMessage("license.error.bad_file");
                return;
            }

            LicenseActionResult result = context.License.Import(text);
            ShowMessage(result.Ok ? "license.done.imported" : "license.error." + result.Error);
        }
    }
}
