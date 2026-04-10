# RtfTableExporter

![RtfTableExporter logo](icon.png)

Кроссплатформенная консольная утилита на .NET для экспорта таблиц из `RTF` и `DOCX` в `TXT`.

Программа автоматически определяет поддерживаемую форму отчета. Для формы `0531857` она выгружает секции доходов и расходов. Для формы `0503152` она выгружает основную таблицу отчета. Если форма не распознана, программа выгружает все содержательные таблицы документа по порядку.

## Что умеет

- Если входные пути не переданы, программа обрабатывает все `*.rtf` и `*.docx`, которые лежат рядом с исполняемым файлом.
- Автоматически распознает формы `0531857` и `0503152`.
- Для неизвестных форм выгружает все содержательные таблицы как обычный текст.
- Старый бинарный `.doc` напрямую не читается; такой файл нужно сохранить как `.docx` или `.rtf`.
- Можно передать один файл, несколько файлов, папку или wildcard-маску.
- Если выходной путь не задан, `txt` сохраняются рядом с исполняемым файлом.
- Если выходной путь задан как папка, все результаты складываются туда.
- Если обрабатывается ровно один файл, можно задать точный путь к итоговому `txt`.
- Готовые `txt` перезаписываются. Если конкретный файл перезаписать нельзя, ошибка уходит в `stderr`, а остальные файлы продолжают обрабатываться.
- Разделитель по умолчанию: `|`. Его можно заменить на любой свой, включая TAB.
- Выходной `txt` по умолчанию пишется в `cp1251` и с переносами строк `CRLF`.
- Кодировку можно переключить через `--encoding`: `cp1251`, `utf8`, `utf8-bom`.
- Для FoxPro можно использовать алиас `--foxpro`.
- Есть безопасная проверка обновлений из GitHub Releases. Ошибки обновления не валят основную обработку.
- Перед обработкой программа по умолчанию пишет файловый лог `RtfTableExporter.log` рядом с исполняемым файлом.

## Базовые примеры

```bash
RtfTableExporter
RtfTableExporter file1.rtf file2.docx
RtfTableExporter --input file1.rtf --input file2.docx
RtfTableExporter --input C:\data\
RtfTableExporter --input C:\data\*.rtf
RtfTableExporter --input C:\data\*.docx
RtfTableExporter --input file.rtf --output C:\result\
RtfTableExporter --input file.docx --output C:\result\file.txt
RtfTableExporter --input file.rtf --delimiter ";"
RtfTableExporter --input file.rtf --tab
RtfTableExporter --input file.rtf --foxpro
RtfTableExporter --input file.rtf --encoding utf8
RtfTableExporter --input file.rtf --encoding utf8-bom
RtfTableExporter --input file.rtf --log-path C:\logs\RtfTableExporter.log
RtfTableExporter --input file.rtf --no-file-log
```

## Логика вывода

`stdout`:

- `SAVED|<input>|<output>`
- `SUMMARY|<successCount>|<failureCount>`

`stderr`:

- `ERROR|<input>|<message>`
- `ERROR|<input>|<output>|<message>`
- `UPDATE|<message>`

## Файловый лог

По умолчанию на каждом запуске создается или дописывается файл `RtfTableExporter.log` рядом с программой.

Что логируется:

- запуск приложения, версия, PID, рабочая папка и аргументы
- параметры команды
- поиск входных файлов
- старт и результат обработки каждого файла
- ошибки по файлам
- этапы автообновления
- итоговый код завершения

Формат строки:

```text
2026-04-10T13:45:12.3456789+07:00 | INFO | Application started. | pid=1234 | version=1.0.13
```

Особенности:

- ошибки записи в лог никогда не прерывают обработку файлов
- при росте файла больше 5 МБ основной лог переносится в `RtfTableExporter.log.1`
- если нужен свой путь, используйте `--log-path`
- если файловый лог в конкретном запуске не нужен, используйте `--no-file-log`

Примеры:

```bash
RtfTableExporter report.rtf --log-path C:\logs\RtfTableExporter.log
RtfTableExporter report.rtf --no-file-log
```

## Коды возврата

- `0` все файлы обработаны успешно
- `1` ошибка аргументов
- `2` входные `rtf`/`docx` не найдены
- `3` все обработки завершились ошибкой
- `4` фатальная ошибка приложения
- `5` частичный успех: часть файлов обработана, часть нет

## Вызов из другой программы

Рекомендуемый сценарий:

1. Запустить программу с явными `--input` и `--output`.
2. Читать `stdout` построчно и забирать строки `SAVED|...`.
3. Читать `stderr` как журнал ошибок.
4. Проверять `ExitCode`.
5. Для интеграционного запуска отключать автообновление ключом `--no-update-check`.

Для пакетного режима это удобнее, чем один-единственный путь в выводе, потому что на одном запуске может быть обработано сразу несколько файлов.

Если результат потом читает Visual FoxPro, можно ничего не добавлять: по умолчанию используется `cp1251`. Для явного режима можно запускать так:

```bash
RtfTableExporter --input file.rtf --foxpro --no-update-check
```

## FoxPro на Linux/Wine

Если Visual FoxPro работает под Linux через Wine, рекомендуемый сценарий такой:

- Использовать именно Windows-сборку `RtfTableExporter.exe`, а не native Linux binary.
- Для FoxPro/Wine по умолчанию брать релиз `win-x86`.
- Использовать тот же `WINEPREFIX`, в котором запущен FoxPro.
- Передавать только явные абсолютные пути в формате Wine, например `Q:\...` или `Z:\...`.
- Всегда передавать `--input`, `--output`, `--foxpro`, `--no-update-check`.
- Для диагностики сохранять `stdout` и `stderr` в отдельные файлы.

