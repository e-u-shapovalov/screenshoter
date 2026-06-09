# Релизы Screenshoter

GitHub Releases - это место, где пользователи должны скачивать готовую программу. Для Screenshoter готовый файл называется **`Screenshoter.exe`**.

Страница последнего релиза: [github.com/e-u-shapovalov/screenshoter/releases/latest](https://github.com/e-u-shapovalov/screenshoter/releases/latest)

## Текущий релиз

### Screenshoter v1.0.0

- Дата публикации: 2026-06-09.
- Страница релиза: [Screenshoter v1.0.0](https://github.com/e-u-shapovalov/screenshoter/releases/tag/v1.0.0).
- Готовый файл: [Screenshoter.exe](https://github.com/e-u-shapovalov/screenshoter/releases/download/v1.0.0/Screenshoter.exe).
- Размер asset в релизе: 23552 bytes.
- SHA256 asset `Screenshoter.exe` для `v1.0.0`: `5e46997214c217f6c0f0e036507b985cedce0f86f98adc4484d020c8cf3fc526`.

Для обычного пользователя нужен именно `Screenshoter.exe`. Архивы `Source code (zip)` и `Source code (tar.gz)` GitHub добавляет автоматически; они нужны разработчикам.

## Что писать в релизе

Хороший release note должен отвечать на три вопроса:

- что это за программа;
- что скачать;
- как запустить.

Пример короткого текста для следующего релиза:

```markdown
## Screenshoter vX.Y.Z

Легкий скриншотер для Windows: снимок области (`Ctrl+Shift+1`) или монитора под курсором (`Ctrl+Shift+3`), сохранение PNG и копирование в буфер сразу картинки и пути к файлу.

### Скачать

Обычным пользователям нужен файл `Screenshoter.exe` в блоке Assets.

Не скачивайте `Source code`, если вы не собираете программу из исходников.

### Изменения

- ...

### Запуск

Скачайте `Screenshoter.exe` и запустите. Программа появится в системном трее Windows.

Если SmartScreen предупреждает о неизвестном издателе, это связано с отсутствием цифровой подписи. Можно запустить файл вручную или собрать его из исходного кода.
```

## Чеклист публикации нового релиза

1. Обновить версию в `Screenshoter.cs`:

```csharp
[assembly: AssemblyVersion("X.Y.Z.0")]
[assembly: AssemblyFileVersion("X.Y.Z.0")]
```

2. Собрать файл:

```powershell
.\build.ps1
```

3. Проверить запуск, трей, хоткеи, сохранение PNG и буфер обмена.
4. Создать git tag, например `vX.Y.Z`.
5. Создать GitHub Release для этого tag.
6. Загрузить в Assets готовый `Screenshoter.exe`.
7. В тексте релиза явно написать: **обычным пользователям скачивать `Screenshoter.exe`, не `Source code`**.
8. Проверить прямую ссылку:

```text
https://github.com/e-u-shapovalov/screenshoter/releases/latest/download/Screenshoter.exe
```

## Рекомендации для продвижения

- Добавить 2-3 изображения в README: выделение области, меню в трее, уведомление после сохранения.
- Добавить GitHub topics: `screenshot`, `screenshots`, `windows`, `winforms`, `screen-capture`, `tray-app`, `portable`, `png`, `clipboard`.
- Поддерживать английский `README.en.md`, потому что запросы `Windows screenshot tool`, `screen capture utility`, `region screenshot`, `copy screenshot path` приводят международных пользователей.
- В каждом релизе повторять простую инструкцию скачивания, потому что GitHub автоматически показывает рядом архивы исходного кода.
