# Установка, запуск и сборка Screenshoter

## Установка готовой версии

Screenshoter не требует установщика.

1. Скачайте готовый файл: [Screenshoter.exe](https://github.com/e-u-shapovalov/screenshoter/releases/latest/download/Screenshoter.exe).
2. Переместите его в постоянную папку, например:

```text
C:\Users\<ваше_имя>\Apps\Screenshoter\Screenshoter.exe
```

3. Запустите `Screenshoter.exe`.
4. Найдите значок программы в системном трее Windows.

Если вы обычный пользователь, не скачивайте `Source code` и не используйте `Code -> Download ZIP`. Эти варианты нужны для разработки.

## Запуск

- `Ctrl+Shift+1` - снимок области.
- `Ctrl+Shift+3` - снимок с задержкой: выделите область, укажите секунды, наведите курсор — по окончании обратного отсчёта область снимется сама.
- `Ctrl+Shift+2` - переключить путь в буфере для последнего снимка: убрать путь (оставить только картинку для чатов) или вернуть его обратно.
- Двойной клик по значку в трее - снимок области.
- Правая кнопка по значку в трее - меню программы.

## Автозапуск

Откройте меню Screenshoter в трее и выберите **Добавить в автозапуск**.

Программа создаст ярлык:

```text
shell:startup\Screenshoter.lnk
```

Чтобы отключить автозапуск, выберите **Убрать из автозапуска** в меню.

## Папка скриншотов

По умолчанию:

```text
%USERPROFILE%\Screenshots
```

Настройка хранится в:

```text
%APPDATA%\Screenshoter\folder.txt
```

Изменить папку можно через меню в трее.

## Удаление

1. Выйдите из Screenshoter через меню в трее.
2. Если включали автозапуск, сначала выберите **Убрать из автозапуска**.
3. Удалите `Screenshoter.exe` из папки, куда вы его положили.
4. При необходимости удалите настройки:

```text
%APPDATA%\Screenshoter
```

Скриншоты из выбранной папки автоматически не удаляются.

## Сборка из исходного кода

Требования:

- Windows;
- .NET Framework 4.x;
- PowerShell;
- встроенный C# compiler:

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

Команды:

```powershell
git clone https://github.com/e-u-shapovalov/screenshoter.git
cd screenshoter
.\build.ps1
.\Screenshoter.exe
```

`build.ps1` собирает WinForms-приложение из `Screenshoter.cs` и создает `Screenshoter.exe` в корне проекта.

Если PowerShell блокирует запуск скрипта, можно запустить так:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## Проверка сборки

После сборки:

1. Запустите `.\Screenshoter.exe`.
2. Убедитесь, что появился значок в трее.
3. Нажмите `Ctrl+Shift+1` и сделайте снимок области.
4. Проверьте PNG в папке скриншотов.
5. Вставьте буфер в чат или Paint, чтобы проверить картинку.
6. Вставьте буфер в Блокнот или терминал, чтобы проверить путь к файлу.

## Возможные проблемы

### `csc.exe` не найден

Проверьте наличие .NET Framework 4.x. Скрипт ожидает компилятор по пути:

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

### Горячие клавиши не работают

Сочетания могут быть заняты другой программой. Освободите `Ctrl+Shift+1` и `Ctrl+Shift+3` в Яндекс.Диске, OneDrive, ShareX, Lightshot, игровых оверлеях или похожих инструментах.

### Программа запущена, но ничего не видно

Это tray app. Проверьте область рядом с часами Windows и скрытые значки.

### Windows предупреждает о неизвестном издателе

Текущий бинарный файл не подписан code-signing сертификатом. Можно запустить файл из официального релиза или собрать его самостоятельно из исходного кода.

## English Quick Install

Download the ready-made executable from [GitHub Releases](https://github.com/e-u-shapovalov/screenshoter/releases/latest), run `Screenshoter.exe`, then use `Ctrl+Shift+1` for a region screenshot or `Ctrl+Shift+3` for a delayed capture with a countdown.

Do not download `Source code` unless you want to build the project yourself.