Пример для FoxPro под Linux/Wine:

```foxpro
LOCAL loShell, lcExe, lcInput, lcOutput, lcOutLog, lcErrLog, lcCmd, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcExe = "Z:\opt\RtfTableExporter\RtfTableExporter.exe"
lcInput = "Q:\Только для обмена документами\report.rtf"
lcOutput = "Q:\Только для обмена документами\report.txt"
lcOutLog = "Z:\tmp\RtfTableExporter-out.log"
lcErrLog = "Z:\tmp\RtfTableExporter-err.log"

lcCmd = [cmd /c ""] + lcExe + ;
    [" --no-update-check --foxpro --input "] + lcInput + ;
    [" --output "] + lcOutput + ;
    [" > "] + lcOutLog + ;
    [" 2> "] + lcErrLog + [""]

lnExitCode = loShell.Run(lcCmd, 0, .T.)
```

Что проверить, если FoxPro “ничего не делает”:

1. Используется ли `win-x86`, а не `win-x64`.
2. Совпадает ли `WINEPREFIX` у ручного запуска и у FoxPro.
3. Видит ли Wine-диск `Q:` или `Z:` именно внутри текущего prefix.
4. Передаются ли явные `--input` и `--output`.
5. Нет ли ошибки в `RtfTableExporter.log`, `RtfTableExporter-out.log` или `RtfTableExporter-err.log`.

Мини-диагностика:

```foxpro
loShell.Run([cmd /c dir "Q:\Только для обмена документами" > "Z:\tmp\fx-dir.txt" 2>&1], 0, .T.)
loShell.Run([cmd /c "] + lcExe + [" --help > "Z:\tmp\fx-help.txt" 2>&1], 0, .T.)
```

## Как запускать без окна

Сам `RtfTableExporter` не прячет консольное окно изнутри. Для запуска из другой программы правильнее скрывать окно на стороне вызывающего процесса.

Windows, .NET:

```csharp
using System.Diagnostics;

var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = @"C:\tools\RtfTableExporter.exe",
        Arguments = @"--foxpro --no-update-check --input ""C:\data\report.rtf"" --output ""C:\out\report.txt""",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }
};

process.Start();
var stdout = process.StandardOutput.ReadToEnd();
var stderr = process.StandardError.ReadToEnd();
process.WaitForExit();
```

Windows, Visual FoxPro:

```foxpro
LOCAL loShell, lcExe, lcInput, lcOutput, lcCommand, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcExe = "C:\tools\RtfTableExporter.exe"
lcInput = "C:\data\report.rtf"
lcOutput = "C:\out\report.txt"
lcCommand = ["] + lcExe + [" --foxpro --no-update-check --input "] + lcInput + ;
    [" --output "] + lcOutput + [""]

* 0 = скрытое окно, .T. = ждать завершения
lnExitCode = loShell.Run(lcCommand, 0, .T.)
```

Windows, PowerShell:

```powershell
$p = Start-Process `
  -FilePath "C:\tools\RtfTableExporter.exe" `
  -ArgumentList '--foxpro --no-update-check --input "C:\data\report.rtf" --output "C:\out\report.txt"' `
  -WindowStyle Hidden `
  -RedirectStandardOutput "C:\temp\rtf-out.log" `
  -RedirectStandardError "C:\temp\rtf-err.log" `
  -PassThru `
  -Wait

$p.ExitCode
```

Linux:

На Linux отдельного консольного окна обычно нет. Если не нужно занимать терминал, запускайте процесс в фоне или из сервиса:

```bash
nohup ./RtfTableExporter --no-update-check --input "/home/user/data/report.rtf" --output "/home/user/out/report.txt" >rtf-out.log 2>rtf-err.log &
```

## Автообновление

Автообновление работает так:

- приложение смотрит последний GitHub Release
- ищет архив под текущую платформу
- скачивает его
- применяет обновление до обработки файлов
- запускает новую версию с теми же аргументами и рабочей папкой
- если новая версия не подтвердила takeover, текущая версия продолжает обработку сама

Ошибки обновления только логируются. Основная обработка `RTF`/`DOCX` не прерывается.

Чтобы автообновление работало в локальной сборке, можно передать:

```bash
RtfTableExporter --github-repo owner/repo
```

или задать переменную среды:

```bash
RTF_TABLE_EXPORTER_GITHUB_REPOSITORY=owner/repo
```

В релизных бинарниках, собранных через GitHub Actions, репозиторий встраивается автоматически.

## Локальная публикация

PowerShell:

```powershell
.\publish-all.ps1 -Version 1.2.3 -GitHubRepository owner/repo
```

Bash:

```bash
./publish-all.sh Release 1.2.3 owner/repo
```

Скрипты публикуют self-contained single-file сборки для:

- `win-x64`
- `win-x86`
- `linux-x64`
- `linux-musl-x64`
- `linux-arm64`

## Документация

- Подробное использование: [docs/USAGE.md](docs/USAGE.md)
- Что попадает в релиз и как им пользоваться: [docs/RELEASE_OVERVIEW.md](docs/RELEASE_OVERVIEW.md)

## Тесты

Локальная проверка:

```bash
dotnet test RtfTableExporter.sln -c Release
```

В тестах есть regression-сценарии для формы `0503152`, выбор стратегии разбора `RTF`, кодировки `cp1251`/`utf8-bom` и новые проверки CLI/логирования.
