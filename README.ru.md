# OmniHID Taskbar Battery Indicator

<div align="center">

[English](README.md) | **Русский**

[![Платформа](https://img.shields.io/badge/платформа-Windows%2010%20%7C%2011-0078D6.svg?style=flat-square&logo=windows)](https://microsoft.com)
[![Среда выполнения](https://img.shields.io/badge/.NET-Framework%204.8-512BD4.svg?style=flat-square&logo=dotnet)](#)
[![Зависимости](https://img.shields.io/badge/зависимости-Ноль%20(Native%20Win32)-brightgreen.svg?style=flat-square)](https://github.com/)
[![Движок](https://img.shields.io/badge/движок-OmniHID.Core-2ea44f.svg?style=flat-square)](https://github.com/nikpsov/omni-hid)
[![Лицензия](https://img.shields.io/badge/лицензия-MIT-blue.svg?style=flat-square)](LICENSE)

*Сверхлегкий виджет панели задач Windows и окно Fluent Flyout для беспроводных устройств на базе движка [OmniHID](https://github.com/nikpsov/omni-hid) (автономный UI — установка самого движка не требуется).*

<br/>

![Preview](preview.png)

</div>

---

## Обзор

**OmniHID Taskbar Battery Indicator** — это самостоятельный графический интерфейс (UI) на базе движка и библиотеки телеметрии [**OmniHID**](https://github.com/nikpsov/omni-hid) (`OmniHid.Core`). Приложение встраивается в панель задач Windows рядом с системным треем и отображает заряд беспроводных устройств в реальном времени (`🎧 85%  🖱️ 92%`).

> **Без лишней настройки**: Движок телеметрии уже встроен непосредственно в приложение. Отдельная установка самого движка OmniHID, консольной утилиты или драйверов не требуется — всё работает «из коробки» (в виде единого портативного EXE или инсталлятора).

Клик по виджету открывает окно **Fluent Flyout** в стиле Windows 11 со статусом зарядки (`⚡`), оставшимся временем работы и напряжением аккумулятора.

- **Сверхлегкий:** Потребляет ~15 МБ RAM и ~0% CPU (заменяет 500+ МБ вендорного софта вроде G HUB, Synapse и iCUE).
- **0 зависимостей:** Чистый C# (.NET 4.8) и нативные Win32 HID API — автономный исполняемый файл со встроенным движком OmniHID, без фоновых служб.
- **Не мешает в играх:** Автоматически скрывается в полноэкранных играх и видео.
- **Smart Dual-Mode:** Определяет зарядку по кабелю без дублирования устройств.
- **Уведомления:** Предупреждает системным тостом при разряде до ≤ 20%.

---

## Поддерживаемые устройства

Поддерживает мыши, клавиатуры, гарнитуры и геймпады основных брендов и контроллеров:  
**Logitech** (HID++ и Centurion), **Razer** (HyperSpeed), **Corsair**, **SteelSeries**, **HyperX**, **CompX / SinoWealth / Areson / YiChip MCU** (Lamzu, Pulsar, ARDOR, Akko и др.), **Sony DualSense** и **Xbox**.

> Полный список поддерживаемых моделей и профилей доступен в [репозитории OmniHID](https://github.com/nikpsov/omni-hid).

---

## Установка

- **Установщик:** Скачайте `omni-hid-taskbar-setup.exe` из раздела [Releases](https://github.com/nikpsov/omni-hid-taskbar-battery-indicator/releases).
- **Портативная версия:** Скачайте `.zip` из [Releases](https://github.com/nikpsov/omni-hid-taskbar-battery-indicator/releases) и запустите `OmniHidTaskbar.exe`.

### Сборка из исходников

SDK не требуется — собирается встроенным в Windows компилятором C# (`csc.exe`):

```cmd
git clone --recursive https://github.com/nikpsov/omni-hid-taskbar-battery-indicator.git
cd omni-hid-taskbar-battery-indicator
build.bat
```

Бинарники сохраняются в `bin\`. Для обновления протоколов из апстрима: `git submodule update --remote --merge`.

---

## Настройки и хранение данных

Настройки и каталог поддерживаемых устройств хранятся в папке `%APPDATA%\OmniHid` (или локально рядом с приложением в портативном режиме):
- **Настройки:** `%APPDATA%\OmniHid\settings.json`
- **Профили устройств:** `%APPDATA%\OmniHid\devices\` (`verified/` и `unverified/`)

Управлять настройками можно через контекстное меню виджета (правый клик) или напрямую в `settings.json`:

| Параметр | По умолчанию | Описание |
|---|---|---|
| `DisplayStyle` | `0` | `0` = Иконка + процент (`🎧 85%`), `1` = Только иконка батареи |
| `DisplayMode` | `0` | `0` = Виджет на панели задач, `1` = Только иконка в трее |
| `HideWhenDisconnected` | `true` | Скрывать виджет, если все девайсы спят или выключены |
| `RunOnStartup` | `false` | Автозапуск при входе в Windows |
| `PollIntervalSeconds` | `15` | Интервал опроса устройств на рабочем столе (в секундах) |
| `BackgroundPollIntervalSeconds` | `30` | Замедленный интервал опроса в полноэкранных играх и при блокировке |

---

## Управление

- **Левый клик:** Открыть / закрыть детальное окно Flyout (отображает статус батареи, вольтаж и бейдж `Unverified` для экспериментальных профилей).
- **Правый клик:** Открыть контекстное меню в стиле Fluent:
  - Переключение стиля отображения и режима (Виджет / Иконка в трее).
  - Управление видимостью отдельных устройств (подменю `Видимость устройств` / `Device Visibility`).
  - **Update Profiles from GitHub:** OTA-обновление каталога профилей устройств напрямую с GitHub без перезапуска и переустановки приложения.
  - Принудительное обновление данных и автозапуск при старте Windows.

---

## FAQ

<details>
<summary><b>Почему уровень заряда не меняется, когда мышь не двигается?</b></summary>
Для экономии батареи беспроводные мыши засыпают через пару минут простоя, отключая радиомодуль. Виджет показывает последнее полученное значение, пока мышь снова не пошевелят.
</details>

<details>
<summary><b>Как включить подробный лог для отладки?</b></summary>
Запустите <code>bin\OmniHidTaskbarDebug.exe</code> или запустите приложение с ключом <code>--debug</code>. Логи выводятся в консоль и сохраняются в файл <code>debug.log</code> (ротируется при достижении 1 МБ).
</details>

<details>
<summary><b>Безопасно ли использовать с античитами?</b></summary>
Да. OmniHID использует стандартные пользовательские API Windows HID (<code>CreateFile</code>, <code>HidD_GetFeature</code>). Утилита не внедряет DLL, не перехватывает память и не использует драйверы ядра.
</details>

---

## Лицензия

OmniHID Taskbar Battery Indicator распространяется под открытой лицензией [MIT](LICENSE).