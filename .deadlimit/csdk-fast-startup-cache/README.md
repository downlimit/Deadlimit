# CSDK Fast Startup Fix

`Fix CSDK Fast Startup.cmd` rebuilds the persistent Source 2 AssetSystem cache used by Reduced CSDK12.

Run it after a clean Reduced CSDK12 installation, after replacing the extracted Deadlock files, or whenever CSDK starts spending several minutes on `Updating dependency information`.

The tool reads `CsdkRoot` from `%LOCALAPPDATA%\Deadlimit\settings.json`. Open Deadlimit Settings, configure **Reduced CSDK12**, and press **SAVE** before the first run.

Close all CSDK windows, then double-click `Fix CSDK Fast Startup.cmd`. The tool:

1. validates the configured CSDK installation;
2. preserves the previous cache until generation succeeds;
3. starts base Citadel tools with `-savereadonlyassets`;
4. waits for the full cache to be written;
5. closes only the temporary CSDK process it created;
6. restores the previous cache if generation fails.

The resulting file is normally written to:

```text
Reduced_CSDK_12\game\citadel\addons\luaunlocker\readonly_tools_asset_info.bin
```

## По-русски

`Fix CSDK Fast Startup.cmd` пересоздаёт постоянный кеш AssetSystem для Reduced CSDK12. Запускайте его после чистой установки CSDK, замены распакованных файлов Deadlock или если запуск снова надолго зависает на `Updating dependency information`.

Скрипт берёт путь `Reduced CSDK12` из настроек Deadlimit. Перед запуском закройте все окна CSDK. Предыдущий рабочий кеш сохраняется до успешного завершения и восстанавливается при ошибке.
