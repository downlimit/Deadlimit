namespace Deadlimit.App;

internal static class TooltipCopyPolicy
{
    private static readonly IReadOnlyDictionary<string, string> ExactRewrites =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Prepare the selected project's working files for Reduced CSDK12 / ModelDoc / Material Editor.\n\nA normal click preserves manual VMAT tuning while synchronizing matching project textures. Hold SHIFT to regenerate Deadlimit Manager custom materials; the confirmation dialog lets you choose whether to create a backup first."] =
                "**PREPARE FOR CSDK** copies the current project files into CSDK and updates the matching textures.\n\nUse it before working with the model or materials in CSDK. A normal run keeps your manual material edits.\n\nHold **SHIFT** to rebuild Deadlimit-created materials from scratch. You can choose whether to make a backup first.",
            ["Подготовить рабочие файлы выбранного проекта для Reduced CSDK12 / ModelDoc / Material Editor.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует совпавшие текстуры проекта. Удерживайте SHIFT, чтобы пересоздать custom-материалы Deadlimit Manager; в окне подтверждения можно выбрать, создавать ли резервную копию."] =
                "**ПОДГОТОВИТЬ ДЛЯ CSDK** переносит текущие файлы проекта в CSDK и обновляет соответствующие текстуры.\n\nИспользуйте перед работой с моделью или материалами в CSDK. При обычном запуске ваши ручные правки материалов сохраняются.\n\nУдерживайте **SHIFT**, чтобы заново создать материалы, сделанные Deadlimit. Перед этим можно выбрать, делать ли резервную копию.",
            ["Prepare the selected project's working files for Reduced CSDK12.\n\nA normal click preserves manual VMAT tuning and synchronizes matching project textures. Hold SHIFT to regenerate custom materials; the confirmation dialog lets you choose whether to create a backup first."] =
                "**PREPARE FOR CSDK** copies the current project files into CSDK and updates the matching textures.\n\nUse it before working with the model or materials in CSDK. A normal run keeps your manual material edits.\n\nHold **SHIFT** to rebuild Deadlimit-created materials from scratch. You can choose whether to make a backup first.",
            ["Подготовить рабочие файлы выбранного проекта для Reduced CSDK12.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует совпавшие текстуры проекта. Удерживайте SHIFT, чтобы пересоздать custom-материалы; в окне подтверждения можно выбрать, создавать ли резервную копию."] =
                "**ПОДГОТОВИТЬ ДЛЯ CSDK** переносит текущие файлы проекта в CSDK и обновляет соответствующие текстуры.\n\nИспользуйте перед работой с моделью или материалами в CSDK. При обычном запуске ваши ручные правки материалов сохраняются.\n\nУдерживайте **SHIFT**, чтобы заново создать материалы, сделанные Deadlimit. Перед этим можно выбрать, делать ли резервную копию.",
            ["Compile the current project and deploy its VPK into Deadlock game client so it is ready for testing.\n\nThis action does not launch the game. If Deadlock is already running, it must be closed because the loaded VPK is locked. Hold SHIFT while clicking to force a full clean rebuild."] =
                "**BUILD FOR TEST** prepares the project, builds the mod and puts it into Deadlock so it is ready for testing.\n\nUse it when you want to check the current version in the game. The game itself is not launched.\n\nIf Deadlock is open, it must be closed first. Hold **SHIFT** for a full rebuild.",
            ["Скомпилировать текущий проект и установить его VPK в игровой клиент Deadlock, чтобы мод был готов к тесту.\n\nЭта кнопка не запускает игру. Если Deadlock уже запущен, его нужно закрыть: загруженный VPK заблокирован игрой. Удерживайте SHIFT при клике для полной чистой пересборки."] =
                "**СОБРАТЬ ДЛЯ ТЕСТА** подготовит проект, соберёт мод и установит его в Deadlock для проверки.\n\nИспользуйте, когда хотите проверить текущую версию мода в игре. Сама игра не запускается.\n\nЕсли Deadlock открыт, его нужно сначала закрыть. Удерживайте **SHIFT** для полной пересборки.",
            ["Compile the current project and deploy its VPK into Deadlock game client.\n\nThis action does not launch the game. Hold SHIFT while clicking to force a full clean rebuild."] =
                "**BUILD FOR TEST** prepares the project, builds the mod and puts it into Deadlock so it is ready for testing.\n\nUse it when you want to check the current version in the game. The game itself is not launched.\n\nHold **SHIFT** for a full rebuild.",
            ["Скомпилировать текущий проект и установить его VPK в игровой клиент Deadlock.\n\nЭта кнопка не запускает игру. Удерживайте SHIFT при клике для полной чистой пересборки."] =
                "**СОБРАТЬ ДЛЯ ТЕСТА** подготовит проект, соберёт мод и установит его в Deadlock для проверки.\n\nИспользуйте, когда хотите проверить текущую версию мода в игре. Сама игра не запускается.\n\nУдерживайте **SHIFT** для полной пересборки.",
            ["Launch the configured Reduced CSDK12 environment.\n\nHold SHIFT while clicking to prepare once, enable ONLINE PREPARATION and launch CSDK. Repeat SHIFT+click to stop online synchronization without launching another CSDK instance."] =
                "**LAUNCH CSDK** opens the configured CSDK.\n\nUse it to work with the project's model and materials.\n\nHold **SHIFT** to prepare the project first and enable **ONLINE PREPARATION**. In this mode, changed model and texture files update in CSDK automatically. Repeat SHIFT-click to turn it off.",
            ["Запустить настроенное окружение Reduced CSDK12.\n\nУдерживайте SHIFT при клике, чтобы выполнить подготовку, включить ОНЛАЙН-ПОДГОТОВКУ и запустить CSDK. Повторный SHIFT+клик остановит онлайн-синхронизацию без запуска ещё одного CSDK."] =
                "**ЗАПУСК CSDK** открывает настроенный CSDK.\n\nИспользуйте его для работы с моделью и материалами проекта.\n\nУдерживайте **SHIFT**, чтобы сначала подготовить проект и включить **ОНЛАЙН-ПОДГОТОВКУ**. В этом режиме изменённые файлы модели и текстуры автоматически обновляются в CSDK. Повторный SHIFT-клик выключит режим.",
            ["Open Deadlimit Manager settings.\n\nConfigure the projects folder, tool locations, interface language and theme."] =
                "Open **Deadlimit Manager Settings**.\n\nUse them to choose the projects folder, tool locations, interface language and theme.",
            ["Открыть настройки Deadlimit Manager.\n\nЗдесь задаются папка проектов, пути к инструментам, язык и тема интерфейса."] =
                "Открыть **настройки Deadlimit Manager**.\n\nЗдесь можно выбрать папку проектов, расположение инструментов, язык и тему интерфейса.",
            ["Launch Deadlock game client through Steam.\n\nHold SHIFT while clicking to copy 'cl_lock_camera true' to the clipboard without launching the game."] =
                "**LAUNCH GAME** opens Deadlock through Steam.\n\nUse it to test the current mod in the game.\n\nHold **SHIFT** to copy the camera-lock command instead of launching Deadlock: cl_lock_camera true.",
            ["Запустить Deadlock через Steam.\n\nУдерживайте SHIFT при клике, чтобы скопировать 'cl_lock_camera true' в буфер обмена без запуска игры."] =
                "**ЗАПУСК ИГРЫ** открывает Deadlock через Steam.\n\nИспользуйте для проверки текущей версии мода в игре.\n\nУдерживайте **SHIFT**, чтобы вместо запуска скопировать команду блокировки камеры: cl_lock_camera true.",
            ["Deadlock is running. Click to close the game.\n\nHold SHIFT while clicking to copy the camera-lock command instead."] =
                "**CLOSE** shuts down the running Deadlock game.\n\nUse it when you need to rebuild or replace the mod.\n\nHold **SHIFT** to copy the camera-lock command instead.",
            ["Deadlock запущен. Нажмите, чтобы закрыть игру.\n\nУдерживайте SHIFT при клике, чтобы вместо этого скопировать команду блокировки камеры."] =
                "**ЗАКРЫТЬ** завершает запущенный Deadlock.\n\nИспользуйте, когда нужно пересобрать или заменить мод.\n\nУдерживайте **SHIFT**, чтобы вместо закрытия скопировать команду блокировки камеры.",
            ["The launch request was sent to Steam. Deadlimit is waiting for the Deadlock process to appear."] =
                "Steam is starting **Deadlock**.\n\nDeadlimit is waiting for the game to open.",
            ["Запрос на запуск отправлен Steam. Deadlimit ждёт появления процесса Deadlock."] =
                "Steam запускает **Deadlock**.\n\nDeadlimit ждёт, пока игра откроется.",
            ["Re-read the available hero names from the currently configured Deadlock game client installation.\n\nThe refreshed catalog is cached by Deadlimit Manager; it does not change the hero assigned to the current project."] =
                "**REFRESH LIST** updates the available heroes from the installed Deadlock.\n\nUse it if the game added a new hero or the list or icons are missing.\n\nThe hero selected for the current project will not change.",
            ["Перечитать доступные имена героев из установленного через Steam Deadlock, указанного в настройках.\n\nОбновлённый каталог сохраняется в кэше Deadlimit Manager и сам по себе не меняет героя текущего проекта."] =
                "**ОБНОВИТЬ СПИСОК** обновит список доступных героев из установленного Deadlock.\n\nИспользуйте, если в игре появился новый герой или список либо иконки не отображаются.\n\nГерой текущего проекта не изменится.",
            ["Hero selection is unlocked, so the project's hero can be changed.\n\nClick the open lock to lock the selection again and protect it from accidental changes."] =
                "**Lock hero selection** to protect this project from accidental changes.\n\nUse it after choosing the correct hero. Click the open lock to lock the selection.",
            ["Выбор героя разблокирован, поэтому героя проекта можно изменить.\n\nНажмите на открытый замок, чтобы снова заблокировать выбор и защитить проект от случайной смены."] =
                "**Заблокировать выбор героя**, чтобы защитить проект от случайной смены.\n\nИспользуйте после выбора нужного героя. Нажмите на открытый замок, чтобы заблокировать выбор.",
            ["Hero selection is locked to protect this saved project from accidental changes.\n\nClick the closed lock once if you intentionally need to choose a different hero."] =
                "**Unlock hero selection** so you can choose a different hero for this project.\n\nUse it only when you intentionally want to change the hero.",
            ["Выбор героя заблокирован, чтобы защитить сохранённый проект от случайной смены.\n\nНажмите на закрытый замок один раз, если вы намеренно хотите выбрать другого героя."] =
                "**Разблокировать выбор героя**, чтобы выбрать для проекта другого героя.\n\nИспользуйте только если вы намеренно хотите изменить героя.",
            ["Open the selected project's root folder in Explorer.\n\nDouble-clicking the project in the Library opens the same folder."] =
                "Open the selected **project folder** in Explorer.\n\nDouble-clicking the project in the Library opens the same folder.",
            ["Открыть корневую папку выбранного проекта в Проводнике.\n\nДвойной клик по проекту в Библиотеке открывает ту же папку."] =
                "Открыть **папку выбранного проекта** в Проводнике.\n\nДвойной клик по проекту в Библиотеке открывает ту же папку.",
            ["Save the project and extract the selected hero's current source resources from Deadlock game client into 0source.\n\nIf 0source already contains files, Deadlimit Manager asks whether to refresh it while keeping the previous copy as a hidden backup or to refresh without retaining that backup."] =
                "**EXTRACT SOURCE** saves the project and copies the selected hero's current source files from Deadlock into **0source**.\n\nUse it when starting work on a hero or when you need fresh game files.\n\nIf 0source already has files, Deadlimit asks whether to keep the previous copy as a backup.",
            ["Сохранить проект и извлечь актуальные исходные ресурсы выбранного героя из игрового клиента Deadlock в 0source.\n\nЕсли в 0source уже есть файлы, Deadlimit Manager предложит обновить их с сохранением предыдущей копии в скрытый backup или обновить без сохранения backup."] =
                "**ИЗВЛЕЧЬ ИСХОДНИКИ** сохранит проект и скопирует актуальные исходные файлы выбранного героя из Deadlock в **0source**.\n\nИспользуйте в начале работы над героем или когда нужны свежие файлы из игры.\n\nЕсли в 0source уже есть файлы, Deadlimit предложит сохранить предыдущую копию как резервную.",
            ["Save this project's metadata, hero, Release ID and current DMX/PNG file list.\n\nAfter a successful save, hero selection is locked again to protect the project from accidental changes."] =
                "**SAVE PROJECT** saves the selected hero, **Release ID** and current project files.\n\nUse it to keep the current project settings for the next time you open it.\n\nAfter saving, hero selection is locked again to prevent accidental changes.",
            ["Сохранить метаданные проекта, героя, Release ID и текущий список DMX/PNG-файлов.\n\nПосле успешного сохранения выбор героя снова блокируется, чтобы защитить проект от случайной смены."] =
                "**СОХРАНИТЬ ПРОЕКТ** сохранит выбранного героя, **Release ID** и текущие файлы проекта.\n\nИспользуйте, чтобы сохранить текущие настройки проекта для следующего открытия.\n\nПосле сохранения выбор героя снова блокируется от случайной смены.",
            ["Game-client VPK release slot: 01-99. Type the number directly or change it with the arrows by ±1.\n\nThe slot becomes part of the deployed VPK filename, for example Release ID 07 → pak07_dir.vpk."] =
                "**Release ID** chooses which numbered mod file this project uses in Deadlock: 01-99.\n\nUse a different number for projects that should not replace each other.\n\nType the number or use the arrows. Example: Release ID 07 → pak07_dir.vpk.",
            ["Слот VPK игрового клиента Deadlock: 01-99. Число можно ввести вручную или менять стрелками на ±1.\n\nСлот входит в имя установленного VPK-файла, например Release ID 07 → pak07_dir.vpk."] =
                "**Release ID** выбирает номер файла мода, который этот проект использует в Deadlock: 01-99.\n\nИспользуйте разные номера для проектов, которые не должны заменять друг друга.\n\nВведите число вручную или используйте стрелки. Пример: Release ID 07 → pak07_dir.vpk.",
            ["Create a new empty project folder inside the configured Projects folder.\n\nThe folder name becomes the project name. Choose a hero and Release ID, then save the project metadata."] =
                "**Create a new project** in the configured Projects folder.\n\nUse it to start a separate Deadlimit project.\n\nThe folder name becomes the project name. Then choose a hero and Release ID and save the project.",
            ["Создать новую пустую папку проекта внутри настроенной Папки проектов.\n\nИмя папки станет именем проекта. После создания выберите героя и Release ID, затем сохраните метаданные проекта."] =
                "**Создать новый проект** в указанной папке проектов.\n\nИспользуйте, чтобы начать отдельный проект Deadlimit.\n\nИмя папки станет именем проекта. Затем выберите героя и Release ID и сохраните проект.",
            ["Create the folder and add it to the Library.\n\nThe project is not initialized until you choose a hero and save it."] =
                "**CREATE** creates the project folder and adds it to the Library.\n\nThen choose a hero and save the project to make it ready for work.",
            ["Создать папку и добавить её в Библиотеку.\n\nПроект будет инициализирован только после выбора героя и сохранения."] =
                "**СОЗДАТЬ** создаст папку проекта и добавит её в Библиотеку.\n\nЗатем выберите героя и сохраните проект, чтобы он был готов к работе.",
            ["Open this project's Deadlimit Manager logs folder in Explorer.\n\nPREPARE FOR CSDK and BUILD FOR TEST write their diagnostic .log files here."] =
                "Open this project's **Logs** folder in Explorer.\n\nUse it when you need to see what happened during **PREPARE FOR CSDK** or **BUILD FOR TEST**.\n\nDeadlimit stores a text record of those actions here.",
            ["Открыть папку логов этого проекта Deadlimit Manager в Проводнике.\n\nПОДГОТОВИТЬ ДЛЯ CSDK и СОБРАТЬ ДЛЯ ТЕСТА сохраняют сюда диагностические .log-файлы."] =
                "Открыть папку **логов** этого проекта в Проводнике.\n\nИспользуйте, чтобы посмотреть, что происходило во время **ПОДГОТОВИТЬ ДЛЯ CSDK** или **СОБРАТЬ ДЛЯ ТЕСТА**.\n\nDeadlimit хранит здесь текстовую запись этих действий.",
            ["**INSTALL…** selects an empty folder and downloads the current Reduced CSDK.\n\n**UPDATE…** overlays the current distribution onto the configured CSDK folder.\n\n**CHECK** validates the installation and checks the latest published CSDK generation."] =
                "**INSTALL…** downloads and installs the current **Reduced CSDK** into an empty folder.\n\n**UPDATE…** updates the selected Reduced CSDK.\n\n**CHECK** checks that the selected installation works and whether an update is available.",
            ["**УСТАНОВИТЬ…** выбирает пустую папку и скачивает актуальный Reduced CSDK.\n\n**ОБНОВИТЬ…** накладывает актуальный дистрибутив поверх настроенной папки CSDK.\n\n**ПРОВЕРИТЬ** валидирует установку и проверяет последнее опубликованное поколение CSDK."] =
                "**УСТАНОВИТЬ…** скачивает и устанавливает актуальный **Reduced CSDK** в пустую папку.\n\n**ОБНОВИТЬ…** обновляет выбранный Reduced CSDK.\n\n**ПРОВЕРИТЬ** проверяет, работает ли выбранная установка и доступно ли обновление.",
            ["Run the optional full CSDK setup from the current installation guide.\n\nDeadlimit downloads the required Deadlock depots, extracts the downloaded VPK as-is, removes the temporary pak01 VPK set, then re-applies Reduced CSDK.\n\nDepotDownloader may open a console for Steam QR authentication.\n\nThe configured Deadlock client folder is only validated and is **never modified**."] =
                "Set up the extra Deadlock files that CSDK needs for a complete working environment.\n\nUse this if the normal Reduced CSDK installation is not enough for your work.\n\nDeadlimit downloads and prepares the required files automatically. Steam may ask you to sign in with a QR code in a separate window. Your installed **Deadlock** game files are **never changed**.",
            ["Выполнить дополнительную полную настройку CSDK по актуальной инструкции.\n\nDeadlimit скачивает нужные депо Deadlock, извлекает скачанный VPK без декомпиляции, удаляет временный набор pak01 VPK и повторно накладывает Reduced CSDK.\n\nDepotDownloader может открыть консоль для Steam-авторизации по QR.\n\nПапка Deadlock клиента только проверяется и **никогда не изменяется**."] =
                "Подготовить дополнительные файлы Deadlock, которые нужны CSDK для полноценной работы.\n\nИспользуйте, если обычной установки Reduced CSDK недостаточно для вашей работы.\n\nDeadlimit автоматически скачает и подготовит нужные файлы. Steam может попросить войти через QR-код в отдельном окне. Файлы установленного **Deadlock никогда не изменяются**.",
            ["Select a Reduced CSDK installation that already exists on this PC.\n\nDeadlimit validates it and immediately checks freshness."] =
                "Select an existing **Reduced CSDK** folder on this PC.\n\nUse it if Reduced CSDK was installed manually.\n\nDeadlimit checks that the folder works and whether an update is available.",
            ["Выбрать уже существующую на этом ПК установку Reduced CSDK.\n\nDeadlimit проверит её структуру и сразу запустит проверку актуальности."] =
                "Выбрать уже установленный **Reduced CSDK** на этом компьютере.\n\nИспользуйте, если Reduced CSDK был установлен вручную.\n\nDeadlimit проверит, подходит ли папка и доступно ли обновление.",
            ["**INSTALL…** downloads the latest official Windows x64 release from GitHub into an empty folder.\n\n**UPDATE…** updates a Deadlimit-managed release installation. Git checkouts are updated through Git and rebuilt.\n\n**CHECK** compares a managed install or Git checkout with the current upstream state.\n\nIf the version of a manually copied build cannot be identified, **INSTALL…** remains available instead of offering a meaningless CHECK."] =
                "**INSTALL…** downloads and installs the latest **DeadlockTools** into an empty folder.\n\n**UPDATE…** updates the selected DeadlockTools when a newer version is available.\n\n**CHECK** checks whether the selected installation is current. If Deadlimit cannot identify its version, it offers **INSTALL…** instead.",
            ["**УСТАНОВИТЬ…** скачивает последний официальный Windows x64 release с GitHub в пустую папку.\n\n**ОБНОВИТЬ…** обновляет установку release, которой управляет Deadlimit. Git checkout обновляется через Git и пересобирается.\n\n**ПРОВЕРИТЬ** сравнивает управляемую установку или Git checkout с текущим upstream.\n\nЕсли версию вручную скопированной сборки определить нельзя, остаётся доступна кнопка **УСТАНОВИТЬ…**, а не бесполезная проверка."] =
                "**УСТАНОВИТЬ…** скачивает и устанавливает актуальный **DeadlockTools** в пустую папку.\n\n**ОБНОВИТЬ…** обновляет выбранный DeadlockTools, если доступна новая версия.\n\n**ПРОВЕРИТЬ** проверяет актуальность выбранной установки. Если Deadlimit не может определить её версию, вместо проверки будет предложена **УСТАНОВИТЬ…**.",
            ["Select an existing DeadlockTools folder.\n\nDeadlimit can fully track installations it installed itself and Git checkouts. A manually copied build may show **Version unknown**."] =
                "Select an existing **DeadlockTools** folder.\n\nUse it if DeadlockTools was installed manually.\n\nDeadlimit checks whether it can use the installation and identify its version. If not, it may show **Version unknown**.",
            ["Выбрать существующую папку DeadlockTools.\n\nDeadlimit полностью отслеживает установки, которые установил сам, и Git checkout. Вручную скопированная сборка может показывать **Версия неизвестна**."] =
                "Выбрать уже установленный **DeadlockTools**.\n\nИспользуйте, если DeadlockTools был установлен вручную.\n\nDeadlimit проверит, подходит ли установка и можно ли определить её версию. Если нет, может отображаться **Версия неизвестна**.",
            ["Validate the installed **Deadlock client** used by Steam.\n\nDeadlimit checks that the selected Project8Staging folder contains the expected game files.\n\nDeadlimit does not install or update the Steam game from Settings."] =
                "Check that the selected folder is the installed **Deadlock client** used by Steam.\n\nUse it if Deadlimit cannot find the game or reports an invalid path.\n\nThis check only reads the folder; it does not install or update the game.",
            ["Проверить установленный **Deadlock клиент**, который запускается через Steam.\n\nDeadlimit проверит, что выбранная папка Project8Staging содержит ожидаемые игровые файлы.\n\nDeadlimit не устанавливает и не обновляет игру через Steam из окна настроек."] =
                "Проверить, что выбрана папка установленного **Deadlock клиента** из Steam.\n\nИспользуйте, если Deadlimit не находит игру или сообщает о неверном пути.\n\nПроверка только читает папку и не устанавливает и не обновляет игру.",
            ["Select the folder where the **Deadlock client** is installed.\n\nFor a standard Steam installation this folder is named Project8Staging."] =
                "Select the folder where the **Deadlock client** is installed.\n\nUse it if Deadlimit did not find the game automatically.\n\nFor a standard Steam installation the folder is named Project8Staging.",
            ["Выбрать папку, в которой установлен **Deadlock клиент**.\n\nВ стандартной установке Steam эта папка называется Project8Staging."] =
                "Выбрать папку, в которой установлен **Deadlock клиент**.\n\nИспользуйте, если Deadlimit не нашёл игру автоматически.\n\nПри стандартной установке Steam папка называется Project8Staging.",
            ["Try to find the installed **Deadlock client** automatically.\n\nDeadlimit checks Steam library folders first, then common Steam locations on local drives. If Deadlock is found, its folder is filled in automatically. Nothing is modified."] =
                "Find the installed **Deadlock client** automatically.\n\nUse this instead of choosing the folder manually.\n\nDeadlimit searches your Steam libraries and common Steam folders. If found, the path is filled in automatically. No files are changed.",
            ["Попытаться автоматически найти установленный **Deadlock клиент**.\n\nDeadlimit сначала проверит библиотеки Steam, затем типичные папки Steam на локальных дисках. Если Deadlock найден, путь подставится автоматически. Никакие файлы не изменяются."] =
                "Автоматически найти установленный **Deadlock клиент**.\n\nИспользуйте вместо ручного выбора папки.\n\nDeadlimit проверит библиотеки Steam и обычные папки Steam. Если игра найдена, путь подставится автоматически. Файлы не изменяются.",
            ["Select the root folder used to store Deadlimit projects.\n\nThis is a workspace folder, so it has no install or update lifecycle."] =
                "Choose the folder where **Deadlimit projects** are stored.\n\nNew projects will be created inside this folder.",
            ["Выбрать корневую папку для проектов Deadlimit.\n\nЭто рабочая папка, поэтому у неё нет установки или обновления."] =
                "Выбрать папку, где хранятся **проекты Deadlimit**.\n\nНовые проекты будут создаваться внутри этой папки.",
            ["Open the bundled Deadlimit Scripts section in File Explorer.\n\nIt contains DeadlimitPipelineScripts.ms and its README."] =
                "Open the **Deadlimit Scripts** section in Explorer.\n\nUse this section for helper scripts that connect creative tools with Deadlimit.\n\nThe folder also contains instructions for the included scripts.",
            ["Открыть раздел Deadlimit Scripts в Проводнике.\n\nВ нём находятся DeadlimitPipelineScripts.ms и README."] =
                "Открыть раздел **Deadlimit Scripts** в Проводнике.\n\nЗдесь находятся вспомогательные скрипты для работы внешних творческих инструментов с Deadlimit.\n\nВ папке также есть инструкции к включённым скриптам.",
            ["If CSDK takes several minutes to start, run this optimization. Afterward, CSDK should start in seconds instead of minutes.\n\nAlso use it after a clean Reduced CSDK installation/update or whenever startup becomes slow again."] =
                "Optimize **CSDK startup** so it is ready for fast launching.\n\nUse it if CSDK takes several minutes to open; after optimization it should start in seconds.\n\nRun it again after a clean Reduced CSDK install or update, or if CSDK becomes slow again.",
            ["Если запуск CSDK занимает несколько минут, проведите эту оптимизацию. После неё CSDK должен запускаться за секунды вместо минут.\n\nТакже используйте её после чистой установки/обновления Reduced CSDK или если запуск снова стал долгим."] =
                "Оптимизировать **запуск CSDK**, чтобы подготовить его к быстрому старту.\n\nИспользуйте, если CSDK открывается несколько минут; после оптимизации он должен запускаться за секунды.\n\nПовторите после чистой установки или обновления Reduced CSDK либо если запуск снова стал долгим.",
            ["Portable releases require an existing Reduced CSDK/DeadlockTools installation selected with BROWSE. Automatic install, update, and full CSDK setup stay disabled until upstream archives have release-pinned trusted checksums."] =
                "This portable Deadlimit build can use an existing **Reduced CSDK** or **DeadlockTools** installation selected with **BROWSE…**.\n\nAutomatic install and update are unavailable in this build.",
            ["Portable-релиз требует существующую установку Reduced CSDK/DeadlockTools, выбранную через ОБЗОР. Автоустановка, обновление и полная настройка CSDK отключены, пока для upstream-архивов нет привязанных к релизу доверенных SHA-256."] =
                "Эта portable-версия Deadlimit может использовать уже установленный **Reduced CSDK** или **DeadlockTools**, выбранный через **ОБЗОР…**.\n\nАвтоматическая установка и обновление в этой версии недоступны.",
            ["Copies the repository MaxScript fileIn command. The helper exports selected geometry and renderable Shape/Spline objects to a **Vertex Color FBX** beside the latest Wall Worm DMX.\n\nOptional **Fixed Gamma** writes RGB^(1/2.2) for Source 2; leave it off for unchanged/Marmoset export.\n\n**PREPARE FOR CSDK** matches multi-color meshes by UV or polygon positions and keeps a rejected sidecar for retry."] =
                "Copy the command for the **Vertex Color** helper script.\n\nUse it after exporting the same model to DMX with Wall Worm when you need vertex colors to reach CSDK correctly. The helper creates a matching **Vertex Color FBX** next to the DMX.\n\n**Fixed Gamma** is optional; leave it off unless your working process specifically needs Source 2 gamma correction.",
            ["Копирует команду fileIn для MaxScript из репозитория. Скрипт экспортирует выделенную геометрию и renderable Shape/Spline в **Vertex Color FBX** рядом с последним DMX Wall Worm.\n\nОпциональный **Fixed Gamma** записывает RGB^(1/2.2) для Source 2; для обычного экспорта и Marmoset оставьте его выключенным.\n\n**ПОДГОТОВИТЬ ДЛЯ CSDK** сопоставляет многоцветные меши по UV или позициям полигонов и сохраняет отклонённый sidecar для повтора."] =
                "Скопировать команду для вспомогательного скрипта **Vertex Color**.\n\nИспользуйте после экспорта той же модели в DMX через Wall Worm, если нужно корректно перенести цвета вершин в CSDK. Скрипт создаст рядом с DMX соответствующий **Vertex Color FBX**.\n\n**Fixed Gamma** — дополнительная опция; оставьте её выключенной, если ваш рабочий процесс отдельно не требует гамма-коррекции Source 2.",
        };

    internal static string Rewrite(string text)
    {
        if (ExactRewrites.TryGetValue(text, out var exact))
        {
            return exact;
        }

        return RewriteDynamic(text);
    }

    private static string RewriteDynamic(string text)
    {
        if (text.StartsWith("ONLINE PREPARATION is off.", StringComparison.Ordinal))
        {
            return "**ONLINE PREPARATION** is off.\n\nHold **SHIFT** and click **LAUNCH CSDK** to prepare the project, open CSDK and keep changed model and texture files updating automatically.\n\nUse the same SHIFT-click again to turn it off.";
        }
        if (text.StartsWith("ОНЛАЙН-ПОДГОТОВКА выключена.", StringComparison.Ordinal))
        {
            return "**ОНЛАЙН-ПОДГОТОВКА** выключена.\n\nУдерживайте **SHIFT** и нажмите **ЗАПУСК CSDK**, чтобы подготовить проект, открыть CSDK и автоматически обновлять изменённые файлы модели и текстуры.\n\nПовторите тот же SHIFT-клик, чтобы выключить режим.";
        }
        if (text.StartsWith("ONLINE PREPARATION is active.", StringComparison.Ordinal))
        {
            var alreadyOpen = text.Contains("existing CSDK", StringComparison.OrdinalIgnoreCase);
            return alreadyOpen
                ? "**ONLINE PREPARATION** is on.\n\nChanged model and texture files are sent to CSDK automatically while you work.\n\nCSDK is already open, so Deadlimit will not open another copy. SHIFT-click again to turn online preparation off."
                : "**ONLINE PREPARATION** is on.\n\nChanged model and texture files are sent to CSDK automatically while you work.\n\nCSDK will open now. SHIFT-click again to turn online preparation off.";
        }
        if (text.StartsWith("ОНЛАЙН-ПОДГОТОВКА активна.", StringComparison.Ordinal))
        {
            var alreadyOpen = text.Contains("Уже запущенный CSDK", StringComparison.OrdinalIgnoreCase);
            return alreadyOpen
                ? "**ОНЛАЙН-ПОДГОТОВКА** включена.\n\nИзменённые файлы модели и текстуры автоматически передаются в CSDK во время работы.\n\nCSDK уже открыт, поэтому Deadlimit не будет запускать ещё одну копию. Повторный SHIFT-клик выключит режим."
                : "**ОНЛАЙН-ПОДГОТОВКА** включена.\n\nИзменённые файлы модели и текстуры автоматически передаются в CSDK во время работы.\n\nCSDK сейчас откроется. Повторный SHIFT-клик выключит режим.";
        }

        if (TryRewriteActionResult(text, out var actionResult))
        {
            return actionResult;
        }

        if (text.Contains("watcher error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sync failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ошибка наблюдения за файлами ОНЛАЙН-ПОДГОТОВКИ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ошибка онлайн-синхронизации", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "**ОНЛАЙН-ПОДГОТОВКА** не смогла продолжить автоматическое обновление файлов.\n\nВыполните **ПОДГОТОВИТЬ ДЛЯ CSDK** один раз, затем снова включите онлайн-подготовку."
                : "**ONLINE PREPARATION** could not continue updating files automatically.\n\nRun **PREPARE FOR CSDK** once, then turn online preparation on again.";
        }
        if (text.Contains("new, deleted, or renamed root DMX/texture file", StringComparison.OrdinalIgnoreCase)
            || text.Contains("новый, удалённый или переименованный DMX/файл текстуры", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "В проекте был добавлен, удалён или переименован файл модели или текстуры.\n\nDeadlimit автоматически подготовит проект заново перед продолжением обновлений в CSDK."
                : "A model or texture file was added, deleted or renamed in the project.\n\nDeadlimit will prepare the project again automatically before continuing CSDK updates.";
        }
        if (text.Contains("changed material references", StringComparison.OrdinalIgnoreCase)
            || text.Contains("изменённые ссылки на материалы", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Материалы, используемые моделью, изменились.\n\nDeadlimit подготовит проект заново перед следующим автоматическим обновлением CSDK."
                : "The materials used by the model changed.\n\nDeadlimit will prepare the project again before the next automatic CSDK update.";
        }
        if (text.Contains("cannot match", StringComparison.OrdinalIgnoreCase)
            || text.Contains("has no prepared DMX target", StringComparison.OrdinalIgnoreCase)
            || text.Contains("не может сопоставить", StringComparison.OrdinalIgnoreCase)
            || text.Contains("нет подготовленного целевого DMX", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Deadlimit не смог безопасно обновить один из файлов модели.\n\nВыполните **ПОДГОТОВИТЬ ДЛЯ CSDK** один раз, затем продолжайте работу."
                : "Deadlimit could not safely update one of the model files.\n\nRun **PREPARE FOR CSDK** once, then continue working.";
        }
        if (text.Contains("kept the existing prepared DMX unchanged because a full PREPARE is already required", StringComparison.OrdinalIgnoreCase)
            || text.Contains("сохранила текущий подготовленный DMX без изменений", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Deadlimit сохранил последнюю рабочую версию модели в CSDK, пока проект готовится заново.\n\nПосле подготовки автоматические обновления продолжатся."
                : "Deadlimit kept the last working model version in CSDK while the project is prepared again.\n\nAutomatic updates will continue afterward.";
        }
        if (text.Contains("Waiting for a valid Vertex Color source pair", StringComparison.OrdinalIgnoreCase)
            || text.Contains("until its Vertex Color source is safe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ожидается корректная пара исходников Vertex Color", StringComparison.OrdinalIgnoreCase)
            || text.Contains("до получения безопасного исходника Vertex Color", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Deadlimit ждёт подходящий файл **Vertex Color** для обновлённой модели.\n\nДо этого в CSDK остаётся последняя рабочая версия модели."
                : "Deadlimit is waiting for the matching **Vertex Color** file for the updated model.\n\nUntil then, CSDK keeps the last working model version.";
        }
        if (text.Contains("detected removal of", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Vertex Color sidecar removal", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Файл **Vertex Color** для модели был удалён.\n\nDeadlimit оставил в CSDK последнюю рабочую версию и ждёт новый соответствующий Vertex Color файл."
                : "The model's **Vertex Color** file was removed.\n\nDeadlimit kept the last working CSDK version and is waiting for a new matching Vertex Color file.";
        }
        if (TryRewriteSynchronizedFile(text, out var synchronized))
        {
            return synchronized;
        }

        if (text.Contains("game\\citadel", StringComparison.OrdinalIgnoreCase))
        {
            var valid = text.Contains("valid", StringComparison.OrdinalIgnoreCase)
                || text.Contains("валидной", StringComparison.OrdinalIgnoreCase);
            if (UiText.IsRussian)
            {
                return valid
                    ? "Выбранная папка является корректной установкой **Deadlock клиента**."
                    : "Эта папка не похожа на установленный **Deadlock клиент**.\n\nВыберите основную папку Project8Staging.";
            }
            return valid
                ? "The selected folder is a valid **Deadlock client** installation."
                : "This folder does not look like the installed **Deadlock client**.\n\nChoose the main Project8Staging folder.";
        }
        if (text.Contains("csdkcfg.exe was not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("не найден csdkcfg.exe", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Эта папка не похожа на установленный **Reduced CSDK**.\n\nВыберите основную папку Reduced CSDK."
                : "This folder does not look like a **Reduced CSDK** installation.\n\nChoose the main Reduced CSDK folder.";
        }
        if (text.Contains("local generation could not be identified", StringComparison.OrdinalIgnoreCase)
            || text.Contains("локальное поколение определить не удалось", StringComparison.OrdinalIgnoreCase)
            || text.Contains("локальное поколение определить", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "**Reduced CSDK** установлен корректно, но Deadlimit не смог определить его версию."
                : "**Reduced CSDK** looks valid, but Deadlimit could not identify its installed version.";
        }
        if (text.StartsWith("Installed CSDK generation:", StringComparison.OrdinalIgnoreCase))
        {
            return "Installed **Reduced CSDK** version: " + text["Installed CSDK generation:".Length..].Trim();
        }
        if (text.StartsWith("Установленное поколение CSDK:", StringComparison.OrdinalIgnoreCase))
        {
            return "Версия установленного **Reduced CSDK**: " + text["Установленное поколение CSDK:".Length..].Trim();
        }
        if (text.StartsWith("Installed CSDK ", StringComparison.OrdinalIgnoreCase)
            && text.Contains(" is available", StringComparison.OrdinalIgnoreCase))
        {
            return text.Replace("Installed CSDK", "Installed **Reduced CSDK**", StringComparison.OrdinalIgnoreCase);
        }
        if (text.StartsWith("Установлен CSDK ", StringComparison.OrdinalIgnoreCase)
            && text.Contains("доступен CSDK", StringComparison.OrdinalIgnoreCase))
        {
            return text.Replace("Установлен CSDK", "Установлен **Reduced CSDK**", StringComparison.OrdinalIgnoreCase)
                .Replace("доступен CSDK", "доступна версия", StringComparison.OrdinalIgnoreCase);
        }
        if (text.Contains("freshness could not be checked", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "Инструмент установлен, но Deadlimit не смог проверить наличие обновлений из-за ошибки сети."
                : "The tool is installed, but Deadlimit could not check for updates because the network source is unavailable.";
        }
        if (text.Contains("локальную версию определить не удалось", StringComparison.OrdinalIgnoreCase)
            || text.Contains("local version could not be identified", StringComparison.OrdinalIgnoreCase))
        {
            return UiText.IsRussian
                ? "**DeadlockTools** установлен, но Deadlimit не смог определить его версию."
                : "**DeadlockTools** is installed, but Deadlimit could not identify its version.";
        }

        if (text.StartsWith("Release ID determines the game-client VPK file name.", StringComparison.Ordinal))
        {
            return "**Release ID** chooses the numbered mod file used by this project in Deadlock.\n\nExample: ID 05 → pak05_dir.vpk.";
        }
        if (text.StartsWith("Release ID определяет имя VPK-файла", StringComparison.Ordinal))
        {
            return "**Release ID** выбирает номер файла мода, который использует этот проект в Deadlock.\n\nПример: ID 05 → pak05_dir.vpk.";
        }
        if (text.StartsWith("Release ID:", StringComparison.Ordinal)
            && text.Contains("Game-client VPK:", StringComparison.Ordinal))
        {
            return text.Replace("Game-client VPK:", "Mod file:", StringComparison.Ordinal)
                .Replace("Changing the ID changes the VPK slot/file name.", "Changing the ID changes which numbered mod file this project uses.", StringComparison.Ordinal);
        }
        if (text.StartsWith("Release ID:", StringComparison.Ordinal)
            && text.Contains("VPK игрового клиента Deadlock:", StringComparison.Ordinal))
        {
            return text.Replace("VPK игрового клиента Deadlock:", "Файл мода:", StringComparison.Ordinal)
                .Replace("Изменение ID меняет VPK-слот и имя файла.", "Изменение ID меняет номер файла мода, который использует этот проект.", StringComparison.Ordinal);
        }

        return text;
    }

    private static bool TryRewriteActionResult(string text, out string rewritten)
    {
        const string enFailedPrefix = "ONLINE PREPARATION kept its previous live-sync baseline because ";
        const string enFailedMarker = " did not finish a successful PREPARE transaction.";
        if (TryBetween(text, enFailedPrefix, enFailedMarker, out var enAction))
        {
            rewritten = $"**ONLINE PREPARATION** kept the last successful CSDK version because **{enAction}** did not finish successfully.\n\nYour last working files were left unchanged.";
            return true;
        }

        const string ruFailedPrefix = "ОНЛАЙН-ПОДГОТОВКА сохранила предыдущую базовую версию, потому что ";
        const string ruFailedMarker = " не завершилась успешной транзакцией PREPARE.";
        if (TryBetween(text, ruFailedPrefix, ruFailedMarker, out var ruAction))
        {
            rewritten = $"**ОНЛАЙН-ПОДГОТОВКА** сохранила последнюю рабочую версию CSDK, потому что **{ruAction}** не завершилось успешно.\n\nПоследние рабочие файлы оставлены без изменений.";
            return true;
        }

        const string enRefreshedPrefix = "ONLINE PREPARATION baseline refreshed after ";
        if (TryBetween(text, enRefreshedPrefix, ".", out enAction))
        {
            rewritten = $"**ONLINE PREPARATION** was refreshed after **{enAction}**.\n\nChanged model and texture files will continue to update in CSDK automatically.\n\nSHIFT-click **LAUNCH CSDK** to turn online preparation off.";
            return true;
        }

        const string ruRefreshedPrefix = "Базовая версия ОНЛАЙН-ПОДГОТОВКИ обновлена после ";
        if (TryBetween(text, ruRefreshedPrefix, ".", out ruAction))
        {
            rewritten = $"**ОНЛАЙН-ПОДГОТОВКА** обновлена после **{ruAction}**.\n\nИзменённые файлы модели и текстуры продолжат автоматически обновляться в CSDK.\n\nSHIFT-клик по **ЗАПУСК CSDK** выключит онлайн-подготовку.";
            return true;
        }

        const string enRefreshErrorPrefix = "ONLINE PREPARATION could not refresh its baseline after ";
        if (text.StartsWith(enRefreshErrorPrefix, StringComparison.Ordinal))
        {
            var action = TakeUntil(text[enRefreshErrorPrefix.Length..], ':');
            rewritten = $"**ONLINE PREPARATION** could not refresh after **{action}**.\n\nAutomatic updating is still using the previous successful project state.";
            return true;
        }

        const string ruRefreshErrorPrefix = "Не удалось обновить базовую версию ОНЛАЙН-ПОДГОТОВКИ после ";
        if (text.StartsWith(ruRefreshErrorPrefix, StringComparison.Ordinal))
        {
            var action = TakeUntil(text[ruRefreshErrorPrefix.Length..], '.');
            rewritten = $"Не удалось обновить **ОНЛАЙН-ПОДГОТОВКУ** после **{action}**.\n\nАвтоматическое обновление продолжает использовать предыдущую рабочую версию проекта.";
            return true;
        }

        rewritten = string.Empty;
        return false;
    }

    private static bool TryRewriteSynchronizedFile(string text, out string rewritten)
    {
        string? file = null;
        if (text.Contains("synchronized Vertex Color source:", StringComparison.OrdinalIgnoreCase))
        {
            file = TakeFileAfter(text, "synchronized Vertex Color source:");
        }
        else if (text.Contains("synchronized DMX:", StringComparison.OrdinalIgnoreCase))
        {
            file = TakeFileAfter(text, "synchronized DMX:");
        }
        else if (text.Contains("synchronized texture:", StringComparison.OrdinalIgnoreCase))
        {
            file = TakeFileAfter(text, "synchronized texture:");
        }
        else if (text.Contains("синхронизировала исходник Vertex Color:", StringComparison.OrdinalIgnoreCase))
        {
            file = TakeFileAfter(text, "синхронизировала исходник Vertex Color:");
        }
        else if (text.Contains("синхронизировала DMX:", StringComparison.OrdinalIgnoreCase))
        {
            file = TakeFileAfter(text, "синхронизировала DMX:");
        }
        else if (text.Contains("синхронизировала текстуру:", StringComparison.OrdinalIgnoreCase))
        {
            file = TakeFileAfter(text, "синхронизировала текстуру:");
        }

        if (file is null)
        {
            rewritten = string.Empty;
            return false;
        }

        rewritten = UiText.IsRussian
            ? $"Обновлено в **CSDK**: {file}"
            : $"Updated in **CSDK**: {file}";
        return true;
    }

    private static string TakeFileAfter(string text, string label)
    {
        var index = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var remainder = text[(index + label.Length)..].Trim();
        var detail = remainder.IndexOf(". Vertex Color", StringComparison.OrdinalIgnoreCase);
        if (detail >= 0)
        {
            remainder = remainder[..detail];
        }
        return remainder.Trim().TrimEnd('.');
    }

    private static bool TryBetween(string text, string prefix, string marker, out string value)
    {
        value = string.Empty;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var start = prefix.Length;
        var end = text.IndexOf(marker, start, StringComparison.Ordinal);
        if (end < start)
        {
            return false;
        }

        value = text[start..end].Trim();
        return value.Length > 0;
    }

    private static string TakeUntil(string text, char marker)
    {
        var index = text.IndexOf(marker);
        return (index >= 0 ? text[..index] : text).Trim();
    }
}
