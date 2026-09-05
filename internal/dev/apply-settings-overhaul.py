from pathlib import Path
import re
import textwrap

ROOT = Path(__file__).resolve().parents[2]


def load(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def save(rel, text):
    path = ROOT / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, got {count}")
    return text.replace(old, new, 1)


def sub_once(text, pattern, replacement, label, flags=re.S):
    new, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one regex match, got {count}")
    return new


settings_path = "internal/src/Deadlimit/App/SettingsForm.cs"
s = load(settings_path)

s = replace_once(
    s,
    "    private bool _busy;\n    private bool _themePreviewApplied;",
    "    private bool _busy;\n    private bool _themePreviewApplied;\n    private CancellationTokenSource? _csdkCheckCancellation;\n    private CancellationTokenSource? _deadlockToolsCheckCancellation;\n    private int _retailCheckGeneration;",
    "settings cancellation fields")

s = replace_once(
    s,
    "        if (disposing)\n        {\n            _toolTip.Dispose();\n        }",
    "        if (disposing)\n        {\n            _csdkCheckCancellation?.Cancel();\n            _csdkCheckCancellation?.Dispose();\n            _deadlockToolsCheckCancellation?.Cancel();\n            _deadlockToolsCheckCancellation?.Dispose();\n            _toolTip.Dispose();\n        }",
    "settings dispose cancellations")

s = replace_once(s, "            RowCount = 3,", "            RowCount = 2,", "settings root row count")
s = replace_once(
    s,
    "        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));\n        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));\n        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));",
    "        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));\n        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));",
    "settings root row styles")

s = sub_once(
    s,
    r"\n        root\.Controls\.Add\(new Label\n        \{\n            AutoSize = true,\n            Dock = DockStyle\.Fill,\n            Text = UiText\.T\(\n                \"Tool status is checked when this window opens\.[\s\S]*?\n        \}, 0, 0\);\n",
    "\n",
    "remove settings explainer")

s = replace_once(s, "        root.Controls.Add(content, 0, 1);", "        root.Controls.Add(content, 0, 0);", "settings content row")

footer_pattern = r"        var footer = new TableLayoutPanel\n        \{[\s\S]*?        root\.Controls\.Add\(footer, 0, 2\);"
footer_replacement = textwrap.dedent(r'''\
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 30,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var friendlyVersionLabel = new Label
        {
            Text = $"v{GetFriendlyVersion()}",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 0, 0),
        };

        var footerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };

        _closeCancelButton.Text = UiText.T("CLOSE", "ЗАКРЫТЬ");
        _applyButton.Text = UiText.T("APPLY", "ПРИМЕНИТЬ");
        _applyButton.Click += (_, _) => ApplySettings();

        _toolTip.SetToolTip(
            _applyButton,
            UiText.T(
                "Validate and apply the changed folders and interface settings.\n\nUnspecified external-tool paths are allowed.",
                "Проверить и применить изменённые папки и настройки интерфейса.\n\nПути к внешним инструментам можно оставить неуказанными."));

        _applyButton.Margin = new Padding(0, 0, 8, 0);
        _closeCancelButton.Margin = Padding.Empty;
        footerActions.Controls.Add(_applyButton);
        footerActions.Controls.Add(_closeCancelButton);
        footer.Controls.Add(friendlyVersionLabel, 0, 0);
        footer.Controls.Add(footerActions, 1, 0);
        root.Controls.Add(footer, 0, 1);''').rstrip()
s = sub_once(s, footer_pattern, footer_replacement, "settings footer replacement")

s = replace_once(
    s,
    "    private static bool ButtonTextFits(Button button)\n    {\n        var measured = TextRenderer.MeasureText(button.Text, button.Font);\n        return measured.Width + 12 <= button.ClientSize.Width;\n    }",
    "    private static bool ButtonTextFits(Button button)\n    {\n        var measured = TextRenderer.MeasureText(button.Text, button.Font);\n        return measured.Width + 12 <= button.ClientSize.Width;\n    }\n\n    private static string GetFriendlyVersion()\n    {\n        var version = Application.ProductVersion?.Trim() ?? \"0.0.0\";\n        var metadataSeparator = version.IndexOf('+');\n        return metadataSeparator >= 0 ? version[..metadataSeparator] : version;\n    }",
    "friendly version helper")

s = replace_once(
    s,
    "        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));\n        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 388));",
    "        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));\n        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 328));",
    "settings status/path widths")

retail_click_old = '''        _retailDeadlockCheckButton.Text = UiText.T("CHECK", "ПРОВЕРИТЬ");
        _retailDeadlockCheckButton.Click += async (_, _) =>
        {
            SetRetailStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
            RefreshDeadlockGameStatus();
        };'''
retail_click_new = '''        _retailDeadlockCheckButton.Text = UiText.T("CHECK", "ПРОВЕРИТЬ");
        _retailDeadlockCheckButton.Click += async (_, _) =>
        {
            if (_retailDeadlockStatus.Kind == ToolchainStatusKind.Checking)
            {
                _retailCheckGeneration++;
                SetRetailStatus(new ToolchainStatus(
                    ToolchainStatusKind.Cancelled,
                    UiText.T("Deadlock client check cancelled.", "Проверка Deadlock клиента отменена.")));
                return;
            }

            var generation = ++_retailCheckGeneration;
            SetRetailStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
            if (generation == _retailCheckGeneration)
            {
                RefreshDeadlockGameStatus();
            }
        };'''
s = replace_once(s, retail_click_old, retail_click_new, "retail check cancellation")

refresh_csdk = r'''    private async Task RefreshCsdkStatusAsync(bool skipCheckingState = false)
    {
        _csdkCheckCancellation?.Cancel();
        _csdkCheckCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _csdkCheckCancellation = cancellation;

        if (!skipCheckingState)
        {
            SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
        }

        try
        {
            var status = await _toolchain.CheckCsdkAsync(_csdkRootText.Text.Trim(), cancellation.Token);
            if (ReferenceEquals(_csdkCheckCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                SetCsdkStatus(status);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_csdkCheckCancellation, cancellation))
            {
                SetCsdkStatus(new ToolchainStatus(
                    ToolchainStatusKind.Cancelled,
                    UiText.T("Reduced CSDK check cancelled.", "Проверка Reduced CSDK отменена.")));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(_csdkCheckCancellation, cancellation))
            {
                SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
            }
        }
        finally
        {
            if (ReferenceEquals(_csdkCheckCancellation, cancellation))
            {
                _csdkCheckCancellation = null;
                cancellation.Dispose();
                UpdateActionAvailability();
            }
        }
    }

    private bool CancelCsdkCheck()
    {
        if (_csdkCheckCancellation is null)
        {
            return false;
        }

        _csdkCheckCancellation.Cancel();
        SetCsdkStatus(new ToolchainStatus(
            ToolchainStatusKind.Cancelled,
            UiText.T("Reduced CSDK check cancelled.", "Проверка Reduced CSDK отменена.")));
        return true;
    }
'''
s = sub_once(
    s,
    r"    private async Task RefreshCsdkStatusAsync\(bool skipCheckingState = false\)\n    \{[\s\S]*?\n    \}\n\n(?=    private async Task RefreshDeadlockToolsStatusAsync)",
    refresh_csdk,
    "replace csdk refresh")

refresh_tools = r'''    private async Task RefreshDeadlockToolsStatusAsync(bool skipCheckingState = false)
    {
        _deadlockToolsCheckCancellation?.Cancel();
        _deadlockToolsCheckCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _deadlockToolsCheckCancellation = cancellation;

        if (!skipCheckingState)
        {
            SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
        }

        try
        {
            var status = await _toolchain.CheckDeadlockToolsAsync(_deadlockToolsRootText.Text.Trim(), cancellation.Token);
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                SetDeadlockToolsStatus(status);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation))
            {
                SetDeadlockToolsStatus(new ToolchainStatus(
                    ToolchainStatusKind.Cancelled,
                    UiText.T("DeadlockTools check cancelled.", "Проверка DeadlockTools отменена.")));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation))
            {
                SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
            }
        }
        finally
        {
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation))
            {
                _deadlockToolsCheckCancellation = null;
                cancellation.Dispose();
                UpdateActionAvailability();
            }
        }
    }

    private bool CancelDeadlockToolsCheck()
    {
        if (_deadlockToolsCheckCancellation is null)
        {
            return false;
        }

        _deadlockToolsCheckCancellation.Cancel();
        SetDeadlockToolsStatus(new ToolchainStatus(
            ToolchainStatusKind.Cancelled,
            UiText.T("DeadlockTools check cancelled.", "Проверка DeadlockTools отменена.")));
        return true;
    }
'''
s = sub_once(
    s,
    r"    private async Task RefreshDeadlockToolsStatusAsync\(bool skipCheckingState = false\)\n    \{[\s\S]*?\n    \}\n\n(?=    private void RefreshDeadlockGameStatus)",
    refresh_tools,
    "replace deadlocktools refresh")

s = replace_once(
    s,
    "        _csdkPrimaryButton.Enabled = !_busy && _csdkStatus.Kind is not ToolchainStatusKind.Checking and not ToolchainStatusKind.Working;\n        _deadlockToolsPrimaryButton.Enabled = !_busy && _deadlockToolsStatus.Kind is not ToolchainStatusKind.Checking and not ToolchainStatusKind.Working;\n        _retailDeadlockCheckButton.Enabled = !_busy && _retailDeadlockStatus.Kind != ToolchainStatusKind.Checking;",
    "        _csdkPrimaryButton.Enabled = !_busy && _csdkStatus.Kind is not ToolchainStatusKind.Working;\n        _deadlockToolsPrimaryButton.Enabled = !_busy && _deadlockToolsStatus.Kind is not ToolchainStatusKind.Working;\n        _retailDeadlockCheckButton.Enabled = !_busy;",
    "keep checking buttons enabled")

s = replace_once(
    s,
    "    private async Task HandleCsdkPrimaryActionAsync()\n    {\n        if (!_allowUnverifiedToolchainAutomation)",
    "    private async Task HandleCsdkPrimaryActionAsync()\n    {\n        if (_csdkStatus.Kind == ToolchainStatusKind.Checking)\n        {\n            CancelCsdkCheck();\n            return;\n        }\n\n        if (!_allowUnverifiedToolchainAutomation)",
    "csdk click cancels checking")

s = replace_once(
    s,
    "        await RunBusyOperationAsync(\n            async _ => await RefreshCsdkStatusAsync(),\n            UiText.T(\"Could not check Reduced CSDK\", \"Не удалось проверить Reduced CSDK\"));",
    "        await RefreshCsdkStatusAsync();",
    "csdk manual check no busy lock")

s = replace_once(
    s,
    "    private async Task HandleDeadlockToolsPrimaryActionAsync()\n    {\n        if (!_allowUnverifiedToolchainAutomation)",
    "    private async Task HandleDeadlockToolsPrimaryActionAsync()\n    {\n        if (_deadlockToolsStatus.Kind == ToolchainStatusKind.Checking)\n        {\n            CancelDeadlockToolsCheck();\n            return;\n        }\n\n        if (!_allowUnverifiedToolchainAutomation)",
    "deadlocktools click cancels checking")

s = replace_once(
    s,
    "        await RunBusyOperationAsync(\n            async _ => await RefreshDeadlockToolsStatusAsync(),\n            UiText.T(\"Could not check DeadlockTools\", \"Не удалось проверить DeadlockTools\"));",
    "        await RefreshDeadlockToolsStatusAsync();",
    "deadlocktools manual check no busy lock")

s = replace_once(
    s,
    "            ToolchainStatusKind.NetworkIssue => $\"! {UiText.T(\"Network issue\", \"Ошибка сети\")}\",\n            ToolchainStatusKind.Checking => $\"↻ {UiText.T(\"Checking…\", \"Проверка…\")}\",",
    "            ToolchainStatusKind.NetworkIssue => $\"! {UiText.T(\"Network issue\", \"Ошибка сети\")}\",\n            ToolchainStatusKind.Cancelled => $\"○ {UiText.T(\"Check cancelled\", \"Проверка отменена\")}\",\n            ToolchainStatusKind.Checking => $\"↻ {UiText.T(\"Checking…\", \"Проверка…\")}\",",
    "cancelled status text")

save(settings_path, s)

# Toolchain cancellation must propagate instead of becoming a network error.
tool_path = "internal/src/Deadlimit/Core/ToolchainDependencyService.cs"
t = load(tool_path)
t = replace_once(t, "    NetworkIssue,\n    Checking,", "    NetworkIssue,\n    Cancelled,\n    Checking,", "toolchain cancelled enum")
t = replace_once(
    t,
    "        catch (Exception exception) when (IsNetworkException(exception))\n        {\n            return new(\n                ToolchainStatusKind.NetworkIssue,\n                \"CSDK is installed, but freshness could not be checked because the update source is unavailable.\");\n        }",
    "        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n        {\n            throw;\n        }\n        catch (Exception exception) when (IsNetworkException(exception))\n        {\n            return new(\n                ToolchainStatusKind.NetworkIssue,\n                \"CSDK is installed, but freshness could not be checked because the update source is unavailable.\");\n        }",
    "csdk cancellation propagation")
t = replace_once(
    t,
    "        catch (Exception exception) when (IsNetworkException(exception))\n        {\n            return new(\n                ToolchainStatusKind.NetworkIssue,\n                \"DeadlockTools is installed, but freshness could not be checked because GitHub is unavailable.\",\n                InstalledVersion: installedRelease);\n        }",
    "        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n        {\n            throw;\n        }\n        catch (Exception exception) when (IsNetworkException(exception))\n        {\n            return new(\n                ToolchainStatusKind.NetworkIssue,\n                \"DeadlockTools is installed, but freshness could not be checked because GitHub is unavailable.\",\n                InstalledVersion: installedRelease);\n        }",
    "deadlocktools cancellation propagation")
save(tool_path, t)

# Replace manager row/state feature with path/open/browse/relocation and cancellable checks.
version_path = "internal/src/Deadlimit/App/SettingsVersionFeature.cs"
v = load(version_path)
v = replace_once(
    v,
    "    private const string VersionValueName = \"DeadlimitManagerVersionValue\";\n    private const string UpdateButtonName = \"DeadlimitUpdateButton\";",
    "    private const string ManagerPathName = \"DeadlimitManagerPath\";\n    private const string OpenButtonName = \"DeadlimitManagerOpenButton\";\n    private const string BrowseButtonName = \"DeadlimitManagerBrowseButton\";\n    private const string UpdateButtonName = \"DeadlimitUpdateButton\";",
    "manager row control names")

add_manager = r'''    internal static void AddManagerRow(TableLayoutPanel grid, int row)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var status = new Label
        {
            Name = VersionStatusName,
            Text = $"↻ {UiText.T("Checking…", "Проверка…")} [main]",
            AutoSize = false,
            Width = 199,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 6, 4),
        };
        var managerPath = new TextBox
        {
            Name = ManagerPathName,
            Text = DeadlimitPaths.DefaultDeadlimitRoot,
            ReadOnly = true,
            TabStop = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 6, 4),
        };
        var openButton = new Button
        {
            Name = OpenButtonName,
            Text = "📂",
            AutoSize = false,
            Width = 28,
            Height = 24,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 4, 4),
            Padding = Padding.Empty,
            TabStop = false,
            Font = new Font("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };
        var browseButton = new Button
        {
            Name = BrowseButtonName,
            Text = UiText.T("BROWSE…", "ОБЗОР…"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 4),
        };
        var action = new Button
        {
            Name = UpdateButtonName,
            Text = UiText.T("CHECKING…", "ПРОВЕРКА…"),
            AutoSize = false,
            Width = 94,
            Height = 26,
            Enabled = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 5, 3),
        };

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = "Deadlimit Manager:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 8),
        }, 0, row);
        grid.Controls.Add(status, 1, row);
        grid.Controls.Add(managerPath, 2, row);
        grid.Controls.Add(openButton, 3, row);
        grid.Controls.Add(browseButton, 4, row);
        grid.Controls.Add(action, 5, row);
        grid.Controls.Add(CreateSpacer(), 6, row);
    }

'''
v = sub_once(v, r"    internal static void AddManagerRow\(TableLayoutPanel grid, int row\)\n    \{[\s\S]*?\n    \}\n\n(?=    private static void OnApplicationIdle)", add_manager, "replace manager row")

ensure_manager = r'''    private static void EnsureManagerStatus(SettingsForm form)
    {
        if (PreparedForms.TryGetValue(form, out _))
        {
            return;
        }

        var toolGrid = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel => panel.ColumnCount == 7 && panel.RowCount >= 5);
        if (toolGrid is null)
        {
            return;
        }

        var versionStatus = FindDescendants<Label>(toolGrid).FirstOrDefault(label =>
            string.Equals(label.Name, VersionStatusName, StringComparison.Ordinal));
        var managerPath = FindDescendants<TextBox>(toolGrid).FirstOrDefault(textBox =>
            string.Equals(textBox.Name, ManagerPathName, StringComparison.Ordinal));
        var openButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, OpenButtonName, StringComparison.Ordinal));
        var browseButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, BrowseButtonName, StringComparison.Ordinal));
        var updateButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, UpdateButtonName, StringComparison.Ordinal));
        if (versionStatus is null || managerPath is null || openButton is null || browseButton is null || updateButton is null)
        {
            return;
        }

        var comboBoxes = FindDescendants<ComboBox>(form).ToArray();
        var languageCombo = comboBoxes.FirstOrDefault(combo =>
            combo.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "English", StringComparison.Ordinal))
            && combo.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "Русский", StringComparison.Ordinal)));
        if (languageCombo is null)
        {
            return;
        }

        var themeCombo = comboBoxes.FirstOrDefault(combo => !ReferenceEquals(combo, languageCombo));
        if (themeCombo is null)
        {
            return;
        }

        string SelectedTheme()
        {
            var label = themeCombo.SelectedItem?.ToString() ?? string.Empty;
            return label switch
            {
                "Light" or "Светлая" => "light",
                "Gray" or "Серая" => "gray",
                "Dark" or "Тёмная" => "dark",
                _ => "system",
            };
        }

        var managerState = ManagerVersionState.Checking(CurrentDisplayIdentity());
        CancellationTokenSource? checkCancellation = null;

        void RenderManagerState()
        {
            var stateText = managerState.Kind switch
            {
                ManagerVersionStateKind.UpToDate => $"✓ {UiText.T("Up to date", "Актуально")}",
                ManagerVersionStateKind.UpdateAvailable => $"↑ {UiText.T("Update available", "Есть обновление")}",
                ManagerVersionStateKind.NetworkIssue => $"! {UiText.T("Network issue", "Ошибка сети")}",
                ManagerVersionStateKind.Cancelled => $"○ {UiText.T("Check cancelled", "Проверка отменена")}",
                _ => $"↻ {UiText.T("Checking…", "Проверка…")}",
            };
            versionStatus.Text = $"{stateText} [{managerState.Identity}]";
            versionStatus.ForeColor = ManagerStatusColor(managerState.Kind, SelectedTheme());
            if (!versionStatus.Font.Bold)
            {
                versionStatus.Font = new Font(versionStatus.Font, FontStyle.Bold);
            }

            managerPath.Text = DeadlimitPaths.DefaultDeadlimitRoot;
            updateButton.Text = managerState.Kind switch
            {
                ManagerVersionStateKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
                ManagerVersionStateKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
                _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
            };
            updateButton.Enabled = true;
            updateButton.AccessibleDescription = managerState.Kind == ManagerVersionStateKind.Checking
                ? UiText.T("Checking for updates. Click again to cancel the check.", "Идёт проверка обновлений. Нажмите ещё раз, чтобы отменить проверку.")
                : managerState.Detail;
            versionStatus.AccessibleDescription = managerState.Detail;
            openButton.AccessibleDescription = UiText.T("Open the Deadlimit installation folder.", "Открыть папку установки Deadlimit.");
            browseButton.AccessibleDescription = UiText.T("Move Deadlimit to another folder.", "Переместить Deadlimit в другую папку.");
        }

        async Task RefreshManagerStateAsync()
        {
            checkCancellation?.Cancel();
            checkCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            checkCancellation = cancellation;
            managerState = ManagerVersionState.Checking(managerState.Identity);
            RenderManagerState();
            try
            {
                var state = await CheckManagerVersionAsync(cancellation.Token);
                if (ReferenceEquals(checkCancellation, cancellation) && !cancellation.IsCancellationRequested)
                {
                    managerState = state;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (ReferenceEquals(checkCancellation, cancellation))
                {
                    managerState = ManagerVersionState.Cancelled(managerState.Identity);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (ReferenceEquals(checkCancellation, cancellation))
                {
                    managerState = ManagerVersionState.NetworkIssue(managerState.Identity, exception.Message);
                }
            }
            finally
            {
                if (ReferenceEquals(checkCancellation, cancellation))
                {
                    checkCancellation = null;
                    cancellation.Dispose();
                }

                if (!form.IsDisposed)
                {
                    RenderManagerState();
                }
            }
        }

        PreparedForms.Add(form, new object());
        themeCombo.SelectedIndexChanged += (_, _) => RenderManagerState();
        openButton.Click += (_, _) => OpenFolder(form, managerPath.Text);
        browseButton.Click += async (_, _) =>
        {
            var selected = ChooseRelocationFolder(form, managerPath.Text);
            if (selected is null || string.Equals(
                    Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(managerPath.Text).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var answer = MessageBox.Show(
                form,
                UiText.T(
                    $"Move Deadlimit to:\n{selected}\n\nDeadlimit Manager will restart after the move.",
                    $"Переместить Deadlimit в:\n{selected}\n\nПосле перемещения Deadlimit Manager перезапустится."),
                UiText.T("Move Deadlimit", "Перемещение Deadlimit"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                form.UseWaitCursor = true;
                browseButton.Enabled = false;
                await DeadlimitRelocationService.PrepareRelocationAsync(selected);
                Application.Exit();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                MessageBox.Show(
                    form,
                    exception.Message,
                    UiText.T("Could not move Deadlimit", "Не удалось переместить Deadlimit"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (!form.IsDisposed)
                {
                    form.UseWaitCursor = false;
                    browseButton.Enabled = true;
                }
            }
        };
        updateButton.Click += async (_, _) =>
        {
            if (managerState.Kind == ManagerVersionStateKind.Checking)
            {
                checkCancellation?.Cancel();
                managerState = ManagerVersionState.Cancelled(managerState.Identity);
                RenderManagerState();
                return;
            }

            if (managerState.Kind == ManagerVersionStateKind.UpdateAvailable)
            {
                LaunchUpdater(form);
                return;
            }

            await RefreshManagerStateAsync();
        };

        RenderManagerState();
        if (form.Visible)
        {
            _ = RefreshManagerStateAsync();
        }
        else
        {
            form.Shown += async (_, _) => await RefreshManagerStateAsync();
        }
    }

    private static string? ChooseRelocationFolder(IWin32Window owner, string currentRoot)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = UiText.T("Choose the new Deadlimit folder", "Выберите новую папку Deadlimit"),
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.GetParent(currentRoot)?.FullName ?? currentRoot,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private static void OpenFolder(IWin32Window owner, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                exception.Message,
                UiText.T("Could not open Deadlimit folder", "Не удалось открыть папку Deadlimit"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

'''
v = sub_once(v, r"    private static void EnsureManagerStatus\(SettingsForm form\)\n    \{[\s\S]*?\n    \}\n\n(?=    private static void LaunchUpdater)", ensure_manager, "replace manager state handler")

v = replace_once(v, "    private static async Task<ManagerVersionState> CheckManagerVersionAsync()", "    private static async Task<ManagerVersionState> CheckManagerVersionAsync(CancellationToken cancellationToken)", "manager check token signature")
v = replace_once(v, "await GetLatestReleaseVersionAsync().ConfigureAwait(false)", "await GetLatestReleaseVersionAsync(cancellationToken).ConfigureAwait(false)", "manager release token call")
v = replace_once(v, "await ReadGitHeadAsync(repositoryRoot).ConfigureAwait(false)", "await ReadGitHeadAsync(repositoryRoot, cancellationToken).ConfigureAwait(false)", "manager git token call")
v = replace_once(v, "await GetMainCommitShaAsync().ConfigureAwait(false)", "await GetMainCommitShaAsync(cancellationToken).ConfigureAwait(false)", "manager main token call")
v = replace_once(
    v,
    "                ? ManagerVersionState.UpToDate(\n                    display,",
    "                ? ManagerVersionState.UpToDate(\n                    $\"v{current}\",",
    "portable manager up to date identity")
v = replace_once(
    v,
    "                : ManagerVersionState.UpdateAvailable(\n                    display,",
    "                : ManagerVersionState.UpdateAvailable(\n                    $\"v{current}\",",
    "portable manager update identity")
v = replace_once(
    v,
    "            ? ManagerVersionState.UpToDate(\n                displayVersion,",
    "            ? ManagerVersionState.UpToDate(\n                $\"main-{ShortSha(localSha)}\",",
    "dev manager up to date identity")
v = replace_once(
    v,
    "            : ManagerVersionState.UpdateAvailable(\n                displayVersion,",
    "            : ManagerVersionState.UpdateAvailable(\n                $\"main-{ShortSha(localSha)}\",",
    "dev manager update identity")
# Remove no-longer-used display locals.
v = v.replace("        var display = $\"{UiText.T(\"Version\", \"Версия\")} {current}\";\n", "")
v = v.replace("        var displayVersion = $\"main · {ShortSha(localSha)}\";\n", "")

v = replace_once(v, "    private static async Task<string> GetLatestReleaseVersionAsync()", "    private static async Task<string> GetLatestReleaseVersionAsync(CancellationToken cancellationToken)", "latest release token signature")
v = v.replace("VersionHttpClient.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false)", "VersionHttpClient.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false)")
v = v.replace("JsonDocument.ParseAsync(stream).ConfigureAwait(false)", "JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)")
v = v.replace("VersionHttpClient.GetAsync(metadataUri).ConfigureAwait(false)", "VersionHttpClient.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false)")
v = v.replace("JsonDocument.ParseAsync(metadataStream).ConfigureAwait(false)", "JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken).ConfigureAwait(false)")
v = replace_once(v, "    private static async Task<string> GetMainCommitShaAsync()", "    private static async Task<string> GetMainCommitShaAsync(CancellationToken cancellationToken)", "main sha token signature")
v = replace_once(v, "VersionHttpClient.GetAsync(MainCommitApiUrl).ConfigureAwait(false)", "VersionHttpClient.GetAsync(MainCommitApiUrl, cancellationToken).ConfigureAwait(false)", "main sha token request")
v = replace_once(v, "    private static async Task<string> ReadGitHeadAsync(string repositoryRoot)", "    private static async Task<string> ReadGitHeadAsync(string repositoryRoot, CancellationToken cancellationToken)", "git head token signature")
v = replace_once(v, "        await process.WaitForExitAsync().ConfigureAwait(false);", "        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);", "git wait token")

v = sub_once(
    v,
    r"    private static string CurrentVersionIdentity\(\) =>[\s\S]*?\n\n(?=    private static string NormalizeReleaseTag)",
    "    private static string CurrentDisplayIdentity() => ReleaseChannelPolicy.IsPortableRelease\n        ? $\"v{NormalizeReleaseTag(GetDisplayVersion())}\"\n        : \"main\";\n\n",
    "current display identity")

v = replace_once(
    v,
    "            ManagerVersionStateKind.NetworkIssue => dark ? Color.FromArgb(255, 118, 118) : Color.FromArgb(184, 40, 40),\n            _ => dark ? Color.FromArgb(117, 190, 255) : Color.FromArgb(30, 105, 175),",
    "            ManagerVersionStateKind.NetworkIssue => dark ? Color.FromArgb(255, 118, 118) : Color.FromArgb(184, 40, 40),\n            ManagerVersionStateKind.Checking => dark ? Color.FromArgb(117, 190, 255) : Color.FromArgb(30, 105, 175),\n            _ => dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(85, 85, 85),",
    "manager cancelled color")

manager_state_pattern = r"    private enum ManagerVersionStateKind\n    \{[\s\S]*?\n    \}\n\n    private sealed record ManagerVersionState\([\s\S]*?\n    \}\n"
manager_state_replacement = r'''    private enum ManagerVersionStateKind
    {
        Checking,
        Cancelled,
        UpToDate,
        UpdateAvailable,
        NetworkIssue,
    }

    private sealed record ManagerVersionState(
        ManagerVersionStateKind Kind,
        string Identity,
        string Detail)
    {
        public static ManagerVersionState Checking(string identity) =>
            new(ManagerVersionStateKind.Checking, identity, UiText.T("Checking for Deadlimit Manager updates…", "Проверка обновлений Deadlimit Manager…"));

        public static ManagerVersionState Cancelled(string identity) =>
            new(ManagerVersionStateKind.Cancelled, identity, UiText.T("Deadlimit Manager update check cancelled.", "Проверка обновлений Deadlimit Manager отменена."));

        public static ManagerVersionState UpToDate(string identity, string detail) =>
            new(ManagerVersionStateKind.UpToDate, identity, detail);

        public static ManagerVersionState UpdateAvailable(string identity, string detail) =>
            new(ManagerVersionStateKind.UpdateAvailable, identity, detail);

        public static ManagerVersionState NetworkIssue(string identity, string detail) =>
            new(ManagerVersionStateKind.NetworkIssue, identity, detail);
    }
'''
v = sub_once(v, manager_state_pattern, manager_state_replacement, "replace manager state record")
save(version_path, v)

# Add robust relocation worker/service.
relocation = r'''using System.Diagnostics;
using System.Text;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class DeadlimitRelocationService
{
    public static async Task PrepareRelocationAsync(string targetRoot)
    {
        var sourceRoot = NormalizeRoot(DeadlimitPaths.DefaultDeadlimitRoot);
        var destinationRoot = NormalizeRoot(targetRoot);
        ValidateRoots(sourceRoot, destinationRoot);

        var destinationExisted = Directory.Exists(destinationRoot);
        if (destinationExisted && Directory.EnumerateFileSystemEntries(destinationRoot).Any())
        {
            throw new InvalidOperationException(
                UiText.T(
                    "The selected Deadlimit folder must be empty.",
                    "Выбранная папка Deadlimit должна быть пустой."));
        }

        Directory.CreateDirectory(destinationRoot);
        try
        {
            await Task.Run(() => CopyTree(sourceRoot, destinationRoot)).ConfigureAwait(true);
        }
        catch
        {
            if (!destinationExisted)
            {
                TryDeleteDirectory(destinationRoot);
            }
            throw;
        }

        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current Deadlimit Manager executable path is unavailable.");
        var executableRelative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(currentExecutable));
        if (EscapesRoot(executableRelative))
        {
            throw new InvalidOperationException("Deadlimit Manager is not running from the detected Deadlimit root.");
        }

        var relocatedExecutable = Path.Combine(destinationRoot, executableRelative);
        if (!File.Exists(relocatedExecutable))
        {
            throw new FileNotFoundException("The relocated Deadlimit Manager executable was not copied.", relocatedExecutable);
        }

        var helperPath = Path.Combine(
            Path.GetTempPath(),
            $"deadlimit-relocate-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(helperPath, RelocationWorkerScript, new UTF8Encoding(false)).ConfigureAwait(true);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-OldRoot");
        startInfo.ArgumentList.Add(sourceRoot);
        startInfo.ArgumentList.Add("-NewRoot");
        startInfo.ArgumentList.Add(destinationRoot);
        startInfo.ArgumentList.Add("-NewExecutable");
        startInfo.ArgumentList.Add(relocatedExecutable);

        Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Deadlimit relocation worker could not be started.");
    }

    internal static void ValidateRoots(string sourceRoot, string destinationRoot)
    {
        if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(UiText.T("Deadlimit is already in this folder.", "Deadlimit уже находится в этой папке."));
        }

        if (IsWithin(destinationRoot, sourceRoot) || IsWithin(sourceRoot, destinationRoot))
        {
            throw new InvalidOperationException(
                UiText.T(
                    "The new Deadlimit folder cannot be inside the current folder or contain the current folder.",
                    "Новая папка Deadlimit не может находиться внутри текущей папки или содержать текущую папку."));
        }
    }

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(UiText.T("The Deadlimit folder is empty.", "Папка Deadlimit не указана."), nameof(path));
        }

        return Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !EscapesRoot(relative)
            && !string.Equals(relative, ".", StringComparison.Ordinal);
    }

    private static bool EscapesRoot(string relative) =>
        string.Equals(relative, "..", StringComparison.Ordinal)
        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || Path.IsPathRooted(relative);

    private static void CopyTree(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
            try
            {
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
                File.SetAttributes(destinationFile, File.GetAttributes(sourceFile));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private const string RelocationWorkerScript = """
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$OldRoot,
    [Parameter(Mandatory=$true)][string]$NewRoot,
    [Parameter(Mandatory=$true)][string]$NewExecutable
)
$ErrorActionPreference = 'Stop'

function Replace-RootPrefix([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $Value }
    if ($Value.StartsWith($OldRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $NewRoot + $Value.Substring($OldRoot.Length)
    }
    return $Value
}

function Rewrite-Shortcut([object]$Shell, [string]$Path) {
    try {
        $shortcut = $Shell.CreateShortcut($Path)
        $changed = $false
        $target = Replace-RootPrefix $shortcut.TargetPath
        if ($target -ne $shortcut.TargetPath) { $shortcut.TargetPath = $target; $changed = $true }
        $working = Replace-RootPrefix $shortcut.WorkingDirectory
        if ($working -ne $shortcut.WorkingDirectory) { $shortcut.WorkingDirectory = $working; $changed = $true }
        $icon = Replace-RootPrefix $shortcut.IconLocation
        if ($icon -ne $shortcut.IconLocation) { $shortcut.IconLocation = $icon; $changed = $true }
        if ($changed) { $shortcut.Save() }
    } catch {}
}

try { Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Milliseconds 250

$shell = New-Object -ComObject WScript.Shell
$locations = @(
    $NewRoot,
    [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory),
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'Deadlimit')
) | Select-Object -Unique
foreach ($location in $locations) {
    if (-not [string]::IsNullOrWhiteSpace($location) -and (Test-Path -LiteralPath $location)) {
        Get-ChildItem -LiteralPath $location -Filter '*.lnk' -File -ErrorAction SilentlyContinue |
            ForEach-Object { Rewrite-Shortcut $shell $_.FullName }
    }
}

$deleteError = $null
try {
    if (Test-Path -LiteralPath $OldRoot) {
        Remove-Item -LiteralPath $OldRoot -Recurse -Force
    }
} catch {
    $deleteError = $_.Exception.Message
}

Start-Process -FilePath $NewExecutable -WorkingDirectory $NewRoot
if ($deleteError) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "Deadlimit was copied to the new folder and restarted, but the old folder could not be removed:`n$OldRoot`n`n$deleteError",
        'Deadlimit relocation',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
}

try { Remove-Item -LiteralPath $PSCommandPath -Force } catch {}
""";
}
'''
save("internal/src/Deadlimit/App/DeadlimitRelocationService.cs", relocation)

# Start the human-readable version track at the next existing beta build.
proj_path = "internal/src/Deadlimit/Deadlimit.csproj"
p = load(proj_path)
p = replace_once(p, "    <Version>0.1.0-beta.1</Version>", "    <Version>0.1.0-beta.2</Version>", "friendly version bump")
save(proj_path, p)

# Extend the already-running UI smoke instead of adding another workflow.
smoke_path = "internal/tests/ui-localization-smoke.ps1"
u = load(smoke_path)
u = replace_once(
    u,
    "Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'Path.Combine(updateRoot, \"Update Deadlimit.cmd\")'",
    "Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'Path.Combine(updateRoot, \"Update Deadlimit.cmd\")'\nAssert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'DeadlimitManagerPath'\nAssert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'DeadlimitRelocationService.PrepareRelocationAsync'\nAssert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'ManagerVersionStateKind.Cancelled'\nAssert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'ToolchainStatusKind.Cancelled'\nAssert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'v{GetFriendlyVersion()}'\nAssert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Tool status is checked when this window opens.'\nAssert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Состояние инструментов проверяется при открытии окна.'\nAssert-Contains 'internal/src/Deadlimit/App/DeadlimitRelocationService.cs' 'Rewrite-Shortcut'\nAssert-Contains 'internal/src/Deadlimit/Deadlimit.csproj' '<Version>0.1.0-beta.2</Version>'",
    "settings overhaul smoke assertions")
save(smoke_path, u)

print("Settings overhaul patch applied.")
