namespace zapret_gui;

public static class Loc
{
    public static event EventHandler? Changed;

    public static AppLanguage Current { get; private set; } = AppLanguage.En;

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language)
            return;

        Current = language;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void SetLanguage(string code)
    {
        SetLanguage(code.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Ru
            : AppLanguage.En);
    }

    public static string LanguageCode =>
        Current == AppLanguage.Ru ? "ru" : "en";

    private static string Pick(string en, string ru) =>
        Current == AppLanguage.Ru ? ru : en;

    public static string WindowTitle => Pick("zapret-gui", "zapret-gui");

    public static string AppVersionLabel(string version) =>
        Pick($"App v{version}", $"Приложение v{version}");

    public static string CheckAppUpdate => Pick("Check app update", "Проверить обновление приложения");

    public static string AppUpdateTitle => Pick("Application update", "Обновление приложения");

    public static string AppVersionLatest(string version) =>
        Pick($"Application version {version} is up to date.", $"Версия приложения {version} актуальна.");

    public static string AppNewVersionAvailable(string version) =>
        Pick(
            $"A new application version {version} is available. Download and install now?",
            $"Доступна новая версия приложения {version}. Скачать и установить сейчас?");

    public static string AppDownloading => Pick("Downloading update...", "Загрузка обновления...");

    public static string AppDownloadProgress(double downloadedMb, double totalMb, double percent) =>
        Pick(
            $"{downloadedMb:0.##} / {totalMb:0.##} MB ({percent:0.#} %)",
            $"{downloadedMb:0.##} / {totalMb:0.##} МБ ({percent:0.#} %)");

    public static string AppUpdateCheckFailedTitle =>
        Pick("Application update check failed", "Ошибка проверки обновления приложения");

    public static string AppUpdateCheckFailedBody(string message) =>
        Pick(
            $"Failed to check for application updates.\n\n{message}",
            $"Не удалось проверить обновления приложения.\n\n{message}");

    public static string AppUpdateDownloadFailed =>
        Pick("Failed to download the update.", "Не удалось скачать обновление.");

    public static string Advanced => Pick("Advanced", "Дополнительно");

    public static string DoEverything => Pick("Do everything", "Сделай красиво");

    public static string DoEverythingSuccess => Pick("Success", "Сделано");

    public static string DoEverythingServiceFailed(int serviceNumber) =>
        Pick($"Service {serviceNumber} failed", $"Не удалось подключить {serviceNumber}");

    public static string DoEverythingFailed =>
        Pick("Failed to establish zapret", "Не удалось установить zapret");

    public static string TurnOff => Pick("Turn Off", "Выключить");

    public static string ServiceRunning(int serviceNumber) =>
        Pick($"Service {serviceNumber} is running", $"Сервис {serviceNumber} работает");

    public static string PathToZapret => Pick("Path to zapret:", "Путь к zapret:");

    public static string Browse => Pick("Browse...", "Обзор...");

    public static string CheckForUpdate => Pick("Check for update", "Проверить обновление");

    public static string Update => Pick("Update", "Обновить");

    public static string TabService => Pick("Service", "Служба");

    public static string TabLists => Pick("Lists", "Списки");

    public static string TabDiagnostics => Pick("Diagnostics", "Диагностика");

    public static string InstalledStrategyUnknown =>
        Pick("Installed strategy: (unknown)", "Установленная стратегия: (неизвестно)");

    public static string StrategyForInstall => Pick("Strategy for install:", "Стратегия для установки:");

    public static string InstallService => Pick("Install service", "Установить службу");

    public static string RemoveServices => Pick("Remove services", "Удалить службы");

    public static string CheckStatus => Pick("Check status", "Проверить статус");

    public static string IpSetUnknown =>
        Pick(@"lists\ipset-all.txt: (unknown)", @"lists\ipset-all.txt: (неизвестно)");

    public static string HostsUnknown =>
        Pick("System hosts file: (unknown)", "Системный файл hosts: (неизвестно)");

    public static string UpdateIpSetList => Pick("Update IPSet list", "Обновить список IPSet");

    public static string UpdateHostsFile => Pick("Update hosts file", "Обновить файл hosts");

    public static string ConnectivityTestsHint =>
        Pick(
            @"Connectivity tests open a separate PowerShell window (utils\test zapret.ps1).",
            @"Тесты связи открываются в отдельном окне PowerShell (utils\test zapret.ps1).");

    public static string RunDiagnostics => Pick("Run diagnostics", "Запустить диагностику");

    public static string RunConnectivityTests => Pick("Run connectivity tests", "Запустить тесты связи");

    public static string Cancel => Pick("Cancel", "Отмена");

    public static string Output => Pick("Output", "Вывод");

    public static string Running => Pick("Running...", "Выполняется...");

    public static string Checking => Pick("Checking...", "Проверка...");

    public static string Updating => Pick("Updating...", "Обновление...");

    public static string None => Pick("(none)", "(нет)");

    public static string Missing => Pick("(missing)", "(отсутствует)");

    public static string NoOutput => Pick("(no output)", "(нет вывода)");

    public static string SelectFolderDescription =>
        Pick("Select folder containing zapret", "Выберите папку с zapret");

    public static string StoredVersionLatest(string version) =>
        Pick($"Stored version is {version} — latest", $"Сохранённая версия {version} — актуальна");

    public static string NewVersionAvailable(string version) =>
        Pick($"New version {version} is available.", $"Доступна новая версия {version}.");

    public static string UpdatedSuccessfully => Pick("Updated successfully", "Обновление выполнено успешно");

    public static string IpSetListUpdated => Pick("IPSet list updated", "Список IPSet обновлён");

    public static string OperationCancelled => Pick("Operation cancelled.", "Операция отменена.");

    public static string TestsRunSeparateWindow =>
        Pick("Tests run in a separate PowerShell window.", "Тесты выполняются в отдельном окне PowerShell.");

    public static string InstalledStrategyInvalidFolder =>
        Pick("Installed strategy: (invalid zapret folder)", "Установленная стратегия: (неверная папка zapret)");

    public static string InstalledStrategy(string strategy) =>
        Pick($"Installed strategy: {strategy}", $"Установленная стратегия: {strategy}");

    public static string IpSetInvalidFolder =>
        Pick(@"lists\ipset-all.txt: (invalid zapret folder)", @"lists\ipset-all.txt: (неверная папка zapret)");

    public static string HostsInvalidFolder =>
        Pick("System hosts file: (invalid zapret folder)", "Системный файл hosts: (неверная папка zapret)");

    public static string IpSetTimestamp(string timestamp) =>
        Pick($@"lists\ipset-all.txt: {timestamp}", $@"lists\ipset-all.txt: {timestamp}");

    public static string HostsTimestamp(string timestamp) =>
        Pick($"System hosts file: {timestamp}", $"Системный файл hosts: {timestamp}");

    public static string LogLine(string path) =>
        Pick($"Log: {path}", $"Журнал: {path}");

    public static string OutputHeader(string action) =>
        Pick($"=== {action} ===", $"=== {action} ===");

    public static string AdminRequiredTitle => Pick("Administrator required", "Требуются права администратора");

    public static string AdminRequiredBody =>
        Pick(
            "zapret-gui must run as Administrator.\n\n" +
            "Right-click the executable and choose \"Run as administrator\".",
            "zapret-gui должен запускаться от имени администратора.\n\n" +
            "Щёлкните правой кнопкой по файлу и выберите «Запуск от имени администратора».");

    public static string UpdateCheckFailedTitle => Pick("Update check failed", "Ошибка проверки обновлений");

    public static string UpdateCheckFailedBody(string message) =>
        Pick($"Failed to check for updates.\n\n{message}", $"Не удалось проверить обновления.\n\n{message}");

    public static string SetPathFirst =>
        Pick("Set path to desired zapret location first", "Сначала укажите путь к папке zapret");

    public static string UpdateFailedTitle => Pick("Update failed", "Ошибка обновления");

    public static string UpdateFailedBody(string message) =>
        Pick($"Update failed.\n\n{message}", $"Не удалось обновить.\n\n{message}");

    public static string LatestReleaseNoZip =>
        Pick("Latest release does not include a .zip download.", "В последнем релизе нет .zip-архива для загрузки.");

    public static string ZapretCommandFailed => Pick("Zapret command failed", "Ошибка команды zapret");

    public static string SelectStrategyFirst =>
        Pick("Select a strategy .bat file first.", "Сначала выберите файл стратегии (.bat).");

    public static string BatNotInInstallList(string fileName) =>
        Pick(
            $"\"{fileName}\" is not in the service.bat install file list.",
            $"«{fileName}» отсутствует в списке установки service.bat.");

    public static string InvalidZapretFolderTitle => Pick("Invalid zapret folder", "Неверная папка zapret");

    public static string InvalidZapretFolderBody =>
        Pick(
            "The selected folder is not a valid zapret installation.\n\n" +
            "Both service.bat and general.bat must exist in the folder.",
            "Выбранная папка не является корректной установкой zapret.\n\n" +
            "В папке должны быть файлы service.bat и general.bat.");

    public static string UpdateHostsConfirm(string hostsPath) =>
        Pick(
            "This runs service.bat option 8, which may open Notepad with zapret hosts entries " +
            "for you to merge into the system hosts file:\n\n" +
            $"{hostsPath}\n\nContinue?",
            "Будет выполнен пункт 8 service.bat — может открыться Блокнот с записями hosts из zapret " +
            "для объединения с системным файлом hosts:\n\n" +
            $"{hostsPath}\n\nПродолжить?");

    public static string ConnectivityScriptNotFound(string scriptPath) =>
        Pick(
            $"Connectivity test script was not found:\n\n{scriptPath}",
            $"Скрипт тестов связи не найден:\n\n{scriptPath}");
}
