using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Services;

namespace Core.Views;

internal static class AppHomeDialog
{
    public static async Task<bool> Offer(MainWindow window, AppServices app)
    {
        if (app.Demo || app.Config.Ui.HomeOfferOff || !UpdateService.CanSelfInstall || Environment.ProcessPath is not { } exe) return false;
        if (AppHome.PassingThrough(exe) is not { } place) return false;
        if (await Ask(window, app, place) is not { } target) return false;
        App.RelaunchAfterExit("", AppHome.RemoveSteps(exe), target);
        return true;
    }

    private static async Task<string?> Ask(MainWindow window, AppServices app, PassingPlace place)
    {
        string name = AppIdentity.Name;
        string title = place == PassingPlace.Downloads ? $"{name} is running from Downloads" : $"{name} is running from a temporary folder";
        string risk = place == PassingPlace.Downloads ? "Cleaning up Downloads would delete it." : "Windows can empty that folder at any time.";

        string folder = AppHome.Folder;
        string? target = null;
        var content = new StackPanel();
        var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = "Install to", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var where = TextFit.TipWhenTrimmed(new TextBlock
        {
            Style = Ui.Res<Style>("Caption"), FontFamily = Ui.Res<FontFamily>("MonoFont"),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(where, 1);
        row.Children.Add(where);
        var error = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed };

        var card = window.DialogCard(title,
            $"{risk} The Start menu entry and Start with Windows follow it; your settings stay as they are.",
            out var buttons, content, error, width: 500);

        var never = new Button { Content = "Don't ask again", Margin = Ui.AfterButton, ToolTip = $"{name} stays where it is and won't offer this again" };
        var later = new Button { Content = "Not now", Margin = Ui.AfterButton, IsCancel = true };
        var move = new Button { Content = "Move", Style = Ui.Res<Style>("PrimaryButton"), IsDefault = true };
        var change = Ui.LabeledButton("I.Folder", "Change…", () =>
        {
            if (!External.Allowed("folder dialog")) return;
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = $"Where to install {name}", InitialDirectory = Directory.Exists(folder) ? folder : Path.GetDirectoryName(folder) };
            if (dialog.ShowDialog() == true) Show(AppHome.InstallFolderFor(dialog.FolderName));
        });
        change.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(change, 2);
        row.Children.Add(change);
        content.Children.Add(row);

        void Say(string? text)
        {
            error.Text = text ?? "";
            error.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        }

        void Show(string picked)
        {
            folder = picked;
            where.Text = picked;
            bool refused = AppHome.PassesThrough(picked);
            move.IsEnabled = !refused;
            if (refused) Say("Downloads and temporary folders get cleaned up. Pick another folder.");
            else if (AppIdentity.NeedsAdmin && StartupManager.OthersCanChangeFolder(picked))
                Say($"Other programs can change files there, so they could get administrator rights through Start with Windows. {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name)} doesn't have that gap.");
            else Say(null);
        }

        never.Click += (_, _) =>
        {
            app.Config.Ui.HomeOfferOff = true;
            app.Save();
            window.CloseModal(false);
        };
        later.Click += (_, _) => window.CloseModal(false);
        move.Click += async (_, _) =>
        {
            if (!External.Allowed($"move {name}")) return;
            foreach (var b in new[] { never, later, move, change }) b.IsEnabled = false;
            move.Content = "Moving…";
            string chosen = folder, failure = "";
            await Task.Run(() =>
            {
                try { target = AppHome.Move(chosen); }
                catch (UnauthorizedAccessException) when (!AppIdentity.NeedsAdmin) { failure = $"{name} runs without administrator rights, so it can't write there. Pick another folder."; }
                catch (Exception ex) { failure = ex.Message; }
            });
            if (target is not null)
            {
                window.CloseModal(true);
                return;
            }
            Say(failure);
            move.Content = "Move";
            foreach (var b in new[] { never, later, change }) b.IsEnabled = true;
            move.IsEnabled = true;
        };

        buttons.Children.Add(never);
        buttons.Children.Add(later);
        buttons.Children.Add(move);
        Show(folder);
        return await window.ShowModal(card) ? target : null;
    }
}
