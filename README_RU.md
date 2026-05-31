# zapret-gui

[English](README.md)

Графический интерфейс (WPF) для управления [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) на Windows.

Приложение автоматизирует установку службы, обновление списков и диагностику — без ручного запуска `service.bat` и выбора пунктов меню в консоли.

## Возможности

- **«Сделай красиво»** — одна кнопка: проверка обновлений zapret, перебор стратегий и установка первой рабочей службы
- **Выключить** — остановка и удаление службы zapret
- **Служба** — ручная установка/удаление, проверка статуса, выбор стратегии (`general*.bat`)
- **Списки** — обновление `ipset-all.txt` и системного файла `hosts`
- **Диагностика** — проверка окружения и тесты доступности (PowerShell)
- **Обновление zapret** — скачивание последнего ZIP с GitHub [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)
- **Обновление приложения** — через [Updatum](https://github.com/sn4k3/Updatum) и [GitHub Releases](https://github.com/primalcs/zapret-gui/releases)
- **Интерфейс** — русский и английский

## Требования

- Windows 10/11 (x64)
- Права **администратора** (приложение запрашивает их при старте)
- Установленный [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0) — если используется не self-contained сборка
- Папка с [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) (по умолчанию можно указать в настройках, например `C:\zapret`)

## Установка

1. Скачайте последний установщик с [Releases](https://github.com/primalcs/zapret-gui/releases):  
   `zapret-gui_win-x64_v{version}.exe`
2. Запустите установщик от имени администратора.
3. Укажите путь к папке zapret в разделе **Дополнительно**, если он отличается от стандартного.

## Быстрый старт

1. Скачайте и распакуйте [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) в папку (например `C:\zapret`).
2. Запустите **zapret-gui** от имени администратора.
3. В **Дополнительно** укажите путь к папке zapret.
4. Нажмите **Сделай красиво** — приложение обновит zapret при необходимости и подберёт рабочую стратегию.
5. При успехе вверху появится зелёный баннер; для отключения используйте **Выключить**.

Если автоматический подбор не сработал, откройте вкладку **Служба** и попробуйте другую стратегию вручную.

## Настройки

Файл `settings.json` создаётся рядом с исполняемым файлом приложения:

```json
{
  "ZapretPath": "C:\\zapret",
  "InstalledVersion": "0.0.0",
  "Language": "ru",
  "CheckAppUpdatesOnStartup": true,
  "SkippedAppUpdateVersion": ""
}
```

| Поле | Описание |
|------|----------|
| `ZapretPath` | Путь к папке zapret (`service.bat`, `general.bat`) |
| `Language` | `ru` или `en` |
| `CheckAppUpdatesOnStartup` | Проверять обновления zapret-gui при запуске |
| `SkippedAppUpdateVersion` | Версия приложения, которую пользователь пропустил |

## Два вида обновлений

| Что обновляется | Откуда | Как в GUI |
|-----------------|--------|-----------|
| **zapret-gui** (это приложение) | [primalcs/zapret-gui](https://github.com/primalcs/zapret-gui) | **Проверить обновление приложения** под заголовком или при старте |
| **zapret-discord-youtube** (движок обхода) | [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) | **Проверить обновление** / **Обновить** в разделе **Дополнительно** |

Обновление приложения скачивает установщик и запускает его в тихом режиме (`/VERYSILENT`), затем перезапускает GUI.

## Сборка из исходников

### Запуск для разработки

```powershell
dotnet run
```

### Сборка установщика локально

1. Установите [.NET SDK 11](https://dotnet.microsoft.com/download) и [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Из корня репозитория:

```cmd
scripts\build-release.cmd
```

Если PowerShell блокирует скрипты:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Опционально: `scripts\build-release.cmd -Version 0.1.1 -SelfContained`

Результат: `artifacts\installer\zapret-gui_win-x64_v{version}.exe`

### Публикация релиза

1. Увеличьте `<Version>` в `zapret-gui.csproj`.
2. Создайте и отправьте тег:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

3. GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)) соберёт установщик и прикрепит его к Release.

## Документация по zapret

Подробное описание структуры папки zapret, стратегий и вызовов `service.bat` — в [`docs/README.md`](docs/README.md).

## Связанные проекты

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — дистрибутив zapret для Windows
- [ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) — родственный проект обхода DPI
- [sn4k3/Updatum](https://github.com/sn4k3/Updatum) — библиотека автообновления
