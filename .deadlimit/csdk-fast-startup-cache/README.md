# CSDK Fast Startup Fix

## Что запускать

Запускайте двойным кликом:

```text
Fix CSDK Fast Startup.cmd
```

`Fix-CsdkFastStartup.ps1` вручную запускать не требуется. Это основной PowerShell-код,
который автоматически вызывается файлом `.cmd`. CMD-лаунчер:

- обходит локальное ограничение PowerShell Execution Policy только для этого запуска;
- оставляет консоль открытой после завершения;
- показывает понятный итог `CSDK fast-startup cache is ready` или сообщение об ошибке.

## Когда запускать

Запускайте фикс:

- после чистой установки Reduced CSDK12;
- после замены или обновления распакованных файлов Deadlock;
- если CSDK снова тратит несколько минут на `Updating dependency information`.

При нормальной быстрой работе CSDK повторять процедуру перед каждым запуском не нужно.

## Подготовка

1. Откройте **Deadlimit Manager → Settings**.
2. Укажите правильную папку **Reduced CSDK12**.
3. Нажмите **SAVE**.
4. Полностью закройте CSDK, Asset Browser, Hammer, ModelDoc, VConsole и остальные окна
   инструментов из этой установки.

Настройка читается из `%LOCALAPPDATA%\Deadlimit\settings.json`.

## Пошаговый запуск

1. Дважды кликните `Fix CSDK Fast Startup.cmd`.
2. Откроется консоль, затем временно запустятся базовые инструменты Citadel.
3. Не закрывайте появившиеся CSDK-окна и консоль. Генерация первого полного кеша может
   занять несколько минут.
4. Дождитесь строки `SUCCESS` и сообщения `CSDK fast-startup cache is ready`.
5. Нажмите любую клавишу, чтобы закрыть консоль.
6. Запустите CSDK обычным способом и проверьте скорость следующего старта.

Готовый кеш обычно находится здесь:

```text
Reduced_CSDK_12\game\citadel\addons\luaunlocker\readonly_tools_asset_info.bin
```

Успешный файл должен быть создан заново во время текущего запуска и иметь размер больше
1 MB. Временный процесс CSDK закроется автоматически.

## Что делает фикс и насколько это безопасно

Скрипт:

1. проверяет путь и структуру Reduced CSDK12;
2. отказывается работать, пока запущены окна этой CSDK;
3. сохраняет копию предыдущего кеша;
4. запускает Citadel с `-savereadonlyassets`;
5. ждёт полной инициализации AssetSystem и нового кеша;
6. закрывает только созданный им временный процесс;
7. восстанавливает предыдущий кеш при ошибке.

## Если появилась ошибка

- `settings were not found` или `Reduced CSDK12 is not configured` — сохраните путь в
  Deadlimit Manager Settings.
- `CSDK executable was not found` — выбрана неправильная папка Reduced CSDK12.
- `Close every CSDK12 window` — закройте перечисленные процессы и повторите запуск.
- `Timed out` или ранний выход CSDK — сохранён предыдущий кеш; скопируйте полный текст
  ошибки из консоли для диагностики.

## Для разработчиков: прямой запуск PowerShell

Обычным пользователям этот способ не нужен. `Fix-CsdkFastStartup.ps1` поддерживает явный
путь к CSDK для тестов и диагностики:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Fix-CsdkFastStartup.ps1 `
  -CsdkRootOverride "D:\Path\To\Reduced_CSDK_12"
```

## English quick start

Double-click `Fix CSDK Fast Startup.cmd`. Do not launch `Fix-CsdkFastStartup.ps1`
manually during normal use; it is the implementation invoked by the CMD launcher.

Before running, save the **Reduced CSDK12** path in Deadlimit Manager Settings and close
all CSDK tools. Wait for `SUCCESS`; the temporary Citadel process closes automatically.

The script backs up the previous cache and restores it if generation fails.
