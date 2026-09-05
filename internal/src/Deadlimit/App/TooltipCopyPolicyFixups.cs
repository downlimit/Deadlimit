namespace Deadlimit.App;

internal static class TooltipCopyPolicyFixups
{
    private const string EnglishStructureSuffix =
        "A structural change was detected. ONLINE PREPARATION is rebuilding the full PREPARE baseline automatically.";
    private const string RussianStructureSuffix =
        "Обнаружено структурное изменение. ОНЛАЙН-ПОДГОТОВКА автоматически перестраивает полную базовую версию PREPARE.";

    internal static string BeforeRewrite(string text)
    {
        // UiText normalizes a few product names before a tooltip reaches RichToolTip.
        // Handle those normalized variants here so the artist-facing copy remains stable.
        if (text.StartsWith("Prepare the selected project's working files for Reduced CSDK", StringComparison.Ordinal))
        {
            return "**PREPARE FOR CSDK** copies the current project files into CSDK and updates the matching textures.\n\nUse it before working with the model or materials in CSDK. A normal run keeps your manual material edits.\n\nHold **SHIFT** to rebuild Deadlimit-created materials from scratch. You can choose whether to make a backup first.";
        }

        if (text.StartsWith("Подготовить рабочие файлы выбранного проекта для Reduced CSDK", StringComparison.Ordinal))
        {
            return "**ПОДГОТОВИТЬ ДЛЯ CSDK** переносит текущие файлы проекта в CSDK и обновляет соответствующие текстуры.\n\nИспользуйте перед работой с моделью или материалами в CSDK. При обычном запуске ваши ручные правки материалов сохраняются.\n\nУдерживайте **SHIFT**, чтобы заново создать материалы, сделанные Deadlimit. Перед этим можно выбрать, делать ли резервную копию.";
        }

        if (text.StartsWith("Launch the configured Reduced CSDK environment.", StringComparison.Ordinal))
        {
            return "**LAUNCH CSDK** opens the configured CSDK.\n\nUse it to work with the project's model and materials.\n\nHold **SHIFT** to prepare the project first and enable **ONLINE PREPARATION**. In this mode, changed model and texture files update in CSDK automatically. Repeat SHIFT-click to turn it off.";
        }

        if (text.StartsWith("Запустить настроенное окружение Reduced CSDK.", StringComparison.Ordinal))
        {
            return "**ЗАПУСК CSDK** открывает настроенный CSDK.\n\nИспользуйте его для работы с моделью и материалами проекта.\n\nУдерживайте **SHIFT**, чтобы сначала подготовить проект и включить **ОНЛАЙН-ПОДГОТОВКУ**. В этом режиме изменённые файлы модели и текстуры автоматически обновляются в CSDK. Повторный SHIFT-клик выключит режим.";
        }

        if (text.StartsWith("Launch Deadlock game client through Steam.", StringComparison.Ordinal))
        {
            return "**LAUNCH GAME** opens Deadlock through Steam.\n\nUse it to test the current mod in the game.\n\nHold **SHIFT** to copy the camera-lock text instead of launching Deadlock.";
        }

        if (text.StartsWith("Запустить Deadlock через Steam.", StringComparison.Ordinal))
        {
            return "**ЗАПУСК ИГРЫ** открывает Deadlock через Steam.\n\nИспользуйте для проверки текущей версии мода в игре.\n\nУдерживайте **SHIFT**, чтобы вместо запуска скопировать текст для блокировки камеры.";
        }

        if (text.StartsWith("Deadlock is running. Click to close the game.", StringComparison.Ordinal))
        {
            return "**CLOSE** shuts down the running Deadlock game.\n\nUse it when you need to rebuild or replace the mod.\n\nHold **SHIFT** to copy the camera-lock text instead.";
        }

        if (text.StartsWith("Deadlock запущен. Нажмите, чтобы закрыть игру.", StringComparison.Ordinal))
        {
            return "**ЗАКРЫТЬ** завершает запущенный Deadlock.\n\nИспользуйте, когда нужно пересобрать или заменить мод.\n\nУдерживайте **SHIFT**, чтобы вместо закрытия скопировать текст для блокировки камеры.";
        }

        if (text.StartsWith("Run the optional CSDK fine-tuning from the current installation guide.", StringComparison.Ordinal))
        {
            return "Prepare the additional Deadlock files that CSDK may need.\n\nUse this if some game files or tools are missing after a normal **Reduced CSDK** installation.\n\nDeadlimit downloads and prepares the required files automatically. Steam may ask you to sign in with a QR code in a separate window. Your installed **Deadlock** files are **never changed**.";
        }

        if (text.StartsWith("Выполнить дополнительную донастройку CSDK по актуальной инструкции.", StringComparison.Ordinal))
        {
            return "Подготовить дополнительные файлы Deadlock, которые могут понадобиться CSDK.\n\nИспользуйте, если после обычной установки **Reduced CSDK** не хватает некоторых файлов игры или инструментов.\n\nDeadlimit автоматически скачает и подготовит нужные файлы. Steam может попросить войти через QR-код в отдельном окне. Файлы установленного **Deadlock** никогда не изменяются.";
        }

        if (text.StartsWith("**INSTALL…** downloads the latest official Windows x64 release from GitHub and creates one DeadlockTools folder", StringComparison.Ordinal))
        {
            return "**INSTALL…** downloads and installs the latest **DeadlockTools** into the selected location.\n\n**UPDATE…** updates DeadlockTools when a newer version is available.\n\n**CHECK** checks whether the selected installation is current. If Deadlimit cannot identify its version, it offers **INSTALL…** instead.";
        }

        if (text.StartsWith("**УСТАНОВИТЬ…** скачивает последний официальный Windows x64 release с GitHub и сам создаёт одну папку DeadlockTools", StringComparison.Ordinal))
        {
            return "**УСТАНОВИТЬ…** скачивает и устанавливает актуальный **DeadlockTools** в выбранное место.\n\n**ОБНОВИТЬ…** обновляет DeadlockTools, если доступна новая версия.\n\n**ПРОВЕРИТЬ** проверяет актуальность выбранной установки. Если Deadlimit не может определить её версию, вместо проверки будет предложена **УСТАНОВИТЬ…**.";
        }

        if (text.StartsWith("Validate and apply the changed folders and interface settings.", StringComparison.Ordinal))
        {
            return "**APPLY** saves the changed folders and interface settings.\n\nUse it when you want to keep the changes made in this window.\n\nTool folders that you do not use can be left empty.";
        }

        if (text.StartsWith("Проверить и применить изменённые папки и настройки интерфейса.", StringComparison.Ordinal))
        {
            return "**ПРИМЕНИТЬ** сохраняет изменённые папки и настройки интерфейса.\n\nИспользуйте, чтобы сохранить изменения, сделанные в этом окне.\n\nПути к инструментам, которыми вы не пользуетесь, можно оставить пустыми.";
        }

        if (text.StartsWith("Discard pending Settings changes and close the window.", StringComparison.Ordinal))
        {
            return "**CANCEL** closes Settings without saving the changes made in this window.\n\nAny temporary theme preview is also undone.";
        }

        if (text.StartsWith("Отменить несохранённые изменения настроек и закрыть окно.", StringComparison.Ordinal))
        {
            return "**ОТМЕНА** закрывает настройки без сохранения изменений, сделанных в этом окне.\n\nВременный предпросмотр темы тоже будет отменён.";
        }

        if (text.StartsWith("Game-client VPK release slot:", StringComparison.Ordinal))
        {
            return "**Release ID** chooses the number Deadlock uses for this project's mod: 01-99.\n\nUse different numbers for projects that should stay installed at the same time.\n\nType the number directly or use the arrows.";
        }

        if (text.StartsWith("Слот VPK игрового клиента Deadlock:", StringComparison.Ordinal))
        {
            return "**Release ID** выбирает номер, который Deadlock использует для мода этого проекта: 01-99.\n\nИспользуйте разные номера для проектов, которые должны оставаться установленными одновременно.\n\nВведите число вручную или используйте стрелки.";
        }

        if (text.StartsWith("Release ID determines the game-client VPK file name.", StringComparison.Ordinal))
        {
            return "**Release ID** chooses the number Deadlock uses for this project's mod.\n\nUse different numbers for projects that should stay installed at the same time.";
        }

        if (text.StartsWith("Release ID определяет имя VPK-файла", StringComparison.Ordinal))
        {
            return "**Release ID** выбирает номер, который Deadlock использует для мода этого проекта.\n\nИспользуйте разные номера для проектов, которые должны оставаться установленными одновременно.";
        }

        if (text.StartsWith("Release ID:", StringComparison.Ordinal))
        {
            var releaseId = FirstLineValue(text);
            var russian = text.Contains("VPK игрового клиента", StringComparison.OrdinalIgnoreCase);
            return russian
                ? $"**Release ID**: {releaseId}\n\nЭтот проект использует номер {releaseId} для своего мода в Deadlock.\n\nВыберите другой ID, если другой проект должен оставаться установленным одновременно."
                : $"**Release ID**: {releaseId}\n\nThis project uses number {releaseId} for its mod in Deadlock.\n\nChoose a different ID if another project should stay installed at the same time.";
        }

        if (text.StartsWith("Copies the repository MaxScript fileIn command.", StringComparison.Ordinal))
        {
            return "Copy the text needed to start the **Vertex Color** helper.\n\nUse it after exporting the model with Wall Worm when you need its vertex colors to appear correctly in CSDK. The helper creates the extra color file automatically next to the exported model.\n\n**Fixed Gamma** slightly adjusts the exported colors. Leave it off unless your workflow specifically requires that correction.";
        }

        if (text.StartsWith("Копирует команду fileIn для MaxScript из репозитория.", StringComparison.Ordinal))
        {
            return "Скопировать текст для запуска помощника **Vertex Color**.\n\nИспользуйте после экспорта модели через Wall Worm, если нужно, чтобы цвета вершин корректно появились в CSDK. Помощник автоматически создаст дополнительный файл цветов рядом с экспортированной моделью.\n\n**Fixed Gamma** немного корректирует экспортируемые цвета. Оставьте эту опцию выключенной, если ваш рабочий процесс специально не требует такой коррекции.";
        }

        if (text.Contains("DeadlockTools.exe was not found", StringComparison.OrdinalIgnoreCase))
        {
            return "This folder does not look like a **DeadlockTools** installation.\n\nChoose the main DeadlockTools folder.";
        }

        if (text.Contains("не найден DeadlockTools.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Эта папка не похожа на установленный **DeadlockTools**.\n\nВыберите основную папку DeadlockTools.";
        }

        if (text.Contains("game\\citadel", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("not a valid Deadlock client", StringComparison.OrdinalIgnoreCase))
            {
                return "This folder does not look like the installed **Deadlock client**.\n\nChoose the main Project8Staging folder.";
            }

            if (text.Contains("не является валидной установкой Deadlock", StringComparison.OrdinalIgnoreCase))
            {
                return "Эта папка не похожа на установленный **Deadlock клиент**.\n\nВыберите основную папку Project8Staging.";
            }
        }

        if (text.Contains(EnglishStructureSuffix, StringComparison.Ordinal))
        {
            return "The project structure changed.\n\nDeadlimit is preparing the project again automatically before continuing CSDK updates.";
        }

        if (text.Contains(RussianStructureSuffix, StringComparison.Ordinal))
        {
            return "Структура проекта изменилась.\n\nDeadlimit автоматически готовит проект заново перед продолжением обновлений в CSDK.";
        }

        return text;
    }

    internal static string AfterRewrite(string text)
    {
        if (text.StartsWith("**Release ID** chooses which numbered mod file", StringComparison.Ordinal))
        {
            return "**Release ID** chooses the number Deadlock uses for this project's mod: 01-99.\n\nUse different numbers for projects that should stay installed at the same time.\n\nType the number directly or use the arrows.";
        }

        if (text.StartsWith("**Release ID** выбирает номер файла мода", StringComparison.Ordinal))
        {
            return "**Release ID** выбирает номер, который Deadlock использует для мода этого проекта: 01-99.\n\nИспользуйте разные номера для проектов, которые должны оставаться установленными одновременно.\n\nВведите число вручную или используйте стрелки.";
        }

        if (text.StartsWith("Copy the command for the **Vertex Color** helper script.", StringComparison.Ordinal))
        {
            return "Copy the text needed to start the **Vertex Color** helper.\n\nUse it after exporting the model with Wall Worm when you need its vertex colors to appear correctly in CSDK. The helper creates the extra color file automatically next to the exported model.\n\n**Fixed Gamma** slightly adjusts the exported colors. Leave it off unless your workflow specifically requires that correction.";
        }

        if (text.StartsWith("Скопировать команду для вспомогательного скрипта **Vertex Color**.", StringComparison.Ordinal))
        {
            return "Скопировать текст для запуска помощника **Vertex Color**.\n\nИспользуйте после экспорта модели через Wall Worm, если нужно, чтобы цвета вершин корректно появились в CSDK. Помощник автоматически создаст дополнительный файл цветов рядом с экспортированной моделью.\n\n**Fixed Gamma** немного корректирует экспортируемые цвета. Оставьте эту опцию выключенной, если ваш рабочий процесс специально не требует такой коррекции.";
        }

        return text
            .Replace("Hold **SHIFT** to copy the camera-lock command instead of launching Deadlock: cl_lock_camera true.",
                "Hold **SHIFT** to copy the camera-lock text instead of launching Deadlock.",
                StringComparison.Ordinal)
            .Replace("Удерживайте **SHIFT**, чтобы вместо запуска скопировать команду блокировки камеры: cl_lock_camera true.",
                "Удерживайте **SHIFT**, чтобы вместо запуска скопировать текст для блокировки камеры.",
                StringComparison.Ordinal)
            .Replace("camera-lock command", "camera-lock text", StringComparison.Ordinal)
            .Replace("команду блокировки камеры", "текст для блокировки камеры", StringComparison.Ordinal)
            .Replace("Installed DeadlockTools release:", "**DeadlockTools** version:", StringComparison.Ordinal)
            .Replace("Установленный релиз DeadlockTools:", "Версия **DeadlockTools**:", StringComparison.Ordinal)
            .Replace("The tool is installed, but Deadlimit could not check for updates because the network source is unavailable.",
                "The tool is installed, but Deadlimit could not check for updates because of a network problem.",
                StringComparison.Ordinal)
            .Replace("Файлы установленного **Deadlock никогда не изменяются**.",
                "Файлы установленного **Deadlock** никогда не изменяются.",
                StringComparison.Ordinal);
    }

    private static string FirstLineValue(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var separator = firstLine.IndexOf(':');
        return separator >= 0
            ? firstLine[(separator + 1)..].Trim()
            : "—";
    }
}
