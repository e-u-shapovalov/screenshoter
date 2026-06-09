# Screenshoter

Лёгкий фоновый трей‑апп для Windows: снимок **области** или **экрана под курсором**, сохранение в PNG и копирование в буфер **сразу и пути к файлу (текстом), и самой картинки**.

Путь удобно вставлять туда, где нужен путь к файлу (терминалы, CLI, редакторы, чаты); картинка — для вставки прямо в редакторы и чаты.

- Один файл **~16 КБ** — без установщика и без зависимостей.
- Собирается **встроенным компилятором .NET Framework** (есть в Windows 10/11 из коробки).
- Язык интерфейса: **русский по умолчанию**, английский — второй (переключается в трее).

## Возможности

- Захват области с затемнённым «замороженным» кадром и подписью размера.
- Снимок экрана того монитора, где курсор.
- PNG с таймстампом (`ГГГГ-ММ-ДД_ЧЧ-ММ-СС.png`) в выбранную папку.
- В буфер кладутся **и путь (текст), и картинка**.
- Живёт в трее, опциональный автозапуск, корректно работает с масштабированием (per‑monitor DPI).
- Переключение языка интерфейса RU/EN из меню.

## Горячие клавиши

| Клавиши | Действие |
|---------|----------|
| `Ctrl+Shift+1` | Снимок области (протяни выделение; `Esc`/ПКМ — отмена) |
| `Ctrl+Shift+3` | Снимок монитора под курсором |

Если клавиши уже заняты другим приложением (например, Яндекс.Диском) — отключи их там; Screenshoter повторяет регистрацию и перехватит их сам.

## Сборка

Нужен .NET Framework 4.x (уже есть в Windows 10/11), SDK не требуется.

```powershell
./build.ps1
```

Компилирует `Screenshoter.exe` встроенным компилятором
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

## Меню в трее (ПКМ по иконке)

- Снимок области / Снимок экрана
- Открыть папку скриншотов
- Изменить папку скриншотов… (запоминается)
- Добавить в автозапуск / Убрать из автозапуска
- Language → Русский / English
- О программе (GitHub)
- Выход

Двойной клик по иконке = снимок области.

## Папка скриншотов

По умолчанию `%USERPROFILE%\Screenshots`. Меняется из меню; выбор хранится в `%APPDATA%\Screenshoter\folder.txt` (язык — в `lang.txt`).

## Автозапуск

Через ярлык в папке Автозагрузки (`shell:startup\Screenshoter.lnk`), включается/выключается из меню трея.

## Лицензия

[MIT](LICENSE) © 2026 Evgenii Shapovalov

---

# Screenshoter (English)

A lightweight Windows tray app: capture a screen **region** or the **monitor under the cursor**, save it as a PNG, and put **both the file path (as text) and the image** on the clipboard.

The path is handy for pasting into anything that takes a file path (terminals, CLIs, editors, chats); the image is there for pasting straight into editors and chats.

- Single **~16 KB** executable — no installer, no dependencies.
- Builds with the **in‑box .NET Framework C# compiler** (preinstalled on Windows 10/11).
- UI language: **Russian by default**, English as a second language (switch in the tray).

## Features

- Region capture with a dimmed freeze‑frame overlay and a live size readout.
- Full‑screen capture of the monitor under the cursor.
- Timestamped PNG (`yyyy-MM-dd_HH-mm-ss.png`) saved to a folder you choose.
- Clipboard gets **both** the file path (text) **and** the image.
- System‑tray app, optional autostart, per‑monitor‑DPI aware.
- Switch UI language RU/EN from the menu.

## Hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Shift+1` | Capture a region (drag to select, `Esc`/right‑click to cancel) |
| `Ctrl+Shift+3` | Capture the monitor under the cursor |

If another app already owns these hotkeys (e.g. Yandex.Disk), free them in that app — Screenshoter keeps retrying registration and grabs them automatically.

## Build

Requires .NET Framework 4.x (already on Windows 10/11). No SDK needed.

```powershell
./build.ps1
```

## Tray menu

- Capture region / Capture screen
- Open screenshots folder
- Change screenshots folder (remembered)
- Add to / Remove from startup
- Язык → Русский / English
- About (GitHub)
- Exit

## License

[MIT](LICENSE) © 2026 Evgenii Shapovalov
