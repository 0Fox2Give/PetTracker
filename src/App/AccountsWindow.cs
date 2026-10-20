namespace PetTracker;
public sealed class AccountsWindow : Window
{
    readonly Host host;
    public AccountsWindow(Host host)
    {
        this.host = host;
        Title = "Pet Tracker — Accounts";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        NativeTheme.Install(this);
        var box = new StackPanel { Margin = new Thickness(24) };
        Content = box;
        var w = this;
        box.Children.Add(Ui.Text("Accounts", 22));
        box.Children.Add(new TextBlock { Text = "Pets are shared across an account. Choose the account to track, or link your detected alt to its account.", FontSize = 12, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) });
        var choices = new ComboBox
        {
            ItemsSource = host.View?.Profiles,
            DisplayMemberPath = "Label",
            SelectedItem = host.View?.Profiles.FirstOrDefault(p => p.ProfileKey == host.View.Selected)
        };
        box.Children.Add(choices);
        var characters = Ui.Text("", 12, Ui.Muted);
        characters.TextWrapping = TextWrapping.Wrap;
        box.Children.Add(characters);
        string? detected = host.View?.DetectedCharacter;
        var link = Ui.Button("", async () =>
        {
            if (choices.SelectedItem is LocalProfileBinding target && detected != null && await host.Change(new("link", Key: target.ProfileKey, Name: detected)))
                w.Close();
        });
        box.Children.Add(link);
        void ShowTarget()
        {
            var target = choices.SelectedItem as LocalProfileBinding;
            characters.Text = target == null ? "Your first account connects automatically when Aion 2 sends its character record." : "Characters: " + string.Join(", ", new[] { target.CharacterName }.Concat(target.LinkedCharacters).Distinct(StringComparer.OrdinalIgnoreCase));
            link.Content = detected != null && target != null ? "Use " + detected + " with " + target.Label : "Waiting for character detection";
            link.IsEnabled = detected != null && target != null;
        }

        choices.SelectionChanged += (s, e) => ShowTarget();
        ShowTarget();
        box.Children.Add(Ui.Button("Use selected account", async () =>
        {
            if (choices.SelectedItem is LocalProfileBinding target && await host.Change(new("select", Key: target.ProfileKey)))
                w.Close();
        }));
        box.Children.Add(Ui.Button("Rename account", () =>
        {
            if (choices.SelectedItem is not LocalProfileBinding target)
                return;
            var form = new StackPanel();
            var rename = Dialog("Rename account", form);
            form.Children.Add(Ui.Text("Account name", 16));
            var name = new TextBox { Text = target.Label, MaxLength = 80, Margin = new Thickness(0, 12, 0, 16) };
            form.Children.Add(name);
            form.Children.Add(Ui.Button("Save name", async () =>
            {
                if (await host.Change(new("rename", Key: target.ProfileKey, AccountName: name.Text)))
                {
                    rename.Close();
                    w.Close();
                }
            }));
            rename.ShowDialog();
        }));
        box.Children.Add(Ui.Button("Add another account", () =>
        {
            var form = new StackPanel();
            var add = Dialog("Add account", form);
            form.Children.Add(new TextBlock { Text = "Create this only for another game account. Alts on your current account share its collection.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 0, 0, 16) });
            form.Children.Add(Ui.Text("Account name", 13));
            var label = new TextBox { MaxLength = 80, Margin = new Thickness(0, 6, 0, 16) };
            form.Children.Add(label);
            form.Children.Add(Ui.Text("First character name", 13));
            var first = new TextBox { MaxLength = 30, Margin = new Thickness(0, 6, 0, 16) };
            form.Children.Add(first);
            form.Children.Add(Ui.Button("Add account", async () =>
            {
                if (await host.Change(new("create", Name: first.Text, AccountName: label.Text)))
                {
                    add.Close();
                    w.Close();
                }
            }));
            add.ShowDialog();
        }));
    }

    Window Dialog(string title, Panel content, double width = 440)
    {
        var w = new Window
        {
            Title = title,
            Owner = this,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        NativeTheme.Install(w);
        content.Margin = new Thickness(24);
        w.Content = content;
        return w;
    }
}
