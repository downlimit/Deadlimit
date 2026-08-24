using System.Drawing;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class HeroCatalogFeature
{
    public static void Attach(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        if (projectGroup is null)
        {
            return;
        }

        var grid = projectGroup.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (grid?.GetControlFromPosition(1, 2) is not TextBox backingHeroText)
        {
            return;
        }

        var folderText = grid.GetControlFromPosition(1, 0) as TextBox;
        if (folderText is null)
        {
            return;
        }

        var combo = new HeroComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = true,
            MaxDropDownItems = 20,
            Margin = new Padding(0, 4, 8, 4),
        };
        var refreshButton = new Button
        {
            Text = UiText.T("REFRESH LIST", "ОБНОВИТЬ СПИСОК"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 5, 0),
        };
        var lockButton = new Button
        {
            AutoSize = false,
            Width = 29,
            Height = 23,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            TabStop = false,
            Font = new Font("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var heroActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        heroActions.Controls.Add(refreshButton);
        heroActions.Controls.Add(lockButton);

        grid.Controls.Remove(backingHeroText);
        grid.Controls.Add(combo, 1, 2);
        grid.Controls.Add(heroActions, 2, 2);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            AutoPopDelay = 7000,
        };
        toolTip.SetToolTip(
            refreshButton,
            UiText.T(
                "Reload hero names from the installed retail Deadlock build.",
                "Перечитать список героев из установленной retail-сборки Deadlock."));

        var syncing = false;
        var hasCatalog = false;
        var heroUnlocked = true;

        bool HasSavedProject()
        {
            var folder = folderText.Text.Trim();
            return Directory.Exists(folder)
                && File.Exists(ProjectStore.GetManifestPath(folder));
        }

        void UpdateLockVisual()
        {
            combo.Enabled = heroUnlocked;
            lockButton.Text = heroUnlocked ? "🔓" : "🔒";
            toolTip.SetToolTip(
                lockButton,
                heroUnlocked
                    ? UiText.T(
                        "Hero selection is unlocked. Click to lock it against accidental changes.",
                        "Выбор героя разблокирован. Нажмите, чтобы защитить его от случайной смены.")
                    : UiText.T(
                        "Hero selection is locked. Click to allow changing the project's hero.",
                        "Выбор героя заблокирован. Нажмите, чтобы разрешить смену героя проекта."));
        }

        void ResetLockForProject()
        {
            heroUnlocked = !HasSavedProject();
            UpdateLockVisual();
        }

        void SelectFromBacking()
        {
            if (syncing)
            {
                return;
            }

            syncing = true;
            try
            {
                var value = backingHeroText.Text.Trim();
                if (value.Length == 0)
                {
                    combo.SelectedIndex = -1;
                    return;
                }

                var match = combo.Items
                    .OfType<HeroCatalogEntry>()
                    .FirstOrDefault(hero =>
                        string.Equals(hero.LookupName, value, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(hero.DisplayName, value, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    match = new HeroCatalogEntry(value, value, string.Empty);
                    combo.Items.Add(match);
                }

                combo.SelectedItem = match;
            }
            finally
            {
                syncing = false;
            }
        }

        void ApplyCatalog(IReadOnlyList<HeroCatalogEntry> heroes)
        {
            var currentValue = backingHeroText.Text.Trim();

            syncing = true;
            try
            {
                combo.BeginUpdate();
                try
                {
                    combo.Items.Clear();
                    foreach (var hero in heroes.OrderBy(hero => hero.DisplayName, StringComparer.OrdinalIgnoreCase))
                    {
                        combo.Items.Add(hero);
                    }
                }
                finally
                {
                    combo.EndUpdate();
                }
            }
            finally
            {
                syncing = false;
            }

            hasCatalog = heroes.Count > 0;
            if (!string.Equals(backingHeroText.Text, currentValue, StringComparison.Ordinal))
            {
                backingHeroText.Text = currentValue;
            }

            SelectFromBacking();
        }

        combo.SelectedIndexChanged += (_, _) =>
        {
            if (syncing || !heroUnlocked)
            {
                return;
            }

            syncing = true;
            try
            {
                backingHeroText.Text = combo.SelectedItem is HeroCatalogEntry hero
                    ? hero.LookupName
                    : string.Empty;
            }
            finally
            {
                syncing = false;
            }
        };
        backingHeroText.TextChanged += (_, _) => SelectFromBacking();
        folderText.TextChanged += (_, _) => ResetLockForProject();
        lockButton.Click += (_, _) =>
        {
            heroUnlocked = !heroUnlocked;
            UpdateLockVisual();
        };

        var saveButton = grid.Controls
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Text, "SAVE PROJECT", StringComparison.Ordinal)
                || string.Equals(button.Text, "СОХРАНИТЬ ПРОЕКТ", StringComparison.Ordinal));
        if (saveButton is not null)
        {
            saveButton.Click += (_, _) => form.BeginInvoke((Action)(() =>
            {
                var folder = folderText.Text.Trim();
                var saved = Directory.Exists(folder) ? ProjectStore.TryLoad(folder) : null;
                if (saved is not null
                    && string.Equals(saved.Hero, backingHeroText.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    heroUnlocked = false;
                    UpdateLockVisual();
                }
            }));
        }

        async Task RefreshCatalogAsync(bool showErrors)
        {
            refreshButton.Enabled = false;
            SetStatus(form, UiText.T(
                "Refreshing hero list from retail Deadlock...",
                "Обновление списка героев из retail Deadlock..."));

            try
            {
                var service = new HeroCatalogService(new DeadlimitPaths());
                var heroes = await service.RefreshAsync();
                ApplyCatalog(heroes);
                SetStatus(form, UiText.T(
                    $"Hero list updated: {heroes.Count} heroes.",
                    $"Список героев обновлён: {heroes.Count}."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                SetStatus(form, UiText.T(
                    "Hero list refresh failed. The previous list was kept.",
                    "Не удалось обновить список героев. Предыдущий список сохранён."));

                if (showErrors)
                {
                    MessageBox.Show(
                        form,
                        ex.Message,
                        UiText.T("Could not refresh hero list", "Не удалось обновить список героев"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        }

        refreshButton.Click += async (_, _) => await RefreshCatalogAsync(showErrors: true);

        var cached = HeroCatalogService.LoadCached();
        if (cached.Count > 0)
        {
            ApplyCatalog(cached);
        }

        ResetLockForProject();

        form.Shown += async (_, _) =>
        {
            ResetLockForProject();
            if (!hasCatalog)
            {
                await RefreshCatalogAsync(showErrors: false);
            }
        };
    }

    private static void SetStatus(MainForm form, string message)
    {
        var status = FindDescendants<StatusStrip>(form)
            .SelectMany(strip => strip.Items.OfType<ToolStripStatusLabel>())
            .FirstOrDefault();
        if (status is not null)
        {
            status.Text = message;
        }
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class HeroComboBox : ComboBox
    {
        private const int WmMouseWheel = 0x020A;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseWheel && !DroppedDown)
            {
                return;
            }

            base.WndProc(ref m);
        }
    }
}
